using BuildingBlocks.Core.Errors;
using BuildingBlocks.Core.Paging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Orders.Contracts;
using ECommerce.Orders.Domain;
using ECommerce.Orders.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Orders.Application;

/// <summary>
/// Orchestrates order placement across four other services.
///
/// This is a saga, executed synchronously because the customer is waiting for an
/// answer. There is no distributed transaction: each step commits locally, and every
/// step that can fail after an earlier step has taken effect has a matching
/// compensating action.
///
///   1. Validate customer      — read only, nothing to compensate
///   2. Validate products      — read only, nothing to compensate
///   3. Calculate total        — local
///   4. Reserve inventory      — compensated by release
///   5. Process payment        — compensated by refund (and release)
///   6. Confirm                — terminal
///
/// The order row is saved before step 4 so that a crash mid-saga leaves a Pending order
/// that operations can see and resolve, rather than nothing at all.
/// </summary>
public sealed class OrderService(
    OrderDbContext db,
    IOutboxWriter outbox,
    CustomerServiceClient customers,
    ProductServiceClient products,
    InventoryServiceClient inventory,
    PaymentServiceClient payments,
    ILogger<OrderService> logger)
{
    public async Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        // ---- Step 1: the customer must exist and be allowed to buy -------------
        var customer = await customers.GetCustomerAsync(request.CustomerId, ct)
            ?? throw new BusinessRuleException(
                "unknown_customer", $"Customer {request.CustomerId} does not exist.");

        if (!string.Equals(customer.Status, "Active", StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException(
                "customer_not_active",
                $"Customer {request.CustomerId} is {customer.Status} and cannot place orders.");

        // ---- Step 2: every product must exist, be orderable, and match currency ----
        var productIds = request.Lines.Select(l => l.ProductId).ToList();
        var catalogue = await products.GetProductsAsync(productIds, ct);
        var byId = catalogue.ToDictionary(p => p.Id);

        var missing = productIds.Where(id => !byId.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException(
                "unknown_product",
                $"Unknown product(s): {string.Join(", ", missing)}.");

        var notOrderable = catalogue.Where(p => !p.IsOrderable).Select(p => p.Id).ToList();
        if (notOrderable.Count > 0)
            throw new BusinessRuleException(
                "product_not_orderable",
                $"Product(s) not available for ordering: {string.Join(", ", notOrderable)}.");

        var currency = request.Currency.Trim().ToUpperInvariant();
        var mismatched = catalogue.Where(p => p.Currency != currency).Select(p => p.Id).ToList();
        if (mismatched.Count > 0)
            // No FX conversion in this system. Mixing currencies on one order would
            // require a rate, a rate source, and a policy for when it was fixed.
            throw new BusinessRuleException(
                "currency_mismatch",
                $"Product(s) {string.Join(", ", mismatched)} are not priced in {currency}.");

        // ---- Step 3: build the order at server-side prices ---------------------
        var items = request.Lines
            .Select(l =>
            {
                var product = byId[l.ProductId];
                return OrderItem.Create(l.ProductId, product.Name, l.Quantity, product.Price);
            })
            .ToList();

        var order = Order.Create(request.CustomerId, currency, items);

        db.Orders.Add(order);

        outbox.Enqueue(
            Topics.OrderEvents,
            EventTypes.OrderCreated,
            order.Id.ToString(),
            new OrderCreatedPayload(
                order.Id, order.OrderNumber, order.CustomerId, order.TotalAmount, order.Currency,
                order.Items.Select(i => new OrderLine(
                    i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.TotalPrice)).ToList(),
                order.CreatedAt));

        // Commit the Pending order before touching anything external, so a crash in the
        // steps below leaves a visible record instead of a silent loss.
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderNumber} created for customer {CustomerId}: {Total} {Currency}",
            order.OrderNumber, order.CustomerId, order.TotalAmount, order.Currency);

        // ---- Step 4: reserve stock ---------------------------------------------
        var reservation = await inventory.ReserveAsync(
            new ReserveStockRequest(
                order.Id,
                order.Items.Select(i => new ReserveLine(i.ProductId, i.Quantity)).ToList()),
            ct);

        if (!reservation.Success)
        {
            // Nothing was reserved, so there is nothing to compensate — just cancel.
            await CancelInternalAsync(
                order, $"stock_unavailable:{reservation.FailureReason}", ct);

            logger.LogWarning(
                "Order {OrderNumber} cancelled: {Reason}", order.OrderNumber, reservation.FailureReason);

            return OrderResponse.From(order);
        }

        order.MarkStockReserved();
        await db.SaveChangesAsync(ct);

        // ---- Step 5: take the money --------------------------------------------
        // The idempotency key is derived from the order id, not random: if this call
        // times out and the saga is retried, PaymentService recognises the same key and
        // returns the original payment instead of charging twice.
        var idempotencyKey = $"order-{order.Id}";

        PaymentResponse payment;
        try
        {
            payment = await payments.ProcessAsync(
                new ProcessPaymentRequest(order.Id, order.TotalAmount, order.Currency, idempotencyKey), ct);
        }
        catch (DownstreamServiceException)
        {
            // We do not know whether the charge happened. Do NOT cancel — that could
            // release stock for an order the customer has actually paid for. Leave the
            // order in StockReserved for the PaymentSucceeded/PaymentFailed consumers,
            // which will resolve it when the event arrives.
            logger.LogError(
                "Payment call failed for order {OrderNumber}; leaving in {Status} for event resolution",
                order.OrderNumber, order.Status);
            throw;
        }

        order.MarkPaymentPending(payment.Id);
        await db.SaveChangesAsync(ct);

        if (!string.Equals(payment.Status, "Captured", StringComparison.OrdinalIgnoreCase))
        {
            // ---- Compensation: give the stock back ----------------------------
            await inventory.TryReleaseAsync(order.Id, "payment_failed", ct);

            order.MarkPaymentFailed(payment.FailureReason ?? "Payment was declined.");

            outbox.Enqueue(
                Topics.OrderEvents,
                EventTypes.OrderCancelled,
                order.Id.ToString(),
                new OrderCancelledPayload(
                    order.Id, order.OrderNumber, order.CustomerId,
                    $"payment_failed:{payment.FailureReason}", DateTimeOffset.UtcNow));

            await db.SaveChangesAsync(ct);

            logger.LogWarning(
                "Order {OrderNumber} failed payment: {Reason}", order.OrderNumber, payment.FailureReason);

            return OrderResponse.From(order);
        }

        // ---- Step 6: confirm ----------------------------------------------------
        order.MarkPaid(payment.Id);
        order.Confirm();

        outbox.Enqueue(
            Topics.OrderEvents,
            EventTypes.OrderConfirmed,
            order.Id.ToString(),
            new OrderConfirmedPayload(
                order.Id, order.OrderNumber, order.CustomerId,
                order.TotalAmount, order.Currency, order.PaymentId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Order {OrderNumber} confirmed", order.OrderNumber);

        return OrderResponse.From(order);
    }

    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException("Order", id);

        return OrderResponse.From(order);
    }

    /// <summary>
    /// Customer- or support-initiated cancellation. Releases stock if any is held.
    /// A Confirmed order cannot be cancelled — that is a refund, which PaymentService owns.
    /// </summary>
    public async Task<OrderResponse> CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException("Order", id);

        if (order.IsTerminal)
            throw new BusinessRuleException(
                "order_already_terminal",
                $"Order {order.OrderNumber} is already {order.Status} and cannot be cancelled.");

        var heldStock = order.HoldsInventory;

        await CancelInternalAsync(order, reason, ct);

        if (heldStock)
            await inventory.TryReleaseAsync(order.Id, $"order_cancelled:{reason}", ct);

        return OrderResponse.From(order);
    }

    /// <summary>
    /// Cancels and queues OrderCancelled in one transaction.
    ///
    /// The event is what makes compensation reliable: even if the synchronous release
    /// call above fails, InventoryService's OrderCancelled consumer will release the
    /// stock when the event lands.
    /// </summary>
    private async Task CancelInternalAsync(Order order, string reason, CancellationToken ct)
    {
        order.Cancel(reason);

        outbox.Enqueue(
            Topics.OrderEvents,
            EventTypes.OrderCancelled,
            order.Id.ToString(),
            new OrderCancelledPayload(
                order.Id, order.OrderNumber, order.CustomerId, reason, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<OrderResponse>> ListAsync(OrderQuery query, CancellationToken ct)
    {
        var q = db.Orders.AsNoTracking().Include(o => o.Items).AsQueryable();

        if (query.CustomerId is { } customerId)
            q = q.Where(o => o.CustomerId == customerId);

        if (query.Status is { } status)
            q = q.Where(o => o.Status == status);

        if (query.CreatedAfter is { } after)
            q = q.Where(o => o.CreatedAt >= after);

        if (query.CreatedBefore is { } before)
            q = q.Where(o => o.CreatedAt <= before);

        var total = await q.LongCountAsync(ct);
        if (total == 0)
            return PagedResult<OrderResponse>.Empty(query.Page, query.PageSize);

        var orders = await q
            .OrderByDescending(o => o.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<OrderResponse>(
            orders.Select(OrderResponse.From).ToList(), query.Page, query.PageSize, total);
    }

    /// <summary>
    /// A customer's order history, newest first. Served by ix_orders_customer_created.
    /// </summary>
    public Task<PagedResult<OrderResponse>> GetHistoryAsync(
        Guid customerId, PageRequest page, CancellationToken ct) =>
        ListAsync(
            new OrderQuery { CustomerId = customerId, Page = page.Page, PageSize = page.PageSize }, ct);
}
