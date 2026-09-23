namespace ECommerce.Orders.Contracts;

public sealed record OrderLine(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal TotalPrice);

public sealed record OrderCreatedPayload(
    Guid OrderId, string OrderNumber, Guid CustomerId,
    decimal TotalAmount, string Currency, IReadOnlyList<OrderLine> Lines, DateTimeOffset CreatedAt);

public sealed record OrderConfirmedPayload(
    Guid OrderId, string OrderNumber, Guid CustomerId,
    decimal TotalAmount, string Currency, Guid? PaymentId, DateTimeOffset ConfirmedAt);

public sealed record OrderCancelledPayload(
    Guid OrderId, string OrderNumber, Guid CustomerId, string Reason, DateTimeOffset CancelledAt);
