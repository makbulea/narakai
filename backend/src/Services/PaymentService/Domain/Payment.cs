using BuildingBlocks.Core.Errors;

namespace ECommerce.Payments.Domain;

public enum PaymentStatus
{
    /// <summary>Created, not yet sent to the provider.</summary>
    Pending = 0,

    /// <summary>Provider reserved the funds but has not moved them.</summary>
    Authorized = 1,

    /// <summary>Funds taken. This is what OrderService waits for.</summary>
    Captured = 2,

    Failed = 3,
    Refunded = 4
}

public class Payment
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public PaymentStatus Status { get; private set; }

    /// <summary>Reference from the provider. Null until the charge is attempted.</summary>
    public string? TransactionId { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>
    /// The caller-supplied key that makes this charge safe to retry.
    ///
    /// Persisted on the payment row rather than only in Redis: correctness here must not
    /// depend on a cache surviving. Redis is consulted first because it is faster, but
    /// the unique index on this column is what actually prevents a double charge.
    /// </summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>
    /// Optimistic concurrency token, mapped to the Postgres xmin system column via
    /// UseXminAsConcurrencyToken. Two people editing the same row no longer silently
    /// overwrite each other; the second save fails and the caller gets a 409.
    /// </summary>
    public uint Version { get; private set; }

    private Payment() { }

    public static Payment Create(Guid orderId, decimal amount, string currency, string idempotencyKey)
    {
        if (amount <= 0)
            throw new BusinessRuleException("invalid_amount", "Payment amount must be greater than zero.");

        var now = DateTimeOffset.UtcNow;

        return new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Amount = decimal.Round(amount, 2, MidpointRounding.ToEven),
            Currency = currency.ToUpperInvariant(),
            Status = PaymentStatus.Pending,
            IdempotencyKey = idempotencyKey,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Authorize(string transactionId)
    {
        EnsureStatus(PaymentStatus.Pending, PaymentStatus.Authorized);

        TransactionId = transactionId;
        Status = PaymentStatus.Authorized;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Capture()
    {
        EnsureStatus(PaymentStatus.Authorized, PaymentStatus.Captured);

        Status = PaymentStatus.Captured;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string reason)
    {
        if (Status is PaymentStatus.Captured or PaymentStatus.Refunded)
            throw new BusinessRuleException(
                "invalid_status_transition", $"A {Status} payment cannot be marked failed.");

        Status = PaymentStatus.Failed;
        FailureReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Only a captured payment can be refunded. Refunding an authorized-but-uncaptured
    /// payment is a void, which is a different provider operation with different fees.
    /// </summary>
    public void Refund()
    {
        EnsureStatus(PaymentStatus.Captured, PaymentStatus.Refunded);

        Status = PaymentStatus.Refunded;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool IsSuccessful => Status is PaymentStatus.Captured;

    private void EnsureStatus(PaymentStatus required, PaymentStatus target)
    {
        if (Status != required)
            throw new BusinessRuleException(
                "invalid_status_transition",
                $"Payment {Id} is {Status}; {target} requires {required}.");
    }
}
