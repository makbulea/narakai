using System.Text.Json;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Kafka;
using ECommerce.Notifications.Application;
using ECommerce.Notifications.Domain;
using ECommerce.Notifications.Infrastructure;
using Microsoft.Extensions.Options;

namespace ECommerce.Notifications.Consumers;

internal sealed record OrderConfirmedMessage(
    Guid OrderId, string OrderNumber, Guid CustomerId, decimal TotalAmount, string Currency);

internal sealed record OrderCancelledMessage(
    Guid OrderId, string OrderNumber, Guid CustomerId, string Reason);

/// <summary>
/// Turns order lifecycle events into customer notifications.
///
/// Deduplication is handled by the base class through IProcessedMessageStore, which is
/// what stops a redelivered OrderConfirmed producing a second confirmation email. That
/// guarantee is why this consumer can be written as if each event arrives exactly once.
/// </summary>
public sealed class OrderNotificationConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<OrderNotificationConsumer> logger)
    : KafkaConsumerBase(
        Topics.OrderEvents, nameof(OrderNotificationConsumer), scopeFactory, options, logger)
{
    protected override IReadOnlySet<string> HandledEventTypes { get; } =
        new HashSet<string> { EventTypes.OrderConfirmed, EventTypes.OrderCancelled };

    protected override async Task HandleAsync(
        IntegrationEvent envelope, IServiceProvider services, CancellationToken ct)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        var customers = services.GetRequiredService<CustomerLookup>();

        if (envelope.EventType == EventTypes.OrderConfirmed)
        {
            var message = JsonSerializer.Deserialize<OrderConfirmedMessage>(envelope.Payload);
            if (message is null) return;

            var contact = await customers.GetContactAsync(message.CustomerId, ct);

            await notifications.QueueAndSendAsync(
                Notification.Create(
                    message.CustomerId,
                    NotificationType.Email,
                    contact.Email,
                    $"Order {message.OrderNumber} confirmed",
                    $"Thank you. Order {message.OrderNumber} is confirmed for a total of " +
                    $"{message.TotalAmount:0.00} {message.Currency}.",
                    envelope.EventType,
                    envelope.EventId),
                ct);

            // A confirmation is worth both channels; a cancellation is not, because an
            // SMS about something that did not happen reads as a scam.
            if (!string.IsNullOrWhiteSpace(contact.Phone))
                await notifications.QueueAndSendAsync(
                    Notification.Create(
                        message.CustomerId,
                        NotificationType.Sms,
                        contact.Phone,
                        $"Order {message.OrderNumber}",
                        $"Order {message.OrderNumber} confirmed: {message.TotalAmount:0.00} {message.Currency}.",
                        envelope.EventType,
                        envelope.EventId),
                    ct);
        }
        else
        {
            var message = JsonSerializer.Deserialize<OrderCancelledMessage>(envelope.Payload);
            if (message is null) return;

            var contact = await customers.GetContactAsync(message.CustomerId, ct);

            await notifications.QueueAndSendAsync(
                Notification.Create(
                    message.CustomerId,
                    NotificationType.Email,
                    contact.Email,
                    $"Order {message.OrderNumber} cancelled",
                    $"Order {message.OrderNumber} was cancelled. Reason: {Humanise(message.Reason)}. " +
                    "Any amount reserved on your card will be released.",
                    envelope.EventType,
                    envelope.EventId),
                ct);
        }
    }

    /// <summary>
    /// Reasons travel as machine tokens ("payment_failed:card_expired"). Customers get
    /// a sentence, not a token.
    /// </summary>
    private static string Humanise(string reason) => reason switch
    {
        var r when r.StartsWith("payment_failed") => "the payment could not be completed",
        var r when r.StartsWith("stock_unavailable") => "one or more items were out of stock",
        var r when r.StartsWith("order_cancelled") => "the order was cancelled on request",
        _ => reason.Replace('_', ' ')
    };
}
