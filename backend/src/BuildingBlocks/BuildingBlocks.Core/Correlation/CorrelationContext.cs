namespace BuildingBlocks.Core.Correlation;

/// <summary>
/// Carries the correlation id for the current logical operation.
///
/// A correlation id is created at the edge (HTTP middleware) and then follows the
/// work everywhere it goes: into log scopes, into outgoing HTTP headers, and into
/// Kafka message headers. Without it a failed order is six unrelated log streams;
/// with it the whole flow is one query.
///
/// AsyncLocal rather than a scoped service because Kafka consumers and background
/// workers have no request scope to hang it off.
/// </summary>
public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";

    private static readonly AsyncLocal<string?> Current = new();

    public static string CorrelationId => Current.Value ??= Guid.NewGuid().ToString();

    public static void Set(string? correlationId)
    {
        Current.Value = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString()
            : correlationId;
    }
}
