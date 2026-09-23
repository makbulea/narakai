namespace ECommerce.Inventory.Contracts;

public sealed record ReservedLine(Guid ProductId, int Quantity);

public sealed record StockReservedPayload(
    Guid OrderId, IReadOnlyList<ReservedLine> Lines, DateTimeOffset ReservedAt);

public sealed record StockReservationFailedPayload(
    Guid OrderId, Guid ProductId, int Requested, int Available, string Reason, DateTimeOffset FailedAt);

public sealed record StockReleasedPayload(
    Guid OrderId, IReadOnlyList<ReservedLine> Lines, string Reason, DateTimeOffset ReleasedAt);

public sealed record StockUpdatedPayload(
    Guid ProductId, int AvailableQuantity, int ReservedQuantity, string Operation, DateTimeOffset UpdatedAt);
