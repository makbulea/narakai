namespace BuildingBlocks.Messaging.Idempotency;

/// <summary>
/// Records which event ids this service has already handled.
///
/// Kafka gives at-least-once delivery: a consumer that processes a message and then
/// crashes before committing its offset will see that message again. Without a record
/// of what has been handled, "PaymentSucceeded" would confirm the same order twice.
///
/// The interface lives here; the implementation lives in each service's own database,
/// because the dedup record must commit in the *same transaction* as the side effect
/// it protects. A shared Redis set would be faster and would reintroduce exactly the
/// dual-write problem the Outbox exists to avoid.
/// </summary>
public interface IProcessedMessageStore
{
    Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default);

    Task MarkProcessedAsync(Guid eventId, string consumerName, string eventType, CancellationToken ct = default);
}
