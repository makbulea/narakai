using BuildingBlocks.Core.Correlation;
using BuildingBlocks.Web.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace BuildingBlocks.Web.Resilience;

public static class ResilienceExtensions
{
    /// <summary>
    /// The standard outbound HTTP policy for service-to-service calls.
    ///
    /// Four layers, in the order the request passes through them:
    ///
    ///  1. Total timeout (10s) — a hard ceiling on the whole operation including retries,
    ///     so a caller never waits indefinitely because each individual attempt was
    ///     "only" three seconds.
    ///  2. Retry (3 attempts, exponential + jitter) — only for transient failures.
    ///     Jitter matters: without it every instance that failed on the same blip
    ///     retries on the same tick and re-flattens the recovering service.
    ///  3. Circuit breaker — after sustained failure, fail fast instead of queueing
    ///     doomed requests. Protects both us (threads) and them (recovery time).
    ///  4. Per-attempt timeout (3s) — bounds a single try so a hung socket does not
    ///     consume the entire total budget.
    ///
    /// What is deliberately NOT retried: 4xx. A 400 or a 404 will be a 400 or a 404
    /// however many times you ask. Retrying them wastes budget and can duplicate work.
    /// Microsoft.Extensions.Http.Resilience's default handler already classifies only
    /// 5xx, 408 and network faults as transient, which is exactly the line we want.
    /// </summary>
    public static IHttpClientBuilder AddStandardResilience(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);

            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromMilliseconds(300);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;

            // Trip once half of a meaningful sample fails. A lower ratio trips on
            // noise; a higher one keeps hammering a service that is clearly down.
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.MinimumThroughput = 8;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        });

        // Propagate correlation on every outgoing call so the downstream service logs
        // under the same id instead of inventing its own.
        builder.AddHttpMessageHandler(() => new CorrelationForwardingHandler());

        // Attach credentials: the caller's token when a user request is in flight,
        // otherwise a token identifying this service. Without it every internal call
        // is anonymous and comes back 401.
        builder.AddHttpMessageHandler<ServiceAuthenticationHandler>();

        return builder;
    }
}

public sealed class CorrelationForwardingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(CorrelationContext.HeaderName))
            request.Headers.Add(CorrelationContext.HeaderName, CorrelationContext.CorrelationId);

        return base.SendAsync(request, cancellationToken);
    }
}
