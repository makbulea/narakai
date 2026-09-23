using BuildingBlocks.Core.Correlation;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Persistence.Outbox;

public sealed class OutboxProcessorOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 50;

    /// <summary>Publish attempts before a row is parked as dead-lettered.</summary>
    public int MaxAttempts { get; set; } = 10;
}

/// <summary>
/// Moves unpublished outbox rows to Kafka.
///
/// Generic over the service's DbContext so each service reuses this worker against its
/// own database without a shared context type — that would couple every service's
/// schema together, which is exactly what the microservice split is for.
///
/// Ordering: rows are taken oldest-first and locked with FOR UPDATE SKIP LOCKED, so
/// running two instances of a service does not double-publish. SKIP LOCKED rather than
/// plain FOR UPDATE because a second instance should move on to other rows instead of
/// blocking behind the first.
/// </summary>
public sealed class OutboxProcessor<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxProcessorOptions> options,
    ILogger<OutboxProcessor<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private readonly OutboxProcessorOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        logger.LogInformation(
            "Outbox processor started for {Context}, polling every {Interval}s",
            typeof(TDbContext).Name, _options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await ProcessBatchAsync(stoppingToken);

                // Only sleep when there was nothing to do. A full batch usually means
                // more is waiting, and sleeping would add latency for no reason.
                if (published < _options.BatchSize)
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox processor iteration failed; continuing");
                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<KafkaEventPublisher>();

        // EnableRetryOnFailure installs a retrying execution strategy, and that strategy
        // refuses a user-initiated transaction outright: it cannot replay a transaction
        // it did not open. Running the whole unit through the strategy is the supported
        // way to combine the two. Safe here because everything inside is database work —
        // a retry re-runs a rolled-back transaction, not a side effect that already happened.
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () => await PublishBatchAsync(db, publisher, ct));
    }

    private async Task<int> PublishBatchAsync(TDbContext db, KafkaEventPublisher publisher, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var pending = await db.Set<OutboxMessage>()
            .FromSqlRaw(
                """
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND is_dead_lettered = FALSE
                ORDER BY occurred_at
                LIMIT {0}
                FOR UPDATE SKIP LOCKED
                """.Replace("{0}", _options.BatchSize.ToString()))
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            await tx.CommitAsync(ct);
            return 0;
        }

        var succeeded = 0;

        foreach (var message in pending)
        {
            try
            {
                CorrelationContext.Set(message.CorrelationId);

                var envelope = new IntegrationEvent
                {
                    EventId = message.Id,          // reused so consumers can deduplicate
                    EventType = message.EventType,
                    OccurredAt = message.OccurredAt,
                    CorrelationId = message.CorrelationId,
                    Payload = message.Payload,
                    Attempt = message.Attempts
                };

                await publisher.PublishEnvelopeAsync(message.Topic, message.PartitionKey, envelope, ct);

                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.LastError = null;
                succeeded++;
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

                if (message.Attempts >= _options.MaxAttempts)
                {
                    message.IsDeadLettered = true;
                    logger.LogError(ex,
                        "Outbox message {Id} ({EventType}) dead-lettered after {Attempts} attempts",
                        message.Id, message.EventType, message.Attempts);
                }
                else
                {
                    logger.LogWarning(ex,
                        "Outbox message {Id} ({EventType}) failed, attempt {Attempts}/{Max}",
                        message.Id, message.EventType, message.Attempts, _options.MaxAttempts);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (succeeded > 0)
            logger.LogDebug("Outbox published {Count} message(s)", succeeded);

        return succeeded;
    }
}
