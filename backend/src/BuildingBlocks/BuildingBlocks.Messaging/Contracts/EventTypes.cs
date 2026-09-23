namespace BuildingBlocks.Messaging.Contracts;

/// <summary>
/// Every event type string in the system, in one place.
///
/// These are wire contracts: changing a value breaks running consumers, so they are
/// constants rather than an enum (an enum tempts you into renaming members, and the
/// rename compiles fine while the deployed consumers stop matching).
/// </summary>
public static class EventTypes
{
    public const string CustomerCreated = "customer.created";
    public const string CustomerUpdated = "customer.updated";
    public const string CustomerDeleted = "customer.deleted";

    public const string ProductCreated = "product.created";
    public const string ProductUpdated = "product.updated";
    public const string ProductDeleted = "product.deleted";

    public const string StockReserved = "inventory.stock_reserved";
    public const string StockReservationFailed = "inventory.stock_reservation_failed";
    public const string StockReleased = "inventory.stock_released";
    public const string StockUpdated = "inventory.stock_updated";

    public const string OrderCreated = "order.created";
    public const string OrderConfirmed = "order.confirmed";
    public const string OrderCancelled = "order.cancelled";

    public const string PaymentSucceeded = "payment.succeeded";
    public const string PaymentFailed = "payment.failed";
    public const string PaymentRefunded = "payment.refunded";

    public const string NotificationSent = "notification.sent";
    public const string NotificationFailed = "notification.failed";
}

/// <summary>Topic names. One topic per publishing service keeps ordering per aggregate simple.</summary>
public static class Topics
{
    public const string CustomerEvents = "customer-events";
    public const string ProductEvents = "product-events";
    public const string InventoryEvents = "inventory-events";
    public const string OrderEvents = "order-events";
    public const string PaymentEvents = "payment-events";
    public const string NotificationEvents = "notification-events";

    /// <summary>Dead-letter topic for a given source topic.</summary>
    public static string DeadLetterFor(string topic) => $"{topic}.dlq";
}
