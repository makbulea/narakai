using BuildingBlocks.Core.Errors;
using ECommerce.Orders.Domain;

namespace ECommerce.Orders.Tests;

/// <summary>
/// The order lifecycle. These tests are the executable answer to "which events can
/// cancel an order" and "how does an order get from Pending to Confirmed".
/// </summary>
public class OrderStateMachineTests
{
    private static Order NewOrder() =>
        Order.Create(Guid.NewGuid(), "EUR",
            [OrderItem.Create(Guid.NewGuid(), "Widget", 2, 25.00m)]);

    [Fact]
    public void New_order_starts_pending()
    {
        Assert.Equal(OrderStatus.Pending, NewOrder().Status);
    }

    /// <summary>The complete happy path, in the order the saga executes it.</summary>
    [Fact]
    public void Happy_path_runs_pending_to_confirmed()
    {
        var order = NewOrder();
        var paymentId = Guid.NewGuid();

        order.MarkStockReserved();
        Assert.Equal(OrderStatus.StockReserved, order.Status);

        order.MarkPaymentPending(paymentId);
        Assert.Equal(OrderStatus.PaymentPending, order.Status);

        order.MarkPaid(paymentId);
        Assert.Equal(OrderStatus.Paid, order.Status);

        order.Confirm();
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(paymentId, order.PaymentId);
        Assert.True(order.IsTerminal);
    }

    [Fact]
    public void Stock_reservation_failure_cancels_a_pending_order()
    {
        var order = NewOrder();

        order.Cancel("stock_unavailable:insufficient_stock");

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Contains("stock_unavailable", order.CancellationReason);
    }

    [Fact]
    public void Payment_failure_moves_a_pending_payment_to_payment_failed()
    {
        var order = NewOrder();
        order.MarkStockReserved();
        order.MarkPaymentPending(Guid.NewGuid());

        order.MarkPaymentFailed("card_expired");

        Assert.Equal(OrderStatus.PaymentFailed, order.Status);
        Assert.Equal("card_expired", order.CancellationReason);
        Assert.True(order.IsTerminal);
    }

    /// <summary>
    /// Skipping a step must not be possible. Without this guard a bug in the saga could
    /// confirm an order whose stock was never reserved.
    /// </summary>
    [Fact]
    public void Cannot_confirm_without_paying()
    {
        var order = NewOrder();
        order.MarkStockReserved();

        var ex = Assert.Throws<BusinessRuleException>(() => order.Confirm());
        Assert.Equal("invalid_status_transition", ex.Rule);
    }

    [Fact]
    public void Cannot_pay_before_reserving_stock()
    {
        var order = NewOrder();

        Assert.Throws<BusinessRuleException>(() => order.MarkPaymentPending(Guid.NewGuid()));
    }

    /// <summary>
    /// A confirmed order is refunded, not cancelled. Allowing cancellation here would
    /// let stock be released for goods that have already been paid for and shipped.
    /// </summary>
    [Fact]
    public void Confirmed_order_cannot_be_cancelled()
    {
        var order = NewOrder();
        order.MarkStockReserved();
        order.MarkPaymentPending(Guid.NewGuid());
        order.MarkPaid(Guid.NewGuid());
        order.Confirm();

        var ex = Assert.Throws<BusinessRuleException>(() => order.Cancel("changed_mind"));
        Assert.Equal("invalid_status_transition", ex.Rule);
    }

    [Fact]
    public void Cancelled_order_is_terminal()
    {
        var order = NewOrder();
        order.Cancel("customer_request");

        Assert.Throws<BusinessRuleException>(() => order.MarkStockReserved());
        Assert.Throws<BusinessRuleException>(() => order.Cancel("again"));
    }

    /// <summary>
    /// HoldsInventory drives whether cancellation needs to compensate. Getting this
    /// wrong either leaks stock forever or double-releases it.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Pending, false)]
    [InlineData(OrderStatus.StockReserved, true)]
    [InlineData(OrderStatus.PaymentPending, true)]
    [InlineData(OrderStatus.Paid, true)]
    [InlineData(OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.PaymentFailed, false)]
    public void HoldsInventory_reflects_whether_stock_is_still_reserved(
        OrderStatus status, bool expected)
    {
        var order = NewOrder();

        switch (status)
        {
            case OrderStatus.StockReserved:
                order.MarkStockReserved();
                break;
            case OrderStatus.PaymentPending:
                order.MarkStockReserved();
                order.MarkPaymentPending(Guid.NewGuid());
                break;
            case OrderStatus.Paid:
                order.MarkStockReserved();
                order.MarkPaymentPending(Guid.NewGuid());
                order.MarkPaid(Guid.NewGuid());
                break;
            case OrderStatus.Confirmed:
                order.MarkStockReserved();
                order.MarkPaymentPending(Guid.NewGuid());
                order.MarkPaid(Guid.NewGuid());
                order.Confirm();
                break;
            case OrderStatus.Cancelled:
                order.Cancel("test");
                break;
            case OrderStatus.PaymentFailed:
                order.MarkStockReserved();
                order.MarkPaymentPending(Guid.NewGuid());
                order.MarkPaymentFailed("test");
                break;
        }

        Assert.Equal(expected, order.HoldsInventory);
    }
}
