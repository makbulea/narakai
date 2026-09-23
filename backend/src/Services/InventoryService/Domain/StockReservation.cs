namespace ECommerce.Inventory.Domain;

public enum ReservationStatus
{
    Held = 0,
    Released = 1,
    Committed = 2
}

/// <summary>
/// A reservation held for one order line.
///
/// Without this record, releasing stock after a failed payment would mean trusting the
/// caller to tell us how much to give back — and a duplicated OrderCancelled event
/// would release it twice. Storing the reservation makes release idempotent: we release
/// what this row says, once, and mark it Released.
/// </summary>
public class StockReservation
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private StockReservation() { }

    public static StockReservation Hold(Guid orderId, Guid productId, int quantity) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = orderId,
        ProductId = productId,
        Quantity = quantity,
        Status = ReservationStatus.Held,
        CreatedAt = DateTimeOffset.UtcNow
    };

    public void MarkReleased()
    {
        Status = ReservationStatus.Released;
        ResolvedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCommitted()
    {
        Status = ReservationStatus.Committed;
        ResolvedAt = DateTimeOffset.UtcNow;
    }

    public bool IsHeld => Status == ReservationStatus.Held;
}
