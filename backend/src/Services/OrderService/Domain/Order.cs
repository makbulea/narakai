using BuildingBlocks.Core.Errors;

namespace ECommerce.Orders.Domain;

/// <summary>
/// Order lifecycle. The legal transitions are encoded in
/// <see cref="Order.EnsureCanTransitionTo"/> rather than left to callers.
/// </summary>
public enum OrderStatus
{
    /// <summary>Created, nothing reserved or charged yet.</summary>
    Pending = 0,

    /// <summary>Inventory is held. The customer's items are safe from other buyers.</summary>
    StockReserved = 1,

    /// <summary>Payment has been requested and we are waiting for the outcome.</summary>
    PaymentPending = 2,

    /// <summary>Payment captured. Stock still reserved until fulfilment confirms.</summary>
    Paid = 3,

    /// <summary>Terminal success.</summary>
    Confirmed = 4,

    /// <summary>Terminal failure. Any held stock has been released.</summary>
    Cancelled = 5,

    /// <summary>
    /// Payment was declined. Distinct from Cancelled so support can tell "the customer
    /// changed their mind" from "the card was refused" without reading logs.
    /// </summary>
    PaymentFailed = 6
}

public class Order
{
    private readonly List<OrderItem> _items = [];

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }

    /// <summary>Human-facing reference. Customers quote this, not the GUID.</summary>
    public string OrderNumber { get; private set; } = string.Empty;

    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "EUR";
    public string? CancellationReason { get; private set; }
    public Guid? PaymentId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>
    /// Optimistic concurrency token, mapped to the Postgres xmin system column via
    /// UseXminAsConcurrencyToken. Two people editing the same row no longer silently
    /// overwrite each other; the second save fails and the caller gets a 409.
    /// </summary>
    public uint Version { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    private Order() { }

    public static Order Create(Guid customerId, string currency, IEnumerable<OrderItem> items)
    {
        var lines = items.ToList();

        if (lines.Count == 0)
            throw new BusinessRuleException("empty_order", "An order must contain at least one item.");

        var now = DateTimeOffset.UtcNow;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            OrderNumber = GenerateOrderNumber(now),
            Status = OrderStatus.Pending,
            Currency = currency.ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now
        };

        order._items.AddRange(lines);
        order.RecalculateTotal();

        return order;
    }

    /// <summary>
    /// Sums line totals rather than trusting a client-supplied figure.
    ///
    /// Each line is rounded at the line, then summed. Summing unrounded values and
    /// rounding once at the end produces a different number, and the invoice must match
    /// what the line items say — a one-cent discrepancy is a support ticket.
    /// </summary>
    public void RecalculateTotal()
    {
        TotalAmount = decimal.Round(_items.Sum(i => i.TotalPrice), 2, MidpointRounding.ToEven);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkStockReserved()
    {
        EnsureCanTransitionTo(OrderStatus.StockReserved);
        Status = OrderStatus.StockReserved;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPaymentPending(Guid paymentId)
    {
        EnsureCanTransitionTo(OrderStatus.PaymentPending);
        Status = OrderStatus.PaymentPending;
        PaymentId = paymentId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPaid(Guid paymentId)
    {
        EnsureCanTransitionTo(OrderStatus.Paid);
        Status = OrderStatus.Paid;
        PaymentId = paymentId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Confirm()
    {
        EnsureCanTransitionTo(OrderStatus.Confirmed);
        Status = OrderStatus.Confirmed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkPaymentFailed(string reason)
    {
        EnsureCanTransitionTo(OrderStatus.PaymentFailed);
        Status = OrderStatus.PaymentFailed;
        CancellationReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(string reason)
    {
        EnsureCanTransitionTo(OrderStatus.Cancelled);
        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>True once stock has been taken and not yet given back.</summary>
    public bool HoldsInventory =>
        Status is OrderStatus.StockReserved or OrderStatus.PaymentPending
               or OrderStatus.Paid or OrderStatus.Confirmed;

    public bool IsTerminal =>
        Status is OrderStatus.Confirmed or OrderStatus.Cancelled or OrderStatus.PaymentFailed;

    /// <summary>
    /// The state machine, in one place.
    ///
    /// Written as an explicit table rather than scattered if-statements so that
    /// "which events can cancel an order" is answerable by reading one method. Note
    /// that Confirmed is terminal: a confirmed order is refunded, not cancelled, and
    /// refunds are PaymentService's business.
    /// </summary>
    private void EnsureCanTransitionTo(OrderStatus target)
    {
        var allowed = Status switch
        {
            OrderStatus.Pending =>
                target is OrderStatus.StockReserved or OrderStatus.Cancelled,

            OrderStatus.StockReserved =>
                target is OrderStatus.PaymentPending or OrderStatus.Cancelled,

            OrderStatus.PaymentPending =>
                target is OrderStatus.Paid or OrderStatus.PaymentFailed or OrderStatus.Cancelled,

            OrderStatus.Paid =>
                target is OrderStatus.Confirmed or OrderStatus.Cancelled,

            // Terminal states accept nothing.
            OrderStatus.Confirmed or OrderStatus.Cancelled or OrderStatus.PaymentFailed => false,

            _ => false
        };

        if (!allowed)
            throw new BusinessRuleException(
                "invalid_status_transition",
                $"Order {OrderNumber} cannot move from {Status} to {target}.");
    }

    /// <summary>
    /// Date prefix plus random suffix. Sortable by eye, and not sequential — a
    /// sequential number would let anyone estimate daily order volume.
    /// </summary>
    private static string GenerateOrderNumber(DateTimeOffset now) =>
        $"ORD-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
}
