using BuildingBlocks.Caching.Redis;
using BuildingBlocks.Core.Errors;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Payments.Contracts;
using ECommerce.Payments.Domain;
using ECommerce.Payments.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Payments.Application;

/// <summary>
/// Payment processing.
///
/// Idempotency here is not a nice-to-have: OrderService retries on timeout, and a retry
/// that charges again is the single worst bug this system could ship. Three layers
/// guard against it, in increasing order of authority:
///
///   1. Redis reservation — fast rejection of an obvious duplicate, no database round trip.
///   2. A lookup by idempotency key — returns the original result for a genuine retry.
///   3. A unique index on the key — wins the race when two requests slip past both checks.
///
/// Layer 3 alone would be correct. The other two exist so the common case is fast and
/// the response to a duplicate is the original outcome rather than an error.
/// </summary>
public sealed class PaymentService(
    PaymentDbContext db,
    IOutboxWriter outbox,
    FakePaymentProvider provider,
    IIdempotencyKeyStore idempotencyKeys,
    ILogger<PaymentService> logger)
{
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromHours(24);

    public async Task<PaymentResponse> ProcessAsync(ProcessPaymentRequest request, CancellationToken ct)
    {
        // Layer 2 first: an existing payment for this key is a retry, and the caller
        // wants the original outcome, not a fresh attempt.
        var existing = await db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdempotencyKey == request.IdempotencyKey, ct);

        if (existing is not null)
        {
            logger.LogInformation(
                "Idempotent replay for key {Key}: returning existing payment {PaymentId} ({Status})",
                request.IdempotencyKey, existing.Id, existing.Status);

            return PaymentResponse.From(existing);
        }

        // Layer 1: stop two concurrent requests both getting past the check above.
        var reserved = await idempotencyKeys.TryReserveAsync(
            request.IdempotencyKey, IdempotencyWindow, ct);

        if (!reserved)
        {
            // Another request holds the key right now. It may not have committed yet, so
            // there is nothing to return — tell the caller to retry rather than starting
            // a second charge.
            logger.LogWarning(
                "Idempotency key {Key} is held by an in-flight request", request.IdempotencyKey);

            throw new ConflictException(
                "A payment with this idempotency key is already being processed. Retry shortly.");
        }

        var payment = Payment.Create(
            request.OrderId, request.Amount, request.Currency, request.IdempotencyKey);

        db.Payments.Add(payment);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Layer 3 fired. Another request created the payment between our lookup and
            // our insert; return theirs.
            db.ChangeTracker.Clear();

            var winner = await db.Payments
                .AsNoTracking()
                .FirstAsync(p => p.IdempotencyKey == request.IdempotencyKey, ct);

            logger.LogInformation(
                "Lost idempotency race for key {Key}; returning payment {PaymentId}",
                request.IdempotencyKey, winner.Id);

            return PaymentResponse.From(winner);
        }

        // ---- Call the provider ------------------------------------------------
        ProviderResult result;
        try
        {
            result = await provider.ChargeAsync(payment.OrderId, payment.Amount, payment.Currency, ct);
        }
        catch (Exception ex) when (ex is TaskCanceledException or TimeoutException)
        {
            // We do not know whether the charge went through. Mark it failed so the
            // order does not hang, and log loudly — this is the case a real system
            // reconciles against the provider's settlement file.
            logger.LogError(ex,
                "Provider call timed out for payment {PaymentId}; marking failed pending reconciliation",
                payment.Id);

            await FailAndPublishAsync(payment, "provider_timeout", ct);
            return PaymentResponse.From(payment);
        }

        if (!result.Approved)
        {
            await FailAndPublishAsync(payment, result.DeclineReason ?? "declined", ct);
            return PaymentResponse.From(payment);
        }

        // Authorize then capture as two steps, mirroring how card processing actually
        // works and leaving a natural place to insert delayed capture later.
        payment.Authorize(result.TransactionId!);
        payment.Capture();

        outbox.Enqueue(
            Topics.PaymentEvents,
            EventTypes.PaymentSucceeded,
            payment.OrderId.ToString(),
            new PaymentSucceededPayload(
                payment.Id, payment.OrderId, payment.Amount, payment.Currency,
                payment.TransactionId!, payment.UpdatedAt));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Payment {PaymentId} captured for order {OrderId}", payment.Id, payment.OrderId);

        return PaymentResponse.From(payment);
    }

    public async Task<PaymentResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment", id);

        return PaymentResponse.From(payment);
    }

    public async Task<PaymentResponse> GetByOrderAsync(Guid orderId, CancellationToken ct)
    {
        var payment = await db.Payments
            .AsNoTracking()
            .Where(p => p.OrderId == orderId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Payment for order", orderId);

        return PaymentResponse.From(payment);
    }

    /// <summary>
    /// Refunds a captured payment. Idempotent by state: refunding an already-refunded
    /// payment returns it unchanged rather than moving money twice.
    /// </summary>
    public async Task<PaymentResponse> RefundAsync(Guid id, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Payment", id);

        if (payment.Status == PaymentStatus.Refunded)
        {
            logger.LogInformation("Payment {PaymentId} is already refunded; no action taken", id);
            return PaymentResponse.From(payment);
        }

        if (payment.Status != PaymentStatus.Captured)
            throw new BusinessRuleException(
                "refund_not_allowed",
                $"Only captured payments can be refunded; this one is {payment.Status}.");

        var result = await provider.RefundAsync(payment.TransactionId!, payment.Amount, ct);

        if (!result.Approved)
            throw new DownstreamServiceException(
                "PaymentProvider", $"Refund was rejected: {result.DeclineReason}");

        payment.Refund();

        outbox.Enqueue(
            Topics.PaymentEvents,
            EventTypes.PaymentRefunded,
            payment.OrderId.ToString(),
            new PaymentRefundedPayload(
                payment.Id, payment.OrderId, payment.Amount, payment.Currency, payment.UpdatedAt));

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Payment {PaymentId} refunded for order {OrderId}", payment.Id, payment.OrderId);

        return PaymentResponse.From(payment);
    }

    private async Task FailAndPublishAsync(Payment payment, string reason, CancellationToken ct)
    {
        payment.Fail(reason);

        outbox.Enqueue(
            Topics.PaymentEvents,
            EventTypes.PaymentFailed,
            payment.OrderId.ToString(),
            new PaymentFailedPayload(
                payment.Id, payment.OrderId, payment.Amount, payment.Currency, reason, payment.UpdatedAt));

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Payment {PaymentId} failed for order {OrderId}: {Reason}",
            payment.Id, payment.OrderId, reason);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
