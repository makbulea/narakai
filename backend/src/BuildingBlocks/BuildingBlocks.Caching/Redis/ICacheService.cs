namespace BuildingBlocks.Caching.Redis;

/// <summary>
/// Read-through cache over Redis.
///
/// Used in exactly one place in this system: ProductService reading a product by id.
/// That endpoint is called by OrderService for every line item of every order, the
/// data changes rarely, and a stale read for a few seconds is harmless. Those three
/// properties together are what justify a cache; two out of three usually does not.
///
/// Deliberately NOT cached: inventory levels (correctness depends on freshness),
/// order state (changes constantly and is read once), customer records (low traffic).
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) where T : class;

    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Invalidates every key under a prefix. Used when a list result set changes.</summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>
    /// Fetch from cache, falling back to <paramref name="factory"/> on a miss and
    /// caching the result. Returns null without caching if the factory returns null —
    /// caching "not found" would turn a transient gap into a sticky one.
    /// </summary>
    Task<T?> GetOrSetAsync<T>(
        string key, Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null, CancellationToken ct = default) where T : class;
}
