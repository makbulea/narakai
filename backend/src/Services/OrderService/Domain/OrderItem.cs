using BuildingBlocks.Core.Errors;

namespace ECommerce.Orders.Domain;

/// <summary>
/// One line of an order.
///
/// ProductName and UnitPrice are copied in, not looked up on read. An order is a record
/// of what was agreed at the time: if the catalogue price changes tomorrow, last week's
/// invoice must not change with it.
/// </summary>
public class OrderItem
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal TotalPrice { get; private set; }

    private OrderItem() { }

    public static OrderItem Create(Guid productId, string productName, int quantity, decimal unitPrice)
    {
        if (quantity <= 0)
            throw new BusinessRuleException("invalid_quantity", "Line quantity must be positive.");

        if (unitPrice < 0)
            throw new BusinessRuleException("invalid_price", "Unit price cannot be negative.");

        return new OrderItem
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            ProductName = productName,
            Quantity = quantity,
            UnitPrice = unitPrice,
            // Rounded per line — see Order.RecalculateTotal for why that matters.
            TotalPrice = decimal.Round(unitPrice * quantity, 2, MidpointRounding.ToEven)
        };
    }
}
