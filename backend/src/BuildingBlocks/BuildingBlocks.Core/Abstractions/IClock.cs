namespace BuildingBlocks.Core.Abstractions;

/// <summary>
/// Injectable clock. Exists so that expiry, retry-backoff and "created at" logic
/// can be tested without sleeping. Production uses <see cref="SystemClock"/>.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
