using BuildingBlocks.Core.Errors;

namespace ECommerce.Notifications.Infrastructure;

public sealed record CustomerContact(string Email, string? Phone, string FullName);

/// <summary>
/// Resolves a customer's contact details.
///
/// NotificationService could have kept its own copy of every customer's address by
/// consuming CustomerCreated/Updated, and at higher volume it should. It asks over HTTP
/// instead because notifications are low-volume and correctness beats throughput here:
/// sending an order confirmation to a stale address is worse than a slow send.
///
/// Falls back to a placeholder rather than throwing. A missing address must not stall
/// the Kafka partition behind it — the notification is recorded as failed and visible
/// in the history instead.
/// </summary>
public sealed class CustomerLookup(HttpClient http, ILogger<CustomerLookup> logger)
{
    private sealed record CustomerResponse(Guid Id, string Email, string? Phone, string FullName);

    public async Task<CustomerContact> GetContactAsync(Guid customerId, CancellationToken ct)
    {
        try
        {
            var customer = await http.GetFromJsonAsync<CustomerResponse>($"api/customers/{customerId}", ct);

            return customer is null
                ? Unknown(customerId)
                : new CustomerContact(customer.Email, customer.Phone, customer.FullName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not resolve contact details for customer {CustomerId}; " +
                "notification will be recorded as undeliverable", customerId);

            return Unknown(customerId);
        }
    }

    private static CustomerContact Unknown(Guid customerId) =>
        new($"unknown+{customerId}@invalid", null, "Unknown customer");
}

/// <summary>
/// Maps an order id to its customer.
///
/// Needed because PaymentService's events carry an order id but no customer id — and
/// correctly so: payments have no business knowing who the customer is.
/// </summary>
public sealed class OrderLookup(HttpClient http, ILogger<OrderLookup> logger)
{
    private sealed record OrderResponse(Guid Id, Guid CustomerId);

    public async Task<Guid?> GetCustomerIdAsync(Guid orderId, CancellationToken ct)
    {
        try
        {
            var order = await http.GetFromJsonAsync<OrderResponse>($"api/orders/{orderId}", ct);
            return order?.CustomerId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not resolve customer for order {OrderId}", orderId);
            return null;
        }
    }
}
