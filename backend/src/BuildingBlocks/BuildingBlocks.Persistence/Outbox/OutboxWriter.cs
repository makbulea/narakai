using System.Text.Json;
using BuildingBlocks.Core.Correlation;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Persistence.Outbox;

/// <summary>
/// Adds outbox rows to the caller's change tracker. Never saves — see
/// <see cref="IOutboxWriter"/> for why that matters.
/// </summary>
public sealed class OutboxWriter<TDbContext>(TDbContext db) : IOutboxWriter
    where TDbContext : DbContext
{
    public void Enqueue<TPayload>(string topic, string eventType, string partitionKey, TPayload payload)
        where TPayload : class
    {
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Topic = topic,
            EventType = eventType,
            PartitionKey = partitionKey,
            Payload = JsonSerializer.Serialize(payload),
            CorrelationId = CorrelationContext.CorrelationId,
            OccurredAt = DateTimeOffset.UtcNow
        });
    }
}
