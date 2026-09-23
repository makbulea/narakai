using ECommerce.Orders.Application;

namespace ECommerce.Orders.Tests;

public class CreateOrderValidatorTests
{
    private readonly CreateOrderRequestValidator _validator = new();

    private static CreateOrderRequest Request(params CreateOrderLine[] lines) =>
        new(Guid.NewGuid(), "EUR", lines);

    [Fact]
    public void Valid_request_passes()
    {
        var result = _validator.Validate(Request(new CreateOrderLine(Guid.NewGuid(), 2)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Order_must_have_lines()
    {
        var result = _validator.Validate(Request());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOrderRequest.Lines));
    }

    /// <summary>
    /// Two lines for the same product would take the same inventory row lock twice
    /// inside one reservation transaction.
    /// </summary>
    [Fact]
    public void Duplicate_product_lines_are_rejected()
    {
        var productId = Guid.NewGuid();

        var result = _validator.Validate(
            Request(new CreateOrderLine(productId, 1), new CreateOrderLine(productId, 2)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("only once"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1001)]
    public void Quantity_outside_one_to_one_thousand_is_rejected(int quantity)
    {
        var result = _validator.Validate(Request(new CreateOrderLine(Guid.NewGuid(), quantity)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Unsupported_currency_is_rejected()
    {
        var request = new CreateOrderRequest(
            Guid.NewGuid(), "XYZ", [new CreateOrderLine(Guid.NewGuid(), 1)]);

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateOrderRequest.Currency));
    }
}
