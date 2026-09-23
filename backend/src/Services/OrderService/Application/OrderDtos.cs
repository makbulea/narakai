using BuildingBlocks.Core.Paging;
using ECommerce.Orders.Domain;

namespace ECommerce.Orders.Application;

public sealed record CreateOrderLine(Guid ProductId, int Quantity);

/// <summary>
/// Note what is NOT here: price. The client says what and how many; the server decides
/// what it costs. Accepting a client-supplied price is how shops get bought out at
/// one cent an item.
/// </summary>
public sealed record CreateOrderRequest(Guid CustomerId, string Currency, IReadOnlyList<CreateOrderLine> Lines);

public sealed record CancelOrderRequest(string Reason);

public sealed record OrderItemResponse(
    Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal TotalPrice);

public sealed record OrderResponse(
    Guid Id, string OrderNumber, Guid CustomerId, string Status,
    decimal TotalAmount, string Currency, string? CancellationReason,
    Guid? PaymentId, IReadOnlyList<OrderItemResponse> Items,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static OrderResponse From(Order o) => new(
        o.Id, o.OrderNumber, o.CustomerId, o.Status.ToString(),
        o.TotalAmount, o.Currency, o.CancellationReason, o.PaymentId,
        o.Items.Select(i => new OrderItemResponse(
            i.ProductId, i.ProductName, i.Quantity, i.UnitPrice, i.TotalPrice)).ToList(),
        o.CreatedAt, o.UpdatedAt);
}

public sealed record OrderQuery : PageRequest
{
    public Guid? CustomerId { get; init; }
    public OrderStatus? Status { get; init; }
    public DateTimeOffset? CreatedAfter { get; init; }
    public DateTimeOffset? CreatedBefore { get; init; }
}
