using System.Text.Json;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Kafka;
using ECommerce.Inventory.Application;
using Microsoft.Extensions.Options;

namespace ECommerce.Inventory.Consumers;

/// <summary>Local view of the OrderCancelled payload. Only the fields this service needs.</summary>
internal sealed record OrderCancelledMessage(Guid OrderId, Guid CustomerId, string Reason);

/// <summary>
/// Releases stock when an order is cancelled.
///
/// This is the asynchronous half of the compensation story. OrderService also releases
/// synchronously on the failure path it can see (payment declined while it is still
/// handling the request), but an order can be cancelled long afterwards — by a support
/// agent, or by a timeout — and nothing is holding an HTTP connection then.
///
/// Both paths converge on InventoryService.ReleaseAsync, which is idempotent, so the
/// stock is released exactly once no matter how many times it is asked.
/// </summary>
public sealed class OrderCancelledConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<OrderCancelledConsumer> logger)
    : KafkaConsumerBase(
        Topics.OrderEvents, nameof(OrderCancelledConsumer), scopeFactory, options, logger)
{
    protected override IReadOnlySet<string> HandledEventTypes { get; } =
        new HashSet<string> { EventTypes.OrderCancelled };

    protected override async Task HandleAsync(
        IntegrationEvent envelope, IServiceProvider services, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<OrderCancelledMessage>(envelope.Payload);
        if (message is null)
        {
            logger.LogWarning("OrderCancelled {EventId} had an empty payload", envelope.EventId);
            return;
        }

        var inventory = services.GetRequiredService<InventoryService>();

        await inventory.ReleaseAsync(
            message.OrderId, $"order_cancelled:{message.Reason}", ct);
    }
}
