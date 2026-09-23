using ECommerce.Products.Application;
using ECommerce.Products.Domain;

namespace ECommerce.Products.Tests;

public class ProductTests
{
    private static Product NewProduct(decimal price = 19.99m) =>
        Product.Create("Widget", "A widget", "wdg-001", price, "eur", "Tools");

    [Fact]
    public void New_product_starts_as_draft_and_is_not_orderable()
    {
        var product = NewProduct();

        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.False(product.IsOrderable);
    }

    /// <summary>SKUs are matched by warehouse staff and integrations; case must not matter.</summary>
    [Fact]
    public void Sku_is_normalised_to_upper_case()
    {
        Assert.Equal("WDG-001", NewProduct().Sku);
    }

    [Fact]
    public void Currency_is_normalised_to_upper_case()
    {
        Assert.Equal("EUR", NewProduct().Currency);
    }

    [Fact]
    public void Activating_makes_a_product_orderable()
    {
        var product = NewProduct();

        product.Activate();

        Assert.Equal(ProductStatus.Active, product.Status);
        Assert.True(product.IsOrderable);
    }

    /// <summary>
    /// Discontinued products stay visible in existing orders and history but cannot be
    /// added to new ones.
    /// </summary>
    [Fact]
    public void Discontinued_product_is_not_orderable()
    {
        var product = NewProduct();
        product.Activate();

        product.Discontinue();

        Assert.False(product.IsOrderable);
    }

    [Fact]
    public void Deleted_product_cannot_be_reactivated()
    {
        var product = NewProduct();
        product.MarkDeleted();

        Assert.Throws<InvalidOperationException>(() => product.Activate());
    }

    [Fact]
    public void Price_is_rounded_to_two_places()
    {
        // Banker's rounding: 10.005 -> 10.00.
        Assert.Equal(10.00m, NewProduct(10.005m).Price);
    }

    [Fact]
    public void Negative_price_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewProduct(-1m));
    }

    [Fact]
    public void Updating_cannot_set_a_negative_price()
    {
        var product = NewProduct();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => product.Update("Widget", null, -5m, "Tools"));
    }
}

public class CreateProductValidatorTests
{
    private readonly CreateProductRequestValidator _validator = new();

    private static CreateProductRequest Request(
        string sku = "WDG-001", decimal price = 19.99m,
        string currency = "EUR", string name = "Widget") =>
        new(name, "A widget", sku, price, currency, "Tools");

    [Fact]
    public void Valid_request_passes()
    {
        Assert.True(_validator.Validate(Request()).IsValid);
    }

    [Theory]
    [InlineData("WDG 001")]      // space
    [InlineData("WDG_001")]      // underscore
    [InlineData("WDG/001")]      // slash
    public void Sku_rejects_characters_outside_letters_digits_and_dashes(string sku)
    {
        Assert.False(_validator.Validate(Request(sku: sku)).IsValid);
    }

    [Fact]
    public void Zero_price_is_rejected()
    {
        Assert.False(_validator.Validate(Request(price: 0m)).IsValid);
    }

    [Fact]
    public void More_than_two_decimal_places_is_rejected()
    {
        Assert.False(_validator.Validate(Request(price: 19.999m)).IsValid);
    }

    [Theory]
    [InlineData("EUR")]
    [InlineData("usd")]
    [InlineData("TRY")]
    public void Supported_currencies_are_accepted_in_any_case(string currency)
    {
        Assert.True(_validator.Validate(Request(currency: currency)).IsValid);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("EURO")]
    public void Unsupported_currency_is_rejected(string currency)
    {
        Assert.False(_validator.Validate(Request(currency: currency)).IsValid);
    }
}
