using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BuildingBlocks.Persistence.Outbox;

/// <summary>
/// An event waiting to be published, stored in the *same database* as the business
/// data that produced it.
///
/// The problem this solves: saving an order and publishing OrderCreated are two
/// different systems. Whichever you do second can fail, and then either the order
/// exists with no event (inventory never reserves) or the event exists with no order
/// (inventory reserves stock for nothing). There is no ordering of two writes that
/// avoids this.
///
/// The Outbox turns two writes into one. The order row and the outbox row commit in a
/// single local transaction — atomic by definition. A background worker then moves
/// outbox rows to Kafka, retrying until they land. Delivery becomes at-least-once,
/// which consumers already tolerate because they deduplicate on EventId.
/// </summary>
[Table("outbox_messages")]
public class OutboxMessage
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Destination topic.</summary>
    [MaxLength(200)]
    public string Topic { get; set; } = string.Empty;

    /// <summary>Partition key — the aggregate id, so one aggregate's events stay ordered.</summary>
    [MaxLength(200)]
    public string PartitionKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Serialized payload. Stored, not re-derived, so a replay sends exactly what was intended.</summary>
    public string Payload { get; set; } = string.Empty;

    [MaxLength(100)]
    public string CorrelationId { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Null until the broker has acknowledged the write.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    /// <summary>
    /// Set once the row has failed too many times. Keeps a poison message from being
    /// retried forever and starving the rows queued behind it.
    /// </summary>
    public bool IsDeadLettered { get; set; }
}
