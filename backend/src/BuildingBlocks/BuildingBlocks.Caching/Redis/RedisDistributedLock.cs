using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BuildingBlocks.Caching.Redis;

public sealed class RedisDistributedLock(
    IConnectionMultiplexer redis,
    ILogger<RedisDistributedLock> logger) : IDistributedLock
{
    public async Task<IAsyncDisposable?> AcquireAsync(
        string resource, TimeSpan expiry, TimeSpan waitTime, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var key = $"lock:{resource}";

        // A unique token per holder. Release compares it before deleting so a holder
        // whose lock already expired cannot delete the *next* holder's lock.
        var token = Guid.NewGuid().ToString();

        var deadline = DateTimeOffset.UtcNow + waitTime;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            // SET NX EX — atomic acquire with an expiry, so a crashed holder's lock
            // eventually frees itself instead of wedging the resource forever.
            if (await db.StringSetAsync(key, token, expiry, When.NotExists))
                return new Handle(db, key, token, logger);

            await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(20, 80)), ct);
        }

        logger.LogWarning("Could not acquire lock {Resource} within {WaitTime}", resource, waitTime);
        return null;
    }

    private sealed class Handle(IDatabase db, string key, string token, ILogger logger) : IAsyncDisposable
    {
        // Compare-and-delete in one round trip. Doing it as GET then DEL would leave a
        // window where the lock expires between the two calls and we delete someone
        // else's lock.
        private const string ReleaseScript = """
            if redis.call('GET', KEYS[1]) == ARGV[1] then
                return redis.call('DEL', KEYS[1])
            else
                return 0
            end
            """;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
            }
            catch (Exception ex)
            {
                // The expiry will clean it up. Log rather than throw from Dispose.
                logger.LogWarning(ex, "Failed to release lock {Key}; relying on expiry", key);
            }
        }
    }
}
