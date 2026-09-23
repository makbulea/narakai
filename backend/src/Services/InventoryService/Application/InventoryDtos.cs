using ECommerce.Inventory.Domain;

namespace ECommerce.Inventory.Application;

public sealed record ReserveLine(Guid ProductId, int Quantity);

/// <summary>
/// Reserve every line of an order, or none of them.
///
/// All-or-nothing because a partially reserved order is not a state anyone can act on:
/// the customer did not ask for three of the five things they ordered.
/// </summary>
public sealed record ReserveStockRequest(Guid OrderId, IReadOnlyList<ReserveLine> Lines);

public sealed record ReserveStockResponse(
    bool Success, Guid OrderId, string? FailureReason,
    Guid? FailedProductId, int? Requested, int? Available);

public sealed record ReleaseStockRequest(Guid OrderId, string Reason);

public sealed record AdjustStockRequest(int Quantity, string? Reason);

public sealed record StockResponse(
    Guid ProductId, int AvailableQuantity, int ReservedQuantity,
    int TotalQuantity, DateTimeOffset UpdatedAt)
{
    public static StockResponse From(InventoryItem i) =>
        new(i.ProductId, i.AvailableQuantity, i.ReservedQuantity, i.TotalQuantity, i.UpdatedAt);
}
