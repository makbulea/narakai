namespace BuildingBlocks.Persistence.Outbox;

/// <summary>
/// Enqueues an event into the caller's own transaction.
///
/// Implementations must NOT call SaveChanges. The point is that the caller commits
/// business data and outbox row together; saving here would split them apart again
/// and reintroduce the dual-write problem.
/// </summary>
public interface IOutboxWriter
{
    void Enqueue<TPayload>(string topic, string eventType, string partitionKey, TPayload payload)
        where TPayload : class;
}
