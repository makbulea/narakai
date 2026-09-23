using BuildingBlocks.Core.Errors;

namespace ECommerce.Inventory.Domain;

/// <summary>
/// Stock for one product.
///
/// Two quantities, and the split matters. AvailableQuantity is what can still be
/// promised to a new order. ReservedQuantity is what has been promised but not yet
/// shipped. Physical stock on the shelf is the sum of the two.
///
/// Keeping them apart is what stops two customers being sold the last unit: reserving
/// moves quantity from available to reserved *before* payment is attempted, so a
/// concurrent order sees the reduced availability.
/// </summary>
public class InventoryItem
{
    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public int AvailableQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // No optimistic concurrency token here, deliberately. Reservation already takes a
    // row lock (SELECT ... FOR UPDATE), which is a stronger guarantee; adding an
    // optimistic token on top would be redundant and, because the reservation path uses
    // raw SQL, would also break — "SELECT *" does not return Postgres system columns
    // such as xmin, so EF would look for a column that is not in the result set.

    private InventoryItem() { }

    public static InventoryItem Create(Guid productId, int initialQuantity)
    {
        if (initialQuantity < 0)
            throw new ArgumentOutOfRangeException(nameof(initialQuantity), "Initial quantity cannot be negative.");

        return new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AvailableQuantity = initialQuantity,
            ReservedQuantity = 0,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Total physical units, reserved or not.</summary>
    public int TotalQuantity => AvailableQuantity + ReservedQuantity;

    public bool CanReserve(int quantity) => quantity > 0 && AvailableQuantity >= quantity;

    /// <summary>
    /// Moves quantity from available to reserved.
    ///
    /// The guard here is the last line of defence, not the first: the caller has
    /// already taken a row lock. This exists so the invariant holds even if some future
    /// caller forgets, which is exactly the sort of thing that otherwise ships a
    /// negative stock bug to production.
    /// </summary>
    public void Reserve(int quantity)
    {
        if (quantity <= 0)
            throw new BusinessRuleException("invalid_quantity", "Reservation quantity must be positive.");

        if (AvailableQuantity < quantity)
            throw new BusinessRuleException(
                "insufficient_stock",
                $"Product {ProductId} has {AvailableQuantity} available but {quantity} were requested.");

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Compensation: a reservation is undone and the stock becomes sellable again.</summary>
    public void Release(int quantity)
    {
        if (quantity <= 0)
            throw new BusinessRuleException("invalid_quantity", "Release quantity must be positive.");

        if (ReservedQuantity < quantity)
            throw new BusinessRuleException(
                "release_exceeds_reserved",
                $"Cannot release {quantity} for product {ProductId}; only {ReservedQuantity} is reserved.");

        ReservedQuantity -= quantity;
        AvailableQuantity += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The order shipped. Reserved units leave the building and are not returned to
    /// available — this is the only operation that reduces total physical stock.
    /// </summary>
    public void Commit(int quantity)
    {
        if (ReservedQuantity < quantity)
            throw new BusinessRuleException(
                "commit_exceeds_reserved",
                $"Cannot commit {quantity} for product {ProductId}; only {ReservedQuantity} is reserved.");

        ReservedQuantity -= quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Goods received.</summary>
    public void Increase(int quantity)
    {
        if (quantity <= 0)
            throw new BusinessRuleException("invalid_quantity", "Increase quantity must be positive.");

        AvailableQuantity += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Shrinkage, damage, manual correction. Cannot touch reserved stock.</summary>
    public void Decrease(int quantity)
    {
        if (quantity <= 0)
            throw new BusinessRuleException("invalid_quantity", "Decrease quantity must be positive.");

        if (AvailableQuantity < quantity)
            throw new BusinessRuleException(
                "insufficient_stock",
                $"Cannot decrease by {quantity}; only {AvailableQuantity} is available.");

        AvailableQuantity -= quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
