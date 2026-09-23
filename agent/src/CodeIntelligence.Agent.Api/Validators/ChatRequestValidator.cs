using CodeIntelligence.Agent.Api.Contracts;
using FluentValidation;

namespace CodeIntelligence.Agent.Api.Validators;

public sealed class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    public ChatRequestValidator()
    {
        RuleFor(r => r.RepositoryId).NotEmpty();
        RuleFor(r => r.Message).NotEmpty().MaximumLength(4000);
    }
}
