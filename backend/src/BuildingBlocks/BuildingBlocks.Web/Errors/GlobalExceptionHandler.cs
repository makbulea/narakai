using BuildingBlocks.Core.Correlation;
using BuildingBlocks.Core.Errors;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.Errors;

/// <summary>
/// Turns exceptions into RFC 7807 ProblemDetails so every service fails the same shape.
///
/// The mapping encodes an important distinction: exceptions that are part of the
/// domain's vocabulary (not found, conflict, rule violated) are *expected* and become
/// 4xx with a useful message. Everything else is a bug, becomes a 500, and the message
/// is deliberately generic — internal exception text leaks schema and file paths to
/// whoever is probing the API.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = Map(exception, context);

        // Always give the caller the correlation id. It is the single thing that turns
        // "it failed" into a support conversation that can actually be investigated.
        problem.Extensions["correlationId"] = CorrelationContext.CorrelationId;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (problem.Status >= 500)
            logger.LogError(exception,
                "Unhandled exception on {Method} {Path} (correlation {CorrelationId})",
                context.Request.Method, context.Request.Path, CorrelationContext.CorrelationId);
        else
            logger.LogWarning(
                "{ExceptionType} on {Method} {Path}: {Message} (correlation {CorrelationId})",
                exception.GetType().Name, context.Request.Method, context.Request.Path,
                exception.Message, CorrelationContext.CorrelationId);

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(problem, ct);

        return true;
    }

    private static ProblemDetails Map(Exception exception, HttpContext context)
    {
        var instance = context.Request.Path.Value;

        return exception switch
        {
            NotFoundException nf => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Resource not found",
                Detail = nf.Message,
                Type = "https://httpstatuses.io/404",
                Instance = instance,
                Extensions = { ["errorCode"] = nf.ErrorCode }
            },

            ConflictException c => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = c.Message,
                Type = "https://httpstatuses.io/409",
                Instance = instance,
                Extensions = { ["errorCode"] = c.ErrorCode }
            },

            // 422 rather than 400: the request parsed and the types were right, a
            // business rule said no. A client cannot fix this by reformatting.
            BusinessRuleException br => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Business rule violated",
                Detail = br.Message,
                Type = "https://httpstatuses.io/422",
                Instance = instance,
                Extensions = { ["errorCode"] = br.ErrorCode, ["rule"] = br.Rule }
            },

            ValidationException v => BuildValidationProblem(v, instance),

            // 502: we are the gateway and something behind us failed. Distinct from 500
            // because it tells the caller a retry might work.
            DownstreamServiceException d => new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Downstream service unavailable",
                Detail = $"'{d.Service}' could not be reached.",
                Type = "https://httpstatuses.io/502",
                Instance = instance,
                Extensions = { ["errorCode"] = "downstream_unavailable", ["service"] = d.Service }
            },

            // The circuit breaker is refusing calls because a dependency is failing.
            // 503 with Retry-After, not 500: this is a known, temporary condition and
            // the caller should be told it is worth trying again shortly.
            Polly.CircuitBreaker.BrokenCircuitException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service temporarily unavailable",
                Detail = "A downstream dependency is failing and calls are being short-circuited. Retry shortly.",
                Type = "https://httpstatuses.io/503",
                Instance = instance,
                Extensions = { ["errorCode"] = "circuit_open" }
            },

            TimeoutException or TaskCanceledException => new ProblemDetails
            {
                Status = StatusCodes.Status504GatewayTimeout,
                Title = "Request timed out",
                Detail = "The operation did not complete within the allowed time.",
                Type = "https://httpstatuses.io/504",
                Instance = instance,
                Extensions = { ["errorCode"] = "timeout" }
            },

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal server error",
                // Intentionally not exception.Message — see class remarks.
                Detail = "An unexpected error occurred. Quote the correlation id when reporting this.",
                Type = "https://httpstatuses.io/500",
                Instance = instance,
                Extensions = { ["errorCode"] = "internal_error" }
            }
        };
    }

    private static ProblemDetails BuildValidationProblem(ValidationException exception, string? instance)
    {
        var errors = exception.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Type = "https://httpstatuses.io/400",
            Instance = instance,
            Extensions = { ["errorCode"] = "validation_failed" }
        };
    }
}
