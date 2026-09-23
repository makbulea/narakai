using ECommerce.Notifications.Domain;
using ECommerce.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Notifications.Application;

/// <summary>
/// Retries notifications whose inline delivery attempt failed.
///
/// Runs on a timer rather than reacting to anything, because the thing it is waiting for
/// — a provider recovering — produces no event. Rows carry their own NextAttemptAt, so
/// the worker only has to ask "what is due" rather than track schedules in memory.
/// </summary>
public sealed class NotificationRetryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationRetryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private const int BatchSize = 25;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        logger.LogInformation("Notification retry worker started, polling every {Interval}", PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Retry sweep failed; will try again next tick");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessDueAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

        var now = DateTimeOffset.UtcNow;

        var due = await db.Notifications
            .Where(n => n.Status == NotificationStatus.Failed
                        && n.NextAttemptAt != null
                        && n.NextAttemptAt <= now)
            .OrderBy(n => n.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        logger.LogInformation("Retrying {Count} notification(s)", due.Count);

        foreach (var notification in due)
            await notifications.TrySendAsync(notification, ct);
    }
}
