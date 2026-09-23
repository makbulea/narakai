using ECommerce.Orders.Domain;

namespace ECommerce.Orders.Tests;

/// <summary>
/// Money arithmetic. These exist because a one-cent discrepancy between the line items
/// and the total is a support ticket, and because using double here would eventually
/// produce one.
/// </summary>
public class OrderTotalTests
{
    private static OrderItem Line(decimal unitPrice, int quantity) =>
        OrderItem.Create(Guid.NewGuid(), "Test product", quantity, unitPrice);

    [Fact]
    public void Total_is_the_sum_of_line_totals()
    {
        var order = Order.Create(Guid.NewGuid(), "EUR",
        [
            Line(10.00m, 2),   // 20.00
            Line(5.50m, 3),    // 16.50
            Line(0.99m, 1)     //  0.99
        ]);

        Assert.Equal(37.49m, order.TotalAmount);
    }

    [Fact]
    public void Line_total_is_unit_price_times_quantity()
    {
        var line = Line(12.34m, 7);
        Assert.Equal(86.38m, line.TotalPrice);
    }

    /// <summary>
    /// 0.1 has no exact binary representation. Summing it ten times as a double gives
    /// 0.9999999999999999; as a decimal it gives exactly 1.00. This is the whole reason
    /// money is decimal in this codebase.
    /// </summary>
    [Fact]
    public void Repeated_fractional_amounts_do_not_drift()
    {
        var order = Order.Create(Guid.NewGuid(), "EUR", [Line(0.10m, 10)]);

        Assert.Equal(1.00m, order.TotalAmount);
    }

    /// <summary>
    /// Banker's rounding: an exact half goes to the nearest EVEN digit, not always up.
    /// Chosen because rounding half away from zero biases every total upward, which
    /// across a million orders is real money in the wrong direction.
    ///
    /// 0.005 -> 0.00 (0 is even), 0.015 -> 0.02 (rounds to the even 2, not down to 1).
    /// </summary>
    [Theory]
    [InlineData(0.005, 1, 0.00)]
    [InlineData(0.015, 1, 0.02)]
    [InlineData(0.025, 1, 0.02)]
    [InlineData(0.035, 1, 0.04)]
    public void Exact_halves_round_to_even(decimal unitPrice, int quantity, decimal expected)
    {
        var line = Line(unitPrice, quantity);
        Assert.Equal(expected, line.TotalPrice);
    }

    [Theory]
    [InlineData(1.005, 2, 2.01)]
    [InlineData(2.675, 2, 5.35)]
    [InlineData(19.99, 3, 59.97)]
    public void Line_totals_round_to_two_places(decimal unitPrice, int quantity, decimal expected)
    {
        var line = Line(unitPrice, quantity);
        Assert.Equal(expected, line.TotalPrice);
    }

    [Fact]
    public void An_order_must_have_at_least_one_line()
    {
        var ex = Assert.Throws<BuildingBlocks.Core.Errors.BusinessRuleException>(
            () => Order.Create(Guid.NewGuid(), "EUR", []));

        Assert.Equal("empty_order", ex.Rule);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Line_quantity_must_be_positive(int quantity)
    {
        Assert.Throws<BuildingBlocks.Core.Errors.BusinessRuleException>(
            () => OrderItem.Create(Guid.NewGuid(), "Test", quantity, 10m));
    }

    [Fact]
    public void Order_number_is_prefixed_and_unique()
    {
        var a = Order.Create(Guid.NewGuid(), "EUR", [Line(1m, 1)]);
        var b = Order.Create(Guid.NewGuid(), "EUR", [Line(1m, 1)]);

        Assert.StartsWith("ORD-", a.OrderNumber);
        Assert.NotEqual(a.OrderNumber, b.OrderNumber);
    }
}
