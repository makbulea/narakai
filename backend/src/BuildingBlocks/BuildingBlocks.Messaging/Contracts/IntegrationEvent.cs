using System.Text.Json.Serialization;

namespace BuildingBlocks.Messaging.Contracts;

/// <summary>
/// The envelope every message on every topic shares.
///
/// Deliberately thin. The payload type lives in the *publishing* service, not here —
/// a shared library holding every service's domain events would recouple the very
/// services we split apart. Consumers declare their own local shape for the payload
/// they care about and ignore fields they do not know, which is what lets a producer
/// add a field without a coordinated deploy.
/// </summary>
public sealed record IntegrationEvent
{
    [JsonPropertyName("eventId")]
    public required Guid EventId { get; init; }

    /// <summary>Discriminator such as "order.confirmed". Consumers switch on this.</summary>
    [JsonPropertyName("eventType")]
    public required string EventType { get; init; }

    [JsonPropertyName("occurredAt")]
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Ties this message to the HTTP request that ultimately caused it.</summary>
    [JsonPropertyName("correlationId")]
    public required string CorrelationId { get; init; }

    /// <summary>Serialized domain payload. Opaque to the transport.</summary>
    [JsonPropertyName("payload")]
    public required string Payload { get; init; }

    /// <summary>How many times delivery has been retried. Drives the dead-letter decision.</summary>
    [JsonPropertyName("attempt")]
    public int Attempt { get; init; }
}
