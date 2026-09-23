namespace ECommerce.Payments.Contracts;

public sealed record PaymentSucceededPayload(
    Guid PaymentId, Guid OrderId, decimal Amount, string Currency,
    string TransactionId, DateTimeOffset CapturedAt);

public sealed record PaymentFailedPayload(
    Guid PaymentId, Guid OrderId, decimal Amount, string Currency,
    string Reason, DateTimeOffset FailedAt);

public sealed record PaymentRefundedPayload(
    Guid PaymentId, Guid OrderId, decimal Amount, string Currency, DateTimeOffset RefundedAt);
