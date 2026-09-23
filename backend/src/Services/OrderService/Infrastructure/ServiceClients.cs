using System.Net;
using BuildingBlocks.Core.Errors;

namespace ECommerce.Orders.Infrastructure;

// Local shapes for the responses this service consumes. Deliberately not shared types:
// OrderService needs a product's price and name, not ProductService's full entity, and
// declaring only what we use means an unrelated field being added upstream cannot
// break deserialisation here.
public sealed record CustomerSummary(Guid Id, string Email, string FullName, string Status);
public sealed record ProductSummary(Guid Id, string Name, decimal Price, string Currency, bool IsOrderable);

public sealed record ReserveLine(Guid ProductId, int Quantity);
public sealed record ReserveStockRequest(Guid OrderId, IReadOnlyList<ReserveLine> Lines);
public sealed record ReserveStockResponse(
    bool Success, Guid OrderId, string? FailureReason,
    Guid? FailedProductId, int? Requested, int? Available);
public sealed record ReleaseStockRequest(Guid OrderId, string Reason);

public sealed record ProcessPaymentRequest(Guid OrderId, decimal Amount, string Currency, string IdempotencyKey);
public sealed record PaymentResponse(
    Guid Id, Guid OrderId, decimal Amount, string Currency, string Status, string? TransactionId, string? FailureReason);

/// <summary>
/// Talks to CustomerService. All four clients follow the same shape: translate transport
/// failures into DownstreamServiceException so the controller returns 502 rather than
/// leaking an HttpRequestException as a 500.
/// </summary>
public sealed class CustomerServiceClient(HttpClient http, ILogger<CustomerServiceClient> logger)
{
    public async Task<CustomerSummary?> GetCustomerAsync(Guid customerId, CancellationToken ct)
    {
        try
        {
            var response = await http.GetAsync($"api/customers/{customerId}", ct);

            // A missing customer is an answer, not a failure. Returning null lets the
            // caller produce a clean 422 instead of a 502.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CustomerSummary>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "CustomerService call failed for {CustomerId}", customerId);
            throw new DownstreamServiceException("CustomerService", "Could not reach CustomerService.", ex);
        }
    }
}

public sealed class ProductServiceClient(HttpClient http, ILogger<ProductServiceClient> logger)
{
    /// <summary>
    /// One call for the whole basket. Fetching products one at a time would multiply
    /// order latency by the number of lines and is the usual reason a microservice
    /// checkout feels slow.
    /// </summary>
    public async Task<IReadOnlyList<ProductSummary>> GetProductsAsync(
        IReadOnlyList<Guid> productIds, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                "api/products/bulk", new { ProductIds = productIds }, ct);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<ProductSummary>>(ct) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "ProductService bulk lookup failed for {Count} ids", productIds.Count);
            throw new DownstreamServiceException("ProductService", "Could not reach ProductService.", ex);
        }
    }
}

public sealed class InventoryServiceClient(HttpClient http, ILogger<InventoryServiceClient> logger)
{
    public async Task<ReserveStockResponse> ReserveAsync(ReserveStockRequest request, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/inventory/reserve", request, ct);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ReserveStockResponse>(ct)
                   ?? new ReserveStockResponse(false, request.OrderId, "Empty response", null, null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Inventory reserve failed for order {OrderId}", request.OrderId);
            throw new DownstreamServiceException("InventoryService", "Could not reach InventoryService.", ex);
        }
    }

    /// <summary>
    /// Compensating call. Never throws: this runs on a path that is already failing,
    /// and turning a compensation error into an exception would replace a clear
    /// "payment declined" message with an opaque 502. If it fails, the OrderCancelled
    /// event still reaches InventoryService's consumer and releases the stock there.
    /// </summary>
    public async Task<bool> TryReleaseAsync(Guid orderId, string reason, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                "api/inventory/release", new ReleaseStockRequest(orderId, reason), ct);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Synchronous stock release failed for order {OrderId}; " +
                "falling back to the OrderCancelled event path", orderId);
            return false;
        }
    }
}

public sealed class PaymentServiceClient(HttpClient http, ILogger<PaymentServiceClient> logger)
{
    public async Task<PaymentResponse> ProcessAsync(ProcessPaymentRequest request, CancellationToken ct)
    {
        try
        {
            // The idempotency key rides in a header as well as the body so PaymentService
            // can reject a duplicate before deserialising anything.
            using var message = new HttpRequestMessage(HttpMethod.Post, "api/payments")
            {
                Content = JsonContent.Create(request)
            };
            message.Headers.Add("Idempotency-Key", request.IdempotencyKey);

            var response = await http.SendAsync(message, ct);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<PaymentResponse>(ct)
                   ?? throw new DownstreamServiceException("PaymentService", "Empty payment response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "Payment call failed for order {OrderId}", request.OrderId);
            throw new DownstreamServiceException("PaymentService", "Could not reach PaymentService.", ex);
        }
    }
}
