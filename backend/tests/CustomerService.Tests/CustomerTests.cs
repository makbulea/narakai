using ECommerce.Customers.Application;
using ECommerce.Customers.Domain;

namespace ECommerce.Customers.Tests;

public class CustomerTests
{
    [Fact]
    public void New_customer_is_active_and_can_order()
    {
        var customer = Customer.Create("Ada", "Lovelace", "ada@example.com", "+441234567890");

        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.True(customer.CanPlaceOrders);
        Assert.Equal("Ada Lovelace", customer.FullName);
    }

    /// <summary>
    /// Email is stored lower-cased so the unique index is genuinely case-insensitive.
    /// "A@b.com" and "a@b.com" are the same mailbox and must collide.
    /// </summary>
    [Fact]
    public void Email_is_normalised_to_lower_case()
    {
        var customer = Customer.Create("Ada", "Lovelace", "  Ada@Example.COM  ", null);

        Assert.Equal("ada@example.com", customer.Email);
    }

    [Fact]
    public void Whitespace_is_trimmed_from_names()
    {
        var customer = Customer.Create("  Ada  ", "  Lovelace  ", "ada@example.com", "  +44123  ");

        Assert.Equal("Ada", customer.FirstName);
        Assert.Equal("Lovelace", customer.LastName);
        Assert.Equal("+44123", customer.Phone);
    }

    [Fact]
    public void Blank_phone_becomes_null_rather_than_empty_string()
    {
        var customer = Customer.Create("Ada", "Lovelace", "ada@example.com", "   ");

        Assert.Null(customer.Phone);
    }

    /// <summary>Suspended and deleted customers cannot place orders. OrderService checks this.</summary>
    [Fact]
    public void Suspended_customer_cannot_place_orders()
    {
        var customer = Customer.Create("Ada", "Lovelace", "ada@example.com", null);

        customer.Suspend();

        Assert.Equal(CustomerStatus.Suspended, customer.Status);
        Assert.False(customer.CanPlaceOrders);
    }

    [Fact]
    public void Reactivating_restores_ordering()
    {
        var customer = Customer.Create("Ada", "Lovelace", "ada@example.com", null);
        customer.Suspend();

        customer.Reactivate();

        Assert.True(customer.CanPlaceOrders);
    }

    /// <summary>
    /// Soft delete. The row survives so historical orders can still resolve who placed
    /// them; only the status changes.
    /// </summary>
    [Fact]
    public void Deleting_marks_status_without_clearing_data()
    {
        var customer = Customer.Create("Ada", "Lovelace", "ada@example.com", null);

        customer.MarkDeleted();

        Assert.Equal(CustomerStatus.Deleted, customer.Status);
        Assert.False(customer.CanPlaceOrders);
        Assert.Equal("ada@example.com", customer.Email);   // still resolvable
    }

    [Fact]
    public void Updating_details_bumps_the_timestamp()
    {
        var customer = Customer.Create("Ada", "Lovelace", "ada@example.com", null);
        var before = customer.UpdatedAt;

        Thread.Sleep(2);
        customer.UpdateDetails("Augusta", "King", "+44999");

        Assert.Equal("Augusta", customer.FirstName);
        Assert.True(customer.UpdatedAt > before);
    }
}

public class CreateCustomerValidatorTests
{
    private readonly CreateCustomerRequestValidator _validator = new();

    private static CreateCustomerRequest Request(
        string first = "Ada", string last = "Lovelace",
        string email = "ada@example.com", string? phone = "+441234567") =>
        new(first, last, email, phone);

    [Fact]
    public void Valid_request_passes()
    {
        Assert.True(_validator.Validate(Request()).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("two@@example.com")]
    public void Invalid_email_is_rejected(string email)
    {
        Assert.False(_validator.Validate(Request(email: email)).IsValid);
    }

    /// <summary>
    /// A host without a dot is accepted on purpose. "user@localhost" and
    /// "user@intranet" are valid addresses, and rejecting them would break internal
    /// accounts. Real verification is a confirmation mail, not a stricter regex.
    /// </summary>
    [Fact]
    public void Host_without_a_dot_is_accepted()
    {
        Assert.True(_validator.Validate(Request(email: "admin@localhost")).IsValid);
    }

    [Fact]
    public void Missing_names_are_rejected()
    {
        Assert.False(_validator.Validate(Request(first: "")).IsValid);
        Assert.False(_validator.Validate(Request(last: "")).IsValid);
    }

    /// <summary>Phone is optional; absent is fine, malformed is not.</summary>
    [Fact]
    public void Phone_is_optional()
    {
        Assert.True(_validator.Validate(Request(phone: null)).IsValid);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12")]
    public void Malformed_phone_is_rejected(string phone)
    {
        Assert.False(_validator.Validate(Request(phone: phone)).IsValid);
    }

    /// <summary>
    /// Deliberately permissive across countries — a strict national format would reject
    /// legitimate numbers, and real verification is an SMS round trip anyway.
    /// </summary>
    [Theory]
    [InlineData("+44 20 7123 4567")]
    [InlineData("(555) 123-4567")]
    [InlineData("05321234567")]
    public void International_formats_are_accepted(string phone)
    {
        Assert.True(_validator.Validate(Request(phone: phone)).IsValid);
    }
}
