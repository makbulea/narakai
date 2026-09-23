using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BuildingBlocks.Caching.Redis;

public sealed class RedisCacheService(
    IConnectionMultiplexer redis,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private IDatabase Db => redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var value = await Db.StringGetAsync(key);
            if (value.IsNullOrEmpty) return null;

            // RedisValue converts implicitly to both string and ReadOnlySpan<byte>, so
            // passing it straight to Deserialize is ambiguous. ToString() picks a side.
            return JsonSerializer.Deserialize<T>(value.ToString(), JsonOptions);
        }
        catch (Exception ex)
        {
            // A cache is an optimisation, never a dependency. If Redis is down the
            // caller should fall through to the database, not return an error to a
            // customer. Every method here swallows and logs for that reason.
            logger.LogWarning(ex, "Cache read failed for {Key}; falling through to source", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
        where T : class
    {
        try
        {
            await Db.StringSetAsync(key, JsonSerializer.Serialize(value, JsonOptions), ttl ?? DefaultTtl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache write failed for {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await Db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache eviction failed for {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            // SCAN rather than KEYS: KEYS blocks the single-threaded server for the
            // whole sweep, which on a warm cache is a visible latency spike for every
            // other client. SCAN is incremental.
            foreach (var endpoint in redis.GetEndPoints())
            {
                var server = redis.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica) continue;

                await foreach (var key in server.KeysAsync(pattern: $"{prefix}*", pageSize: 250)
                                   .WithCancellation(ct))
                {
                    await Db.KeyDeleteAsync(key);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache prefix eviction failed for {Prefix}", prefix);
        }
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key, Func<CancellationToken, Task<T?>> factory,
        TimeSpan? ttl = null, CancellationToken ct = default) where T : class
    {
        var cached = await GetAsync<T>(key, ct);
        if (cached is not null) return cached;

        var fresh = await factory(ct);
        if (fresh is not null)
            await SetAsync(key, fresh, ttl, ct);

        return fresh;
    }
}
