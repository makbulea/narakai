using ECommerce.Notifications.Domain;

namespace ECommerce.Notifications.Infrastructure;

/// <summary>
/// A delivery channel. Two implementations, both fake — no SMTP, no SMS gateway, no
/// credentials to leak. Swapping in a real provider means one new class and one DI line.
/// </summary>
public interface INotificationProvider
{
    NotificationType Type { get; }

    /// <summary>Returns false for a soft failure (retryable). Throws for a hard one.</summary>
    Task<bool> SendAsync(string recipient, string subject, string content, CancellationToken ct);
}

public sealed class FakeEmailProvider(ILogger<FakeEmailProvider> logger) : INotificationProvider
{
    public NotificationType Type => NotificationType.Email;

    public async Task<bool> SendAsync(string recipient, string subject, string content, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(20, 120), ct);

        // 10% soft failure, so the retry path in NotificationRetryWorker is a code path
        // that actually runs rather than one nobody has ever observed.
        if (Random.Shared.NextDouble() < 0.10)
        {
            logger.LogWarning("Simulated email delivery failure to {Recipient}", Mask(recipient));
            return false;
        }

        // Recipient is masked. Logs get shipped, indexed and read by people who have no
        // business seeing a customer's address.
        logger.LogInformation(
            "EMAIL to {Recipient}: {Subject}", Mask(recipient), subject);

        return true;
    }

    /// <summary>a****@example.com — enough to correlate, not enough to identify.</summary>
    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***";

        return $"{email[0]}***{email[at..]}";
    }
}

public sealed class FakeSmsProvider(ILogger<FakeSmsProvider> logger) : INotificationProvider
{
    public NotificationType Type => NotificationType.Sms;

    public async Task<bool> SendAsync(string recipient, string subject, string content, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(10, 80), ct);

        if (Random.Shared.NextDouble() < 0.08)
        {
            logger.LogWarning("Simulated SMS delivery failure to {Recipient}", Mask(recipient));
            return false;
        }

        logger.LogInformation("SMS to {Recipient}: {Content}", Mask(recipient), Truncate(content));
        return true;
    }

    private static string Mask(string phone) =>
        phone.Length <= 4 ? "***" : $"***{phone[^4..]}";

    private static string Truncate(string s) => s.Length <= 60 ? s : s[..60] + "...";
}
