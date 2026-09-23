using BuildingBlocks.Core.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Web.Middleware;

/// <summary>
/// Establishes the correlation id for the request and makes sure it leaves again.
///
/// Accepting a client-supplied id is what makes cross-service tracing work: OrderService
/// forwards its id on the HTTP call to InventoryService, which adopts it rather than
/// minting a new one, so both services' logs carry the same value.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[CorrelationContext.HeaderName].FirstOrDefault();
        CorrelationContext.Set(incoming);

        var correlationId = CorrelationContext.CorrelationId;

        // Echo it back before the body is written; headers are locked once it starts.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Push into the log scope so every log line in this request carries it without
        // each call site remembering to pass it.
        using (logger.BeginScope(new Dictionary<string, object>
               {
                   ["CorrelationId"] = correlationId,
                   ["RequestPath"] = context.Request.Path.Value ?? string.Empty
               }))
        {
            await next(context);
        }
    }
}
