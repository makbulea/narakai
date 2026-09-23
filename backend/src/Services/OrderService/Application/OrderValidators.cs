using FluentValidation;

namespace ECommerce.Orders.Application;

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    private static readonly string[] SupportedCurrencies = ["EUR", "USD", "GBP", "TRY"];

    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(c => SupportedCurrencies.Contains(c.Trim().ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {string.Join(", ", SupportedCurrencies)}.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("An order must contain at least one line.")
            .Must(l => l.Count <= 100).WithMessage("An order may not exceed 100 distinct lines.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 1000);
        });

        // Duplicate product ids would take the same inventory row lock twice inside one
        // reservation transaction. Reject here rather than merging silently.
        RuleFor(x => x.Lines)
            .Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count)
            .WithMessage("Each product may appear only once; combine duplicate lines.");
    }
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
