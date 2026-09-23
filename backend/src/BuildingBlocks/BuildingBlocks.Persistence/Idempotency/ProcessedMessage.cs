using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BuildingBlocks.Persistence.Idempotency;

/// <summary>
/// A record that this service has already handled a given event.
///
/// Composite key (EventId, ConsumerName): two different consumers in the same service
/// must each get their own shot at the message, so the event id alone is not enough.
///
/// Lives in the service's own database rather than Redis on purpose — the dedup row
/// has to commit in the same transaction as the work it guards. If it were in Redis,
/// a crash between "handled the payment" and "recorded it in Redis" would leave the
/// payment applied and the message eligible for reprocessing.
/// </summary>
// Composite key is configured in ApplyOutboxAndIdempotency rather than by
// attribute, so all mapping for these tables lives in one readable place.
[Table("processed_messages")]
public class ProcessedMessage
{
    public Guid EventId { get; set; }

    [MaxLength(200)]
    public string ConsumerName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; set; }
}
