using BuildingBlocks.Core.Paging;
using ECommerce.Products.Domain;

namespace ECommerce.Products.Application;

public sealed record CreateProductRequest(
    string Name, string? Description, string Sku,
    decimal Price, string Currency, string Category);

public sealed record UpdateProductRequest(
    string Name, string? Description, decimal Price, string Category);

public sealed record ProductResponse(
    Guid Id, string Name, string? Description, string Sku,
    decimal Price, string Currency, string Category, string Status,
    bool IsOrderable, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static ProductResponse From(Product p) => new(
        p.Id, p.Name, p.Description, p.Sku, p.Price, p.Currency,
        p.Category, p.Status.ToString(), p.IsOrderable, p.CreatedAt, p.UpdatedAt);
}

public sealed record ProductQuery : PageRequest
{
    public string? Search { get; init; }
    public string? Category { get; init; }
    public ProductStatus? Status { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
}

/// <summary>
/// Batch lookup used by OrderService: one call for every line item on an order rather
/// than N round trips. Chatty service-to-service calls are the usual reason a "fast"
/// microservice system is slow.
/// </summary>
public sealed record BulkProductRequest(IReadOnlyList<Guid> ProductIds);
