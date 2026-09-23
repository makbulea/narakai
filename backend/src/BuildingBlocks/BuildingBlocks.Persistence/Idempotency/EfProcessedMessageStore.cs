using BuildingBlocks.Messaging.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Persistence.Idempotency;

/// <summary>
/// EF Core backed deduplication store, one per service database.
/// </summary>
public sealed class EfProcessedMessageStore<TDbContext>(TDbContext db) : IProcessedMessageStore
    where TDbContext : DbContext
{
    public Task<bool> HasProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default) =>
        db.Set<ProcessedMessage>()
            .AsNoTracking()
            .AnyAsync(m => m.EventId == eventId && m.ConsumerName == consumerName, ct);

    public async Task MarkProcessedAsync(
        Guid eventId, string consumerName, string eventType, CancellationToken ct = default)
    {
        db.Set<ProcessedMessage>().Add(new ProcessedMessage
        {
            EventId = eventId,
            ConsumerName = consumerName,
            EventType = eventType,
            ProcessedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two consumers in the same group raced on the same message. The primary
            // key rejected the second insert, which is precisely the guarantee we
            // wanted — treat it as success rather than failing the handler.
            db.ChangeTracker.Clear();
        }
    }
}
