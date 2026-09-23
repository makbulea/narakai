using BuildingBlocks.Caching.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BuildingBlocks.Caching;

public static class CachingExtensions
{
    public static IServiceCollection AddRedisCaching(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis")
                               ?? "localhost:6379";

        // One multiplexer per process. It is thread-safe and multiplexes every command
        // over a small number of sockets; creating one per request exhausts connections
        // under load and is the classic StackExchange.Redis mistake.
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;   // let the app start before Redis is up
            options.ConnectRetry = 3;
            options.ConnectTimeout = 5000;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddSingleton<IIdempotencyKeyStore, RedisIdempotencyKeyStore>();

        return services;
    }
}
