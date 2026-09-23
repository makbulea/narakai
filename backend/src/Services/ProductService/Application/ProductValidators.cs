using FluentValidation;

namespace ECommerce.Products.Application;

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    // ISO 4217 subset this shop actually trades in. Validating against a closed list
    // stops "EURO" or "eur " reaching the database and breaking totals downstream.
    private static readonly string[] SupportedCurrencies = ["EUR", "USD", "GBP", "TRY"];

    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);

        RuleFor(x => x.Sku)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[A-Za-z0-9-]+$")
            .WithMessage("SKU may contain only letters, digits and dashes.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be greater than zero.")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("Price supports at most two decimal places.");

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(c => SupportedCurrencies.Contains(c.Trim().ToUpperInvariant()))
            .WithMessage($"Currency must be one of: {string.Join(", ", SupportedCurrencies)}.");

        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Price).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
    }
}
