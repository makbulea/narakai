using System.Text.Json;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Kafka;
using ECommerce.Notifications.Application;
using ECommerce.Notifications.Domain;
using ECommerce.Notifications.Infrastructure;
using Microsoft.Extensions.Options;

namespace ECommerce.Notifications.Consumers;

internal sealed record PaymentSucceededMessage(
    Guid PaymentId, Guid OrderId, decimal Amount, string Currency, string TransactionId);

internal sealed record PaymentFailedMessage(
    Guid PaymentId, Guid OrderId, decimal Amount, string Currency, string Reason);

/// <summary>
/// Payment receipts and decline notices.
///
/// A separate consumer from OrderNotificationConsumer, and separately named in the
/// dedup store, so that a redelivery on the order topic cannot suppress a payment
/// notification or vice versa. Both subscribe with the same consumer group but track
/// their own processed-message rows.
/// </summary>
public sealed class PaymentNotificationConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<PaymentNotificationConsumer> logger)
    : KafkaConsumerBase(
        Topics.PaymentEvents, nameof(PaymentNotificationConsumer), scopeFactory, options, logger)
{
    protected override IReadOnlySet<string> HandledEventTypes { get; } =
        new HashSet<string> { EventTypes.PaymentSucceeded, EventTypes.PaymentFailed, EventTypes.PaymentRefunded };

    protected override async Task HandleAsync(
        IntegrationEvent envelope, IServiceProvider services, CancellationToken ct)
    {
        var notifications = services.GetRequiredService<NotificationService>();
        var customers = services.GetRequiredService<CustomerLookup>();

        // PaymentService knows the order, not the customer — service boundaries mean it
        // has no reason to. We resolve the customer through OrderService instead.
        var orders = services.GetRequiredService<OrderLookup>();

        if (envelope.EventType == EventTypes.PaymentFailed)
        {
            var message = JsonSerializer.Deserialize<PaymentFailedMessage>(envelope.Payload);
            if (message is null) return;

            var customerId = await orders.GetCustomerIdAsync(message.OrderId, ct);
            if (customerId is null) return;

            var contact = await customers.GetContactAsync(customerId.Value, ct);

            await notifications.QueueAndSendAsync(
                Notification.Create(
                    customerId.Value,
                    NotificationType.Email,
                    contact.Email,
                    "Payment could not be completed",
                    $"We could not take payment of {message.Amount:0.00} {message.Currency}. " +
                    "No money has left your account and any reserved items have been released.",
                    envelope.EventType,
                    envelope.EventId),
                ct);

            return;
        }

        if (envelope.EventType == EventTypes.PaymentSucceeded)
        {
            var message = JsonSerializer.Deserialize<PaymentSucceededMessage>(envelope.Payload);
            if (message is null) return;

            var customerId = await orders.GetCustomerIdAsync(message.OrderId, ct);
            if (customerId is null) return;

            var contact = await customers.GetContactAsync(customerId.Value, ct);

            await notifications.QueueAndSendAsync(
                Notification.Create(
                    customerId.Value,
                    NotificationType.Email,
                    contact.Email,
                    "Payment receipt",
                    $"We received {message.Amount:0.00} {message.Currency}. " +
                    // The transaction id is a provider reference, not a secret, and the
                    // customer needs it when disputing a charge. No card data appears here.
                    $"Reference: {message.TransactionId}.",
                    envelope.EventType,
                    envelope.EventId),
                ct);
        }
    }
}
