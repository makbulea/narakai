using System.Text.Json;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.Kafka;
using ECommerce.Orders.Contracts;
using ECommerce.Orders.Domain;
using ECommerce.Orders.Infrastructure;
using BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerce.Orders.Consumers;

internal sealed record PaymentSucceededMessage(Guid PaymentId, Guid OrderId, decimal Amount, string Currency);
internal sealed record PaymentFailedMessage(Guid PaymentId, Guid OrderId, string Reason);

/// <summary>
/// Resolves orders whose payment outcome arrived asynchronously.
///
/// The happy path is handled inline in OrderService.CreateAsync — the customer is
/// waiting and gets an answer immediately. This consumer exists for the case that path
/// cannot cover: the HTTP call to PaymentService timed out, so OrderService never
/// learned the result and left the order in StockReserved or PaymentPending.
///
/// Because both paths can run for the same order, every transition here is guarded by
/// the order's current state. An order already Confirmed by the synchronous path is
/// left alone rather than transitioned again.
/// </summary>
public sealed class PaymentEventsConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> options,
    ILogger<PaymentEventsConsumer> logger)
    : KafkaConsumerBase(
        Topics.PaymentEvents, nameof(PaymentEventsConsumer), scopeFactory, options, logger)
{
    protected override IReadOnlySet<string> HandledEventTypes { get; } =
        new HashSet<string> { EventTypes.PaymentSucceeded, EventTypes.PaymentFailed };

    protected override async Task HandleAsync(
        IntegrationEvent envelope, IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<OrderDbContext>();
        var outbox = services.GetRequiredService<IOutboxWriter>();

        if (envelope.EventType == EventTypes.PaymentSucceeded)
            await HandleSucceededAsync(envelope, db, outbox, ct);
        else
            await HandleFailedAsync(envelope, db, outbox, ct);
    }

    private async Task HandleSucceededAsync(
        IntegrationEvent envelope, OrderDbContext db, IOutboxWriter outbox, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<PaymentSucceededMessage>(envelope.Payload);
        if (message is null) return;

        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == message.OrderId, ct);

        if (order is null)
        {
            logger.LogWarning(
                "PaymentSucceeded for unknown order {OrderId}; ignoring", message.OrderId);
            return;
        }

        // The synchronous path already finished this order. Nothing to do — and
        // attempting the transition would throw on a terminal state.
        if (order.Status is OrderStatus.Confirmed)
        {
            logger.LogDebug("Order {OrderNumber} already confirmed; skipping", order.OrderNumber);
            return;
        }

        if (order.Status is not (OrderStatus.StockReserved or OrderStatus.PaymentPending))
        {
            logger.LogWarning(
                "PaymentSucceeded arrived for order {OrderNumber} in state {Status}; not confirming",
                order.OrderNumber, order.Status);
            return;
        }

        if (order.Status == OrderStatus.StockReserved)
            order.MarkPaymentPending(message.PaymentId);

        order.MarkPaid(message.PaymentId);
        order.Confirm();

        outbox.Enqueue(
            Topics.OrderEvents,
            EventTypes.OrderConfirmed,
            order.Id.ToString(),
            new OrderConfirmedPayload(
                order.Id, order.OrderNumber, order.CustomerId,
                order.TotalAmount, order.Currency, order.PaymentId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Order {OrderNumber} confirmed from PaymentSucceeded event", order.OrderNumber);
    }

    private async Task HandleFailedAsync(
        IntegrationEvent envelope, OrderDbContext db, IOutboxWriter outbox, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<PaymentFailedMessage>(envelope.Payload);
        if (message is null) return;

        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == message.OrderId, ct);

        if (order is null || order.IsTerminal) return;

        order.MarkPaymentFailed(message.Reason);

        // OrderCancelled is what releases the stock. InventoryService's consumer picks
        // it up; we do not call InventoryService from here, because a consumer that
        // makes synchronous calls to another service turns one outage into two.
        outbox.Enqueue(
            Topics.OrderEvents,
            EventTypes.OrderCancelled,
            order.Id.ToString(),
            new OrderCancelledPayload(
                order.Id, order.OrderNumber, order.CustomerId,
                $"payment_failed:{message.Reason}", DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Order {OrderNumber} marked PaymentFailed from event: {Reason}",
            order.OrderNumber, message.Reason);
    }
}
