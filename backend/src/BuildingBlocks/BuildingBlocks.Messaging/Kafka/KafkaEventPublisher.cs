using System.Text;
using System.Text.Json;
using BuildingBlocks.Core.Correlation;
using BuildingBlocks.Messaging.Contracts;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Kafka-backed publisher. One producer instance per process: the client is
/// thread-safe, keeps its own connection pool, and creating one per call would
/// re-handshake the cluster on every publish.
/// </summary>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaEventPublisher> logger)
    {
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,

            // Acks.All means the leader waits for in-sync replicas before acknowledging.
            // Slower than Acks.Leader, but a broker failure right after a write no
            // longer silently loses an OrderConfirmed event.
            Acks = options.Value.RequireAllAcks ? Acks.All : Acks.Leader,

            // Without idempotence a producer-side retry can write the same message
            // twice. Consumers are idempotent anyway, but cheap insurance at the source.
            EnableIdempotence = true,

            MessageSendMaxRetries = 3,
            RetryBackoffMs = 200,
            LingerMs = 5
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<TPayload>(
        string topic,
        string eventType,
        string key,
        TPayload payload,
        CancellationToken ct = default) where TPayload : class
    {
        var envelope = new IntegrationEvent
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = CorrelationContext.CorrelationId,
            Payload = JsonSerializer.Serialize(payload)
        };

        await PublishEnvelopeAsync(topic, key, envelope, ct);
    }

    /// <summary>
    /// Publishes an already-built envelope. Used by the Outbox processor, which must
    /// preserve the EventId assigned when the row was written — that id is what makes
    /// consumer deduplication work across a redelivery.
    /// </summary>
    public async Task PublishEnvelopeAsync(
        string topic,
        string key,
        IntegrationEvent envelope,
        CancellationToken ct = default)
    {
        var message = new Message<string, string>
        {
            Key = key,
            Value = JsonSerializer.Serialize(envelope),
            Headers =
            [
                new Header(CorrelationContext.HeaderName, Encoding.UTF8.GetBytes(envelope.CorrelationId)),
                new Header("event-type", Encoding.UTF8.GetBytes(envelope.EventType))
            ]
        };

        try
        {
            var result = await _producer.ProduceAsync(topic, message, ct);

            _logger.LogInformation(
                "Published {EventType} {EventId} to {Topic}[{Partition}]@{Offset} (correlation {CorrelationId})",
                envelope.EventType, envelope.EventId, topic,
                result.Partition.Value, result.Offset.Value, envelope.CorrelationId);
        }
        catch (ProduceException<string, string> ex)
        {
            // Let this bubble. When called from the Outbox processor the row simply
            // stays unpublished and is retried on the next tick — which is the whole
            // point of the Outbox. Swallowing here would lose the event silently.
            _logger.LogError(ex,
                "Failed to publish {EventType} {EventId} to {Topic}: {Reason}",
                envelope.EventType, envelope.EventId, topic, ex.Error.Reason);
            throw;
        }
    }

    public void Dispose()
    {
        // Give in-flight messages a chance to land before the process exits.
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
