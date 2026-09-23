using FluentValidation;

namespace ECommerce.Inventory.Application;

public sealed class ReserveStockRequestValidator : AbstractValidator<ReserveStockRequest>
{
    public ReserveStockRequestValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("At least one line is required.")
            .Must(l => l.Count <= 100).WithMessage("An order may not exceed 100 distinct lines.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be positive.")
                .LessThanOrEqualTo(1000).WithMessage("Quantity per line is capped at 1000.");
        });

        // Two lines for the same product would take the same row lock twice in one
        // transaction and double-count the reservation. Reject rather than merge:
        // silently combining them hides a bug in the caller.
        RuleFor(x => x.Lines)
            .Must(l => l.Select(x => x.ProductId).Distinct().Count() == l.Count)
            .WithMessage("Each product may appear only once; combine duplicate lines.");
    }
}

public sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
