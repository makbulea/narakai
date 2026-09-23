using BuildingBlocks.Core.Errors;
using ECommerce.Inventory.Domain;

namespace ECommerce.Inventory.Tests;

/// <summary>
/// Reservation invariants. The database enforces these too (CHECK constraints and a row
/// lock), but the domain must not depend on that: these tests are what guarantee the
/// rules hold before anything touches Postgres.
/// </summary>
public class InventoryReservationTests
{
    [Fact]
    public void New_item_has_everything_available_and_nothing_reserved()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 100);

        Assert.Equal(100, item.AvailableQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(100, item.TotalQuantity);
    }

    /// <summary>
    /// Reserving moves quantity between the two buckets; it does not create or destroy
    /// stock. Total must be unchanged.
    /// </summary>
    [Fact]
    public void Reserving_moves_quantity_from_available_to_reserved()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 100);

        item.Reserve(30);

        Assert.Equal(70, item.AvailableQuantity);
        Assert.Equal(30, item.ReservedQuantity);
        Assert.Equal(100, item.TotalQuantity);
    }

    [Fact]
    public void Reserving_exactly_all_available_stock_is_allowed()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 5);

        item.Reserve(5);

        Assert.Equal(0, item.AvailableQuantity);
        Assert.Equal(5, item.ReservedQuantity);
    }

    /// <summary>The central rule: stock can never go negative.</summary>
    [Fact]
    public void Reserving_more_than_available_is_rejected()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 5);

        var ex = Assert.Throws<BusinessRuleException>(() => item.Reserve(6));

        Assert.Equal("insufficient_stock", ex.Rule);
        Assert.Equal(5, item.AvailableQuantity);   // unchanged
        Assert.Equal(0, item.ReservedQuantity);
    }

    /// <summary>
    /// Already-reserved stock is not available to a second order. This is what stops two
    /// customers being sold the same last unit.
    /// </summary>
    [Fact]
    public void Reserved_stock_is_not_available_to_another_order()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);

        item.Reserve(10);

        Assert.Throws<BusinessRuleException>(() => item.Reserve(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reservation_quantity_must_be_positive(int quantity)
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);

        var ex = Assert.Throws<BusinessRuleException>(() => item.Reserve(quantity));
        Assert.Equal("invalid_quantity", ex.Rule);
    }

    [Fact]
    public void CanReserve_agrees_with_Reserve()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 3);

        Assert.True(item.CanReserve(3));
        Assert.False(item.CanReserve(4));
        Assert.False(item.CanReserve(0));
    }
}

/// <summary>Compensation: releasing stock after a failed payment or a cancellation.</summary>
public class InventoryReleaseTests
{
    [Fact]
    public void Releasing_returns_quantity_to_available()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 100);
        item.Reserve(40);

        item.Release(40);

        Assert.Equal(100, item.AvailableQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(100, item.TotalQuantity);
    }

    [Fact]
    public void Partial_release_is_allowed()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 100);
        item.Reserve(40);

        item.Release(15);

        Assert.Equal(75, item.AvailableQuantity);
        Assert.Equal(25, item.ReservedQuantity);
    }

    /// <summary>
    /// Guards against a duplicate compensation inflating stock. The service layer also
    /// drives release off reservation rows so this should be unreachable — belt and braces.
    /// </summary>
    [Fact]
    public void Releasing_more_than_reserved_is_rejected()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 100);
        item.Reserve(10);

        var ex = Assert.Throws<BusinessRuleException>(() => item.Release(11));

        Assert.Equal("release_exceeds_reserved", ex.Rule);
        Assert.Equal(90, item.AvailableQuantity);
        Assert.Equal(10, item.ReservedQuantity);
    }

    /// <summary>Committing is the only operation that reduces total physical stock.</summary>
    [Fact]
    public void Committing_removes_stock_permanently()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 100);
        item.Reserve(20);

        item.Commit(20);

        Assert.Equal(80, item.AvailableQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Equal(80, item.TotalQuantity);   // 20 units left the warehouse
    }

    [Fact]
    public void Cannot_commit_more_than_reserved()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 100);
        item.Reserve(5);

        Assert.Throws<BusinessRuleException>(() => item.Commit(6));
    }
}

public class InventoryAdjustmentTests
{
    [Fact]
    public void Increase_adds_to_available_only()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        item.Reserve(4);

        item.Increase(20);

        Assert.Equal(26, item.AvailableQuantity);
        Assert.Equal(4, item.ReservedQuantity);
    }

    /// <summary>
    /// Shrinkage cannot eat stock that has been promised to a customer. Allowing it
    /// would let an order be confirmed for goods that no longer exist.
    /// </summary>
    [Fact]
    public void Decrease_cannot_consume_reserved_stock()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        item.Reserve(8);   // 2 available, 8 reserved

        var ex = Assert.Throws<BusinessRuleException>(() => item.Decrease(3));

        Assert.Equal("insufficient_stock", ex.Rule);
        Assert.Equal(2, item.AvailableQuantity);
    }

    [Fact]
    public void Decrease_reduces_available_stock()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);

        item.Decrease(4);

        Assert.Equal(6, item.AvailableQuantity);
    }

    [Fact]
    public void Initial_quantity_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InventoryItem.Create(Guid.NewGuid(), -1));
    }
}

public class StockReservationTests
{
    [Fact]
    public void New_reservation_is_held()
    {
        var reservation = StockReservation.Hold(Guid.NewGuid(), Guid.NewGuid(), 3);

        Assert.True(reservation.IsHeld);
        Assert.Equal(ReservationStatus.Held, reservation.Status);
        Assert.Null(reservation.ResolvedAt);
    }

    /// <summary>
    /// Once released, a reservation is no longer held — which is what makes a repeated
    /// OrderCancelled event a no-op instead of a double release.
    /// </summary>
    [Fact]
    public void Released_reservation_is_no_longer_held()
    {
        var reservation = StockReservation.Hold(Guid.NewGuid(), Guid.NewGuid(), 3);

        reservation.MarkReleased();

        Assert.False(reservation.IsHeld);
        Assert.Equal(ReservationStatus.Released, reservation.Status);
        Assert.NotNull(reservation.ResolvedAt);
    }

    [Fact]
    public void Committed_reservation_is_no_longer_held()
    {
        var reservation = StockReservation.Hold(Guid.NewGuid(), Guid.NewGuid(), 3);

        reservation.MarkCommitted();

        Assert.False(reservation.IsHeld);
        Assert.Equal(ReservationStatus.Committed, reservation.Status);
    }
}
