namespace ECommerce.Notifications.Domain;

public enum NotificationType
{
    Email = 0,
    Sms = 1
}

public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,

    /// <summary>Delivery failed but attempts remain. The retry worker will pick it up.</summary>
    Failed = 2,

    /// <summary>Out of attempts. Needs a human, or nothing at all.</summary>
    Abandoned = 3
}

public class Notification
{
    /// <summary>
    /// Attempts before giving up. Kept low because notifications age badly: an order
    /// confirmation delivered two hours late is worse than useless, it is confusing.
    /// </summary>
    public const int MaxAttempts = 4;

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Email address or phone number, depending on <see cref="Type"/>.</summary>
    public string Recipient { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public NotificationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>The event that caused this notification. Makes the trail auditable.</summary>
    public string TriggeringEventType { get; private set; } = string.Empty;
    public Guid TriggeringEventId { get; private set; }

    private Notification() { }

    public static Notification Create(
        Guid customerId, NotificationType type, string recipient,
        string subject, string content, string eventType, Guid eventId) => new()
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Type = type,
            Recipient = recipient,
            Subject = subject,
            Content = content,
            Status = NotificationStatus.Pending,
            Attempts = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            NextAttemptAt = DateTimeOffset.UtcNow,
            TriggeringEventType = eventType,
            TriggeringEventId = eventId
        };

    public void MarkSent()
    {
        Status = NotificationStatus.Sent;
        SentAt = DateTimeOffset.UtcNow;
        NextAttemptAt = null;
        LastError = null;
    }

    /// <summary>
    /// Records a failure and schedules the next attempt with exponential backoff plus
    /// jitter. Jitter matters here for the same reason it does everywhere else: a
    /// provider outage fails every queued notification at once, and without jitter they
    /// all come back on the same second.
    /// </summary>
    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;

        if (Attempts >= MaxAttempts)
        {
            Status = NotificationStatus.Abandoned;
            NextAttemptAt = null;
            return;
        }

        Status = NotificationStatus.Failed;

        var backoffSeconds = Math.Pow(2, Attempts) * 5;
        var jitterSeconds = Random.Shared.Next(0, 10);

        NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds + jitterSeconds);
    }

    public bool IsDue(DateTimeOffset now) =>
        Status is NotificationStatus.Pending or NotificationStatus.Failed
        && NextAttemptAt is not null
        && NextAttemptAt <= now;
}
