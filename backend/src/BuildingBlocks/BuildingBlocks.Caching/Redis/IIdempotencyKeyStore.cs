using StackExchange.Redis;
namespace BuildingBlocks.Caching.Redis;

/// <summary>
/// Short-lived store for HTTP idempotency keys.
///
/// Different from <c>IProcessedMessageStore</c>, and the difference matters:
///
///   - Kafka dedup must be transactional with the side effect, so it lives in Postgres.
///   - HTTP idempotency guards against a client retrying a POST after a timeout. The
///     window is minutes, the volume is high, and losing an entry costs one duplicate
///     rather than a corrupted ledger. Redis with a TTL is the right shape.
///
/// PaymentService additionally persists its idempotency key on the payment row, so
/// correctness never depends on Redis surviving.
/// </summary>
public interface IIdempotencyKeyStore
{
    /// <summary>Reserves the key. False means it was already taken — a duplicate request.</summary>
    Task<bool> TryReserveAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    Task<string?> GetStoredResponseAsync(string key, CancellationToken ct = default);

    Task StoreResponseAsync(string key, string response, TimeSpan ttl, CancellationToken ct = default);
}

public sealed class RedisIdempotencyKeyStore(IConnectionMultiplexer redis) : IIdempotencyKeyStore
{
    private static string Reservation(string key) => $"idem:reserved:{key}";
    private static string Response(string key) => $"idem:response:{key}";

    public async Task<bool> TryReserveAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        await redis.GetDatabase().StringSetAsync(Reservation(key), "1", ttl, When.NotExists);

    public async Task<string?> GetStoredResponseAsync(string key, CancellationToken ct = default)
    {
        var value = await redis.GetDatabase().StringGetAsync(Response(key));
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public Task StoreResponseAsync(string key, string response, TimeSpan ttl, CancellationToken ct = default) =>
        redis.GetDatabase().StringSetAsync(Response(key), response, ttl);
}
