using Microsoft.Extensions.Options;

namespace ECommerce.Payments.Infrastructure;

public sealed class PaymentProviderOptions
{
    public const string SectionName = "PaymentProvider";

    /// <summary>
    /// Fraction of charges that are declined, 0.0–1.0.
    ///
    /// Non-zero in development on purpose: the compensation path (release stock, cancel
    /// order, notify customer) is the half of this system most likely to be broken, and
    /// it only runs when a payment fails. A 0% failure rate means it is never exercised.
    /// </summary>
    public double FailureRate { get; set; } = 0.15;

    /// <summary>Simulated provider latency, so timeouts and circuit breakers see realistic timing.</summary>
    public int MinLatencyMs { get; set; } = 50;
    public int MaxLatencyMs { get; set; } = 400;

    /// <summary>Fraction of calls that hang past the caller's timeout.</summary>
    public double TimeoutRate { get; set; } = 0.02;
}

public sealed record ProviderResult(bool Approved, string? TransactionId, string? DeclineReason);

/// <summary>
/// Stand-in for a real card processor. No network, no credentials, no PCI surface.
///
/// The decline reasons are drawn from the categories a real gateway returns, because
/// downstream code cares about the distinction: "insufficient funds" is worth retrying
/// later with the customer's consent, "stolen card" is not.
/// </summary>
public sealed class FakePaymentProvider(
    IOptions<PaymentProviderOptions> options,
    ILogger<FakePaymentProvider> logger)
{
    private static readonly string[] DeclineReasons =
    [
        "insufficient_funds",
        "card_expired",
        "do_not_honour",
        "suspected_fraud"
    ];

    private readonly PaymentProviderOptions _options = options.Value;

    public async Task<ProviderResult> ChargeAsync(
        Guid orderId, decimal amount, string currency, CancellationToken ct)
    {
        var latency = Random.Shared.Next(_options.MinLatencyMs, _options.MaxLatencyMs);

        // Occasionally hang, so the caller's timeout and circuit breaker are real code
        // paths rather than configuration nobody has ever seen fire.
        if (Random.Shared.NextDouble() < _options.TimeoutRate)
        {
            logger.LogWarning("Simulating provider timeout for order {OrderId}", orderId);
            latency = 30_000;
        }

        await Task.Delay(latency, ct);

        if (Random.Shared.NextDouble() < _options.FailureRate)
        {
            var reason = DeclineReasons[Random.Shared.Next(DeclineReasons.Length)];

            logger.LogInformation(
                "Provider declined {Amount} {Currency} for order {OrderId}: {Reason}",
                amount, currency, orderId, reason);

            return new ProviderResult(false, null, reason);
        }

        // Never log card data — there is none here, and there must be none here.
        var transactionId = $"txn_{Guid.NewGuid():N}";

        logger.LogInformation(
            "Provider approved {Amount} {Currency} for order {OrderId} as {TransactionId}",
            amount, currency, orderId, transactionId);

        return new ProviderResult(true, transactionId, null);
    }

    public async Task<ProviderResult> RefundAsync(string transactionId, decimal amount, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(_options.MinLatencyMs, _options.MaxLatencyMs), ct);

        // Refunds against an already-captured transaction effectively always succeed.
        return new ProviderResult(true, $"rfnd_{Guid.NewGuid():N}", null);
    }
}
