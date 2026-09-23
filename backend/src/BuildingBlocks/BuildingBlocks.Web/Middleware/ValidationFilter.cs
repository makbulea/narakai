using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BuildingBlocks.Web.Middleware;

/// <summary>
/// Runs FluentValidation over action arguments before the action executes.
///
/// Throwing ValidationException rather than short-circuiting with a BadRequest keeps
/// one error-shaping path: GlobalExceptionHandler turns it into the same
/// ValidationProblemDetails that every other failure mode produces.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(
                new ValidationContext<object>(argument), context.HttpContext.RequestAborted);

            if (!result.IsValid)
                throw new ValidationException(result.Errors);
        }

        await next();
    }
}
