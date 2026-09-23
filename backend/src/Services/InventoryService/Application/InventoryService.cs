using BuildingBlocks.Caching.Redis;
using BuildingBlocks.Core.Errors;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Inventory.Contracts;
using ECommerce.Inventory.Domain;
using ECommerce.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Inventory.Application;

/// <summary>
/// Stock operations.
///
/// Reservation is the only genuinely concurrency-sensitive path in this system, and it
/// is defended in three layers:
///
///  1. A Redis lock per product, taken before the transaction. This is an optimisation:
///     it keeps competing requests from piling into the database at all. It is NOT the
///     correctness guarantee — a Redis failover can hand the same lock to two holders.
///  2. SELECT ... FOR UPDATE inside a serialisable-enough transaction. This is the real
///     guarantee. Postgres serialises the row, so the second transaction reads the
///     first one's committed result rather than a stale copy.
///  3. A CHECK constraint on the column. Catches anything that bypasses the domain.
///
/// Layer 2 alone would be correct. Layer 1 exists because a flash sale on one product
/// otherwise turns into hundreds of connections queueing on one row.
/// </summary>
public sealed class InventoryService(
    InventoryDbContext db,
    IOutboxWriter outbox,
    IDistributedLock distributedLock,
    ILogger<InventoryService> logger)
{
    private static readonly TimeSpan LockExpiry = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    public async Task<StockResponse> GetStockAsync(Guid productId, CancellationToken ct)
    {
        var item = await db.InventoryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.ProductId == productId, ct)
            ?? throw new NotFoundException("Inventory", productId);

        return StockResponse.From(item);
    }

    /// <summary>
    /// Reserves every line or none. Called synchronously by OrderService, because the
    /// customer is waiting and needs to know now whether their order can be fulfilled.
    /// </summary>
    public async Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest request, CancellationToken ct)
    {
        // Already reserved for this order? Return success without touching stock.
        // OrderService retrying a timed-out call must not double-reserve.
        var existing = await db.Reservations
            .AsNoTracking()
            .Where(r => r.OrderId == request.OrderId && r.Status == ReservationStatus.Held)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            logger.LogInformation(
                "Order {OrderId} already holds {Count} reservation(s); treating reserve as idempotent",
                request.OrderId, existing.Count);

            return new ReserveStockResponse(true, request.OrderId, null, null, null, null);
        }

        // Deterministic lock order. Two orders containing the same two products in
        // opposite sequence would otherwise take the locks in opposite order and
        // deadlock — the classic ABBA. Sorting by id makes that impossible.
        var lines = request.Lines.OrderBy(l => l.ProductId).ToList();

        var locks = new List<IAsyncDisposable>();
        try
        {
            foreach (var line in lines)
            {
                var handle = await distributedLock.AcquireAsync(
                    $"inventory:{line.ProductId}", LockExpiry, LockWait, ct);

                if (handle is null)
                    throw new DownstreamServiceException(
                        "InventoryLock",
                        $"Could not acquire stock lock for product {line.ProductId} within {LockWait.TotalSeconds}s.");

                locks.Add(handle);
            }

            return await ReserveInTransactionAsync(request.OrderId, lines, ct);
        }
        finally
        {
            // Release in reverse order, and never let a release failure mask the real
            // outcome — the locks expire on their own if this goes wrong.
            for (var i = locks.Count - 1; i >= 0; i--)
                await locks[i].DisposeAsync();
        }
    }

    private async Task<ReserveStockResponse> ReserveInTransactionAsync(
        Guid orderId, List<ReserveLine> lines, CancellationToken ct)
    {
        // EnableRetryOnFailure's execution strategy will not allow a transaction it did
        // not open, so the whole unit runs through the strategy. See OutboxProcessor for
        // the same pattern and why replaying it is safe.
        var strategy = db.Database.CreateExecutionStrategy();

        var outcome = await strategy.ExecuteAsync(async () =>
            await TryReserveAsync(orderId, lines, ct));

        // The failure event is published AFTER the strategy block, on its own
        // transaction. Publishing inside would mean a retry emitted it twice.
        if (outcome.Failure is { } failure)
        {
            await PublishReservationFailedAsync(
                orderId, failure.ProductId, failure.Requested, failure.Available, failure.Reason, ct);

            return new ReserveStockResponse(
                false, orderId, failure.Message, failure.ProductId, failure.Requested, failure.Available);
        }

        return new ReserveStockResponse(true, orderId, null, null, null, null);
    }

    private sealed record ReservationFailure(
        Guid ProductId, int Requested, int Available, string Reason, string Message);

    private sealed record ReservationOutcome(ReservationFailure? Failure);

    private async Task<ReservationOutcome> TryReserveAsync(
        Guid orderId, List<ReserveLine> lines, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var reserved = new List<ReservedLine>();

        foreach (var line in lines)
        {
            // FOR UPDATE takes a row-level write lock held until commit. Any concurrent
            // transaction touching this product blocks here and then re-reads our
            // committed values, which is what makes the availability check trustworthy.
            var item = await db.InventoryItems
                .FromSql($"SELECT * FROM inventory_items WHERE product_id = {line.ProductId} FOR UPDATE")
                .FirstOrDefaultAsync(ct);

            if (item is null)
            {
                await tx.RollbackAsync(ct);

                return new ReservationOutcome(new ReservationFailure(
                    line.ProductId, line.Quantity, 0, "unknown_product",
                    "Product has no inventory record."));
            }

            if (!item.CanReserve(line.Quantity))
            {
                // Roll back everything reserved so far in this transaction. All-or-nothing.
                var available = item.AvailableQuantity;
                await tx.RollbackAsync(ct);

                logger.LogWarning(
                    "Reservation failed for order {OrderId}: product {ProductId} has {Available}, wanted {Requested}",
                    orderId, line.ProductId, available, line.Quantity);

                return new ReservationOutcome(new ReservationFailure(
                    line.ProductId, line.Quantity, available, "insufficient_stock",
                    $"Insufficient stock: {available} available, {line.Quantity} requested."));
            }

            item.Reserve(line.Quantity);
            db.Reservations.Add(StockReservation.Hold(orderId, line.ProductId, line.Quantity));
            reserved.Add(new ReservedLine(line.ProductId, line.Quantity));
        }

        outbox.Enqueue(
            Topics.InventoryEvents,
            EventTypes.StockReserved,
            orderId.ToString(),
            new StockReservedPayload(orderId, reserved, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Reserved {LineCount} line(s) for order {OrderId}", reserved.Count, orderId);

        return new ReservationOutcome(null);
    }

    /// <summary>
    /// Compensating action: hand the stock back.
    ///
    /// Called from two places — the HTTP endpoint when OrderService compensates
    /// synchronously, and the OrderCancelled consumer when it compensates
    /// asynchronously. Both paths must be safe to run twice, which is why the work is
    /// driven off held reservation rows rather than off quantities in the request.
    /// </summary>
    public async Task ReleaseAsync(Guid orderId, string reason, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => await ReleaseInTransactionAsync(orderId, reason, ct));
    }

    private async Task ReleaseInTransactionAsync(Guid orderId, string reason, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var held = await db.Reservations
            .Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Held)
            .ToListAsync(ct);

        if (held.Count == 0)
        {
            // Nothing held: either never reserved, or already released. Both are fine —
            // this is the idempotent no-op that lets a duplicate OrderCancelled through.
            await tx.CommitAsync(ct);

            logger.LogInformation(
                "Release for order {OrderId} found no held reservations; nothing to do", orderId);
            return;
        }

        var releasedLines = new List<ReservedLine>();

        foreach (var reservation in held)
        {
            var item = await db.InventoryItems
                .FromSql($"SELECT * FROM inventory_items WHERE product_id = {reservation.ProductId} FOR UPDATE")
                .FirstAsync(ct);

            item.Release(reservation.Quantity);
            reservation.MarkReleased();
            releasedLines.Add(new ReservedLine(reservation.ProductId, reservation.Quantity));
        }

        outbox.Enqueue(
            Topics.InventoryEvents,
            EventTypes.StockReleased,
            orderId.ToString(),
            new StockReleasedPayload(orderId, releasedLines, reason, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Released {Count} reservation(s) for order {OrderId}: {Reason}",
            releasedLines.Count, orderId, reason);
    }

    /// <summary>The order shipped. Reserved units leave stock permanently.</summary>
    public async Task CommitAsync(Guid orderId, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () => await CommitInTransactionAsync(orderId, ct));
    }

    private async Task CommitInTransactionAsync(Guid orderId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var held = await db.Reservations
            .Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Held)
            .ToListAsync(ct);

        foreach (var reservation in held)
        {
            var item = await db.InventoryItems
                .FromSql($"SELECT * FROM inventory_items WHERE product_id = {reservation.ProductId} FOR UPDATE")
                .FirstAsync(ct);

            item.Commit(reservation.Quantity);
            reservation.MarkCommitted();
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<StockResponse> IncreaseAsync(Guid productId, int quantity, CancellationToken ct)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.ProductId == productId, ct);

        // Goods can arrive for a product that has never been stocked before.
        if (item is null)
        {
            item = InventoryItem.Create(productId, 0);
            db.InventoryItems.Add(item);
        }

        item.Increase(quantity);
        await PublishStockUpdatedAndSaveAsync(item, "increase", ct);

        return StockResponse.From(item);
    }

    public async Task<StockResponse> DecreaseAsync(Guid productId, int quantity, CancellationToken ct)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.ProductId == productId, ct)
            ?? throw new NotFoundException("Inventory", productId);

        item.Decrease(quantity);
        await PublishStockUpdatedAndSaveAsync(item, "decrease", ct);

        return StockResponse.From(item);
    }

    private async Task PublishStockUpdatedAndSaveAsync(
        InventoryItem item, string operation, CancellationToken ct)
    {
        outbox.Enqueue(
            Topics.InventoryEvents,
            EventTypes.StockUpdated,
            item.ProductId.ToString(),
            new StockUpdatedPayload(
                item.ProductId, item.AvailableQuantity, item.ReservedQuantity,
                operation, item.UpdatedAt));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Publishes the failure on its own transaction — the reservation transaction has
    /// already been rolled back, so the outbox row from inside it is gone too.
    /// </summary>
    private async Task PublishReservationFailedAsync(
        Guid orderId, Guid productId, int requested, int available, string reason, CancellationToken ct)
    {
        outbox.Enqueue(
            Topics.InventoryEvents,
            EventTypes.StockReservationFailed,
            orderId.ToString(),
            new StockReservationFailedPayload(
                orderId, productId, requested, available, reason, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
    }
}
