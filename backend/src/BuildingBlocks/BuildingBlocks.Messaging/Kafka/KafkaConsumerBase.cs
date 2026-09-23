using System.Text.Json;
using BuildingBlocks.Core.Correlation;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Idempotency;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Base class for every Kafka consumer in the system. Handles the parts that are the
/// same everywhere so subclasses only write business logic:
///
///   - manual offset commit (commit *after* handling, never before)
///   - correlation id restored from the envelope so logs stay joined up
///   - deduplication via <see cref="IProcessedMessageStore"/>
///   - bounded retry with exponential backoff and jitter
///   - dead-letter after <see cref="KafkaOptions.MaxDeliveryAttempts"/>
///
/// Offsets are committed manually because auto-commit acknowledges on a timer, not on
/// success: a crash between the timer tick and the handler completing loses the
/// message outright. Committing after the handler returns turns loss into duplication,
/// and duplication is what the dedup store is for.
/// </summary>
public abstract class KafkaConsumerBase(
    string topic,
    string consumerName,
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger logger) : BackgroundService
{
    private readonly KafkaOptions _options = options.Value;

    protected string ConsumerName => consumerName;

    /// <summary>Event types this consumer cares about. Everything else is skipped cheaply.</summary>
    protected abstract IReadOnlySet<string> HandledEventTypes { get; }

    /// <summary>
    /// Handle one event. Throwing signals failure and triggers retry; returning
    /// normally means the message is done and the offset may advance.
    /// </summary>
    protected abstract Task HandleAsync(
        IntegrationEvent envelope,
        IServiceProvider scopedServices,
        CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Yield so host startup is not blocked by the consume loop.
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,

            // See class remarks: commit only after the handler succeeds.
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => logger.LogError(
                "Kafka consumer error on {Topic}: {Reason} (fatal: {IsFatal})",
                topic, e.Reason, e.IsFatal))
            .Build();

        consumer.Subscribe(topic);
        logger.LogInformation(
            "{Consumer} subscribed to {Topic} as group {Group}",
            consumerName, topic, _options.ConsumerGroupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result?.Message is null) continue;

                await ProcessWithRetryAsync(result, consumer, stoppingToken);

                consumer.StoreOffset(result);
                consumer.Commit(result);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                logger.LogError(ex, "{Consumer} consume error on {Topic}", consumerName, topic);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (Exception ex)
            {
                // Already dead-lettered inside ProcessWithRetryAsync; commit so the
                // partition is not blocked by a message we have given up on.
                logger.LogError(ex,
                    "{Consumer} gave up on offset {Offset}, advancing past it",
                    consumerName, result?.Offset.Value);

                if (result is not null)
                {
                    consumer.StoreOffset(result);
                    consumer.Commit(result);
                }
            }
        }

        consumer.Close();
    }

    private async Task ProcessWithRetryAsync(
        ConsumeResult<string, string> result,
        IConsumer<string, string> consumer,
        CancellationToken ct)
    {
        IntegrationEvent? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IntegrationEvent>(result.Message.Value);
        }
        catch (JsonException ex)
        {
            // Malformed JSON will never parse, no matter how often we retry.
            // Straight to the dead-letter topic.
            logger.LogError(ex, "{Consumer} could not deserialize message, dead-lettering", consumerName);
            await DeadLetterRawAsync(result.Message.Value, "deserialization_failed", ct);
            return;
        }

        if (envelope is null) return;

        if (!HandledEventTypes.Contains(envelope.EventType))
            return;

        CorrelationContext.Set(envelope.CorrelationId);

        for (var attempt = 1; attempt <= _options.MaxDeliveryAttempts; attempt++)
        {
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                var store = services.GetService<IProcessedMessageStore>();

                if (store is not null &&
                    await store.HasProcessedAsync(envelope.EventId, consumerName, ct))
                {
                    logger.LogDebug(
                        "{Consumer} skipping duplicate {EventType} {EventId}",
                        consumerName, envelope.EventType, envelope.EventId);
                    return;
                }

                await HandleAsync(envelope, services, ct);

                if (store is not null)
                    await store.MarkProcessedAsync(envelope.EventId, consumerName, envelope.EventType, ct);

                logger.LogInformation(
                    "{Consumer} handled {EventType} {EventId} (correlation {CorrelationId}, attempt {Attempt})",
                    consumerName, envelope.EventType, envelope.EventId, envelope.CorrelationId, attempt);

                return;
            }
            catch (Exception ex) when (attempt < _options.MaxDeliveryAttempts)
            {
                // Exponential backoff with jitter. Jitter matters here because a broker
                // hiccup fails every partition's consumer at once; without it they all
                // retry on the same tick and hit the recovering dependency together.
                var backoff = _options.RetryBaseDelayMs * Math.Pow(2, attempt - 1);
                var jitter = Random.Shared.Next(0, _options.RetryBaseDelayMs);
                var delay = TimeSpan.FromMilliseconds(backoff + jitter);

                logger.LogWarning(ex,
                    "{Consumer} attempt {Attempt}/{Max} failed for {EventType} {EventId}, retrying in {Delay}ms",
                    consumerName, attempt, _options.MaxDeliveryAttempts,
                    envelope.EventType, envelope.EventId, delay.TotalMilliseconds);

                // Tell Kafka we are still alive while we wait, otherwise a long backoff
                // triggers a rebalance and another consumer picks up the same message.
                consumer.Pause([result.TopicPartition]);
                try { await Task.Delay(delay, ct); }
                finally { consumer.Resume([result.TopicPartition]); }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "{Consumer} exhausted {Max} attempts for {EventType} {EventId}, dead-lettering",
                    consumerName, _options.MaxDeliveryAttempts, envelope.EventType, envelope.EventId);

                await DeadLetterAsync(envelope with { Attempt = attempt }, ex.Message, ct);
                return;
            }
        }
    }

    private async Task DeadLetterAsync(IntegrationEvent envelope, string reason, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var publisher = scope.ServiceProvider.GetService<KafkaEventPublisher>();
        if (publisher is null) return;

        try
        {
            await publisher.PublishEnvelopeAsync(
                Topics.DeadLetterFor(topic), envelope.EventId.ToString(), envelope, ct);
        }
        catch (Exception ex)
        {
            // If even the DLQ write fails there is nowhere left to put it. Log loudly:
            // this is the one place in the pipeline where a message really is lost.
            logger.LogCritical(ex,
                "{Consumer} could not dead-letter {EventId} ({Reason}) — message lost",
                consumerName, envelope.EventId, reason);
        }
    }

    private async Task DeadLetterRawAsync(string raw, string reason, CancellationToken ct)
    {
        var envelope = new IntegrationEvent
        {
            EventId = Guid.NewGuid(),
            EventType = "unparseable",
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = CorrelationContext.CorrelationId,
            Payload = raw
        };

        await DeadLetterAsync(envelope, reason, ct);
    }
}
