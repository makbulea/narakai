using BuildingBlocks.Core.Errors;
using ECommerce.Payments.Domain;

namespace ECommerce.Payments.Tests;

public class PaymentLifecycleTests
{
    private static Payment NewPayment(decimal amount = 99.99m) =>
        Payment.Create(Guid.NewGuid(), amount, "EUR", $"order-{Guid.NewGuid()}");

    [Fact]
    public void New_payment_is_pending_with_no_transaction()
    {
        var payment = NewPayment();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.TransactionId);
        Assert.False(payment.IsSuccessful);
    }

    [Fact]
    public void Authorize_then_capture_is_the_success_path()
    {
        var payment = NewPayment();

        payment.Authorize("txn_abc123");
        Assert.Equal(PaymentStatus.Authorized, payment.Status);
        Assert.Equal("txn_abc123", payment.TransactionId);

        payment.Capture();
        Assert.Equal(PaymentStatus.Captured, payment.Status);
        Assert.True(payment.IsSuccessful);
    }

    /// <summary>
    /// Capturing without authorizing would mean taking money the provider never
    /// reserved. The provider would reject it; we reject it first.
    /// </summary>
    [Fact]
    public void Cannot_capture_without_authorizing()
    {
        var payment = NewPayment();

        var ex = Assert.Throws<BusinessRuleException>(() => payment.Capture());
        Assert.Equal("invalid_status_transition", ex.Rule);
    }

    [Fact]
    public void Failure_records_the_reason()
    {
        var payment = NewPayment();

        payment.Fail("insufficient_funds");

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("insufficient_funds", payment.FailureReason);
        Assert.False(payment.IsSuccessful);
    }

    /// <summary>
    /// A captured payment must never be quietly marked failed — the money has moved,
    /// and the record has to say so.
    /// </summary>
    [Fact]
    public void Captured_payment_cannot_be_marked_failed()
    {
        var payment = NewPayment();
        payment.Authorize("txn_1");
        payment.Capture();

        Assert.Throws<BusinessRuleException>(() => payment.Fail("too late"));
    }

    [Fact]
    public void Only_a_captured_payment_can_be_refunded()
    {
        var payment = NewPayment();
        payment.Authorize("txn_1");

        // Authorized-but-uncaptured is a void at the provider, not a refund.
        Assert.Throws<BusinessRuleException>(() => payment.Refund());

        payment.Capture();
        payment.Refund();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void Refunding_twice_is_rejected_at_the_domain_level()
    {
        var payment = NewPayment();
        payment.Authorize("txn_1");
        payment.Capture();
        payment.Refund();

        Assert.Throws<BusinessRuleException>(() => payment.Refund());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void Amount_must_be_positive(decimal amount)
    {
        var ex = Assert.Throws<BusinessRuleException>(
            () => Payment.Create(Guid.NewGuid(), amount, "EUR", "key"));

        Assert.Equal("invalid_amount", ex.Rule);
    }

    [Fact]
    public void Amount_is_rounded_to_two_places()
    {
        var payment = Payment.Create(Guid.NewGuid(), 10.005m, "EUR", "key");

        // Banker's rounding: 10.005 -> 10.00 (0 is even).
        Assert.Equal(10.00m, payment.Amount);
    }

    [Fact]
    public void Currency_is_normalised_to_upper_case()
    {
        var payment = Payment.Create(Guid.NewGuid(), 10m, "eur", "key");

        Assert.Equal("EUR", payment.Currency);
    }

    /// <summary>
    /// The idempotency key is stored on the row, not only in Redis. This is what makes
    /// the double-charge guarantee survive a cache restart.
    /// </summary>
    [Fact]
    public void Idempotency_key_is_persisted_on_the_payment()
    {
        var orderId = Guid.NewGuid();
        var payment = Payment.Create(orderId, 50m, "EUR", $"order-{orderId}");

        Assert.Equal($"order-{orderId}", payment.IdempotencyKey);
    }
}

public class ProcessPaymentValidatorTests
{
    private readonly ECommerce.Payments.Application.ProcessPaymentRequestValidator _validator = new();

    private static ECommerce.Payments.Application.ProcessPaymentRequest Request(
        string idempotencyKey = "order-1", decimal amount = 10m, string currency = "EUR") =>
        new(Guid.NewGuid(), amount, currency, idempotencyKey);

    [Fact]
    public void Valid_request_passes()
    {
        Assert.True(_validator.Validate(Request()).IsValid);
    }

    /// <summary>
    /// A payment API that accepts requests without an idempotency key will eventually
    /// charge someone twice. This is not an optional field.
    /// </summary>
    [Fact]
    public void Idempotency_key_is_required()
    {
        var result = _validator.Validate(Request(idempotencyKey: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("idempotency key"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Amount_must_be_positive(decimal amount)
    {
        Assert.False(_validator.Validate(Request(amount: amount)).IsValid);
    }

    [Fact]
    public void More_than_two_decimal_places_is_rejected()
    {
        Assert.False(_validator.Validate(Request(amount: 10.123m)).IsValid);
    }

    [Fact]
    public void Unsupported_currency_is_rejected()
    {
        Assert.False(_validator.Validate(Request(currency: "XYZ")).IsValid);
    }
}
