using BuildingBlocks.Core.Paging;
using ECommerce.Notifications.Domain;
using ECommerce.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Notifications.Application;

public sealed record NotificationResponse(
    Guid Id, Guid CustomerId, string Type, string Recipient, string Subject,
    string Content, string Status, int Attempts, string? LastError,
    string TriggeringEventType, DateTimeOffset CreatedAt, DateTimeOffset? SentAt)
{
    public static NotificationResponse From(Notification n) => new(
        n.Id, n.CustomerId, n.Type.ToString(), n.Recipient, n.Subject, n.Content,
        n.Status.ToString(), n.Attempts, n.LastError, n.TriggeringEventType, n.CreatedAt, n.SentAt);
}

public sealed record NotificationQuery : PageRequest
{
    public Guid? CustomerId { get; init; }
    public NotificationStatus? Status { get; init; }
    public NotificationType? Type { get; init; }
}

/// <summary>
/// Queues notifications and attempts delivery.
///
/// Delivery is attempted inline once, then handed to the retry worker if it fails.
/// Doing the first attempt inline means the common case (it works) is immediate, while
/// the worker keeps a provider outage from losing anything.
/// </summary>
public sealed class NotificationService(
    NotificationDbContext db,
    IEnumerable<INotificationProvider> providers,
    ILogger<NotificationService> logger)
{
    private readonly Dictionary<NotificationType, INotificationProvider> _providers =
        providers.ToDictionary(p => p.Type);

    public async Task QueueAndSendAsync(Notification notification, CancellationToken ct)
    {
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        await TrySendAsync(notification, ct);
    }

    /// <summary>
    /// One delivery attempt. Never throws — a failed notification must not fail the
    /// Kafka handler that created it, or the message would be redelivered and the
    /// customer would eventually get five copies of the same email.
    /// </summary>
    public async Task TrySendAsync(Notification notification, CancellationToken ct)
    {
        if (!_providers.TryGetValue(notification.Type, out var provider))
        {
            notification.MarkFailed($"No provider registered for {notification.Type}.");
            await db.SaveChangesAsync(ct);
            return;
        }

        try
        {
            var delivered = await provider.SendAsync(
                notification.Recipient, notification.Subject, notification.Content, ct);

            if (delivered)
                notification.MarkSent();
            else
                notification.MarkFailed("Provider reported a soft delivery failure.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Notification {NotificationId} delivery threw on attempt {Attempt}",
                notification.Id, notification.Attempts + 1);

            notification.MarkFailed(ex.Message);
        }

        await db.SaveChangesAsync(ct);

        if (notification.Status == NotificationStatus.Abandoned)
            logger.LogError(
                "Notification {NotificationId} abandoned after {Attempts} attempts: {Error}",
                notification.Id, notification.Attempts, notification.LastError);
    }

    public async Task<PagedResult<NotificationResponse>> ListAsync(
        NotificationQuery query, CancellationToken ct)
    {
        var q = db.Notifications.AsNoTracking().AsQueryable();

        if (query.CustomerId is { } customerId)
            q = q.Where(n => n.CustomerId == customerId);

        if (query.Status is { } status)
            q = q.Where(n => n.Status == status);

        if (query.Type is { } type)
            q = q.Where(n => n.Type == type);

        var total = await q.LongCountAsync(ct);
        if (total == 0)
            return PagedResult<NotificationResponse>.Empty(query.Page, query.PageSize);

        var items = await q
            .OrderByDescending(n => n.CreatedAt)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(n => NotificationResponse.From(n))
            .ToListAsync(ct);

        return new PagedResult<NotificationResponse>(items, query.Page, query.PageSize, total);
    }
}
