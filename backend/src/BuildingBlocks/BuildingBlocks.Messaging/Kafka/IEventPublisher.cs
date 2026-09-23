namespace BuildingBlocks.Messaging.Kafka;

/// <summary>
/// Publishes a domain payload as an integration event.
///
/// Application code never sees Kafka types. That matters for two reasons: services
/// stay testable without a broker, and the Outbox can implement this same interface
/// so "publish" becomes "write a row in my transaction" wherever atomicity matters.
/// </summary>
public interface IEventPublisher
{
    /// <param name="key">
    /// Partition key. Use the aggregate id (order id, product id) so all events for
    /// one aggregate land on one partition and stay ordered relative to each other.
    /// </param>
    Task PublishAsync<TPayload>(
        string topic,
        string eventType,
        string key,
        TPayload payload,
        CancellationToken ct = default) where TPayload : class;
}
