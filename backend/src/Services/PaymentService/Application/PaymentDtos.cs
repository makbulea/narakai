using ECommerce.Payments.Domain;
using FluentValidation;

namespace ECommerce.Payments.Application;

public sealed record ProcessPaymentRequest(
    Guid OrderId, decimal Amount, string Currency, string IdempotencyKey);

public sealed record PaymentResponse(
    Guid Id, Guid OrderId, decimal Amount, string Currency, string Status,
    string? TransactionId, string? FailureReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static PaymentResponse From(Payment p) => new(
        p.Id, p.OrderId, p.Amount, p.Currency, p.Status.ToString(),
        p.TransactionId, p.FailureReason, p.CreatedAt, p.UpdatedAt);
}

public sealed class ProcessPaymentRequestValidator : AbstractValidator<ProcessPaymentRequest>
{
    private static readonly string[] SupportedCurrencies = ["EUR", "USD", "GBP", "TRY"];

    public ProcessPaymentRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Amount supports at most two decimal places.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(c => SupportedCurrencies.Contains(c.Trim().ToUpperInvariant()));

        // Required, not optional. A payment API that accepts requests without an
        // idempotency key will eventually charge someone twice.
        RuleFor(x => x.IdempotencyKey)
            .NotEmpty().WithMessage("An idempotency key is required for payment processing.")
            .MaximumLength(200);
    }
}
