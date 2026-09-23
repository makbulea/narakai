using ECommerce.Notifications.Domain;

namespace ECommerce.Notifications.Tests;

/// <summary>
/// Delivery retry behaviour. This is the code that decides whether a customer gets one
/// confirmation email, five, or none.
/// </summary>
public class NotificationRetryTests
{
    private static Notification NewNotification() =>
        Notification.Create(
            Guid.NewGuid(), NotificationType.Email, "ada@example.com",
            "Order confirmed", "Your order is confirmed.",
            "order.confirmed", Guid.NewGuid());

    [Fact]
    public void New_notification_is_pending_and_immediately_due()
    {
        var notification = NewNotification();

        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(0, notification.Attempts);
        Assert.True(notification.IsDue(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Sending_clears_the_retry_schedule()
    {
        var notification = NewNotification();

        notification.MarkSent();

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.NotNull(notification.SentAt);
        Assert.Null(notification.NextAttemptAt);
        Assert.False(notification.IsDue(DateTimeOffset.UtcNow.AddYears(1)));
    }

    [Fact]
    public void Failure_schedules_a_retry_in_the_future()
    {
        var notification = NewNotification();

        notification.MarkFailed("smtp timeout");

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.Attempts);
        Assert.Equal("smtp timeout", notification.LastError);
        Assert.NotNull(notification.NextAttemptAt);
        Assert.True(notification.NextAttemptAt > DateTimeOffset.UtcNow);

        // Not due yet — the worker must not pick it up on the next tick.
        Assert.False(notification.IsDue(DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Backoff grows with each attempt: 10s, 20s, 40s (plus up to 10s of jitter). The
    /// point is that a struggling provider gets increasing breathing room.
    /// </summary>
    [Fact]
    public void Backoff_grows_with_each_attempt()
    {
        var first = NewNotification();
        first.MarkFailed("attempt 1");
        var firstDelay = first.NextAttemptAt!.Value - DateTimeOffset.UtcNow;

        var second = NewNotification();
        second.MarkFailed("attempt 1");
        second.MarkFailed("attempt 2");
        var secondDelay = second.NextAttemptAt!.Value - DateTimeOffset.UtcNow;

        // 2^1*5 = 10s vs 2^2*5 = 20s. Jitter is capped at 10s, so the windows do not
        // overlap and this comparison is stable.
        Assert.True(secondDelay > firstDelay,
            $"expected the second delay ({secondDelay}) to exceed the first ({firstDelay})");
    }

    /// <summary>
    /// Notifications age badly — a confirmation delivered hours late is confusing rather
    /// than helpful. After MaxAttempts the message is abandoned, not retried forever.
    /// </summary>
    [Fact]
    public void Notification_is_abandoned_after_the_attempt_limit()
    {
        var notification = NewNotification();

        for (var i = 0; i < Notification.MaxAttempts; i++)
            notification.MarkFailed($"attempt {i + 1}");

        Assert.Equal(NotificationStatus.Abandoned, notification.Status);
        Assert.Equal(Notification.MaxAttempts, notification.Attempts);
        Assert.Null(notification.NextAttemptAt);

        // An abandoned notification is never due again.
        Assert.False(notification.IsDue(DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public void Long_error_messages_are_truncated_before_storage()
    {
        var notification = NewNotification();

        notification.MarkFailed(new string('x', 5000));

        Assert.Equal(1000, notification.LastError!.Length);
    }

    /// <summary>
    /// The triggering event is recorded so the delivery trail can be traced back to the
    /// Kafka message that caused it.
    /// </summary>
    [Fact]
    public void Triggering_event_is_recorded()
    {
        var eventId = Guid.NewGuid();

        var notification = Notification.Create(
            Guid.NewGuid(), NotificationType.Sms, "+441234567890",
            "Order", "Confirmed.", "order.confirmed", eventId);

        Assert.Equal("order.confirmed", notification.TriggeringEventType);
        Assert.Equal(eventId, notification.TriggeringEventId);
    }

    [Fact]
    public void Failed_notification_becomes_due_once_the_backoff_elapses()
    {
        var notification = NewNotification();
        notification.MarkFailed("temporary");

        Assert.False(notification.IsDue(DateTimeOffset.UtcNow));
        Assert.True(notification.IsDue(DateTimeOffset.UtcNow.AddMinutes(5)));
    }
}
