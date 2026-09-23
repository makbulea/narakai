namespace BuildingBlocks.Messaging.Kafka;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>Consumer group. One per service, so each service sees every message once.</summary>
    public string ConsumerGroupId { get; set; } = "default-group";

    /// <summary>
    /// Deliveries attempted before a message is dead-lettered. Kept small: a message
    /// that fails five times is almost always a poison message, and retrying it
    /// forever blocks the partition behind it.
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    public int RetryBaseDelayMs { get; set; } = 500;

    /// <summary>Producer waits for all in-sync replicas. Slower, but no silent loss.</summary>
    public bool RequireAllAcks { get; set; } = true;
}
