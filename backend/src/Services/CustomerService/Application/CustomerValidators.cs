using FluentValidation;

namespace ECommerce.Customers.Application;

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100);

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Email is not a valid address.")
            .MaximumLength(320);

        // Loose on purpose: this system serves several countries and a strict national
        // format would reject legitimate numbers. Real verification is an SMS round
        // trip, not a regex.
        RuleFor(x => x.Phone)
            .Matches(@"^\+?[0-9\s\-()]{7,30}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Phone must be 7-30 characters of digits, spaces, dashes or parentheses.");
    }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);

        RuleFor(x => x.Phone)
            .Matches(@"^\+?[0-9\s\-()]{7,30}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}
