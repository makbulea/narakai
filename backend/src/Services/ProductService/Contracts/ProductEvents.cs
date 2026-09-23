namespace ECommerce.Products.Contracts;

public sealed record ProductCreatedPayload(
    Guid ProductId, string Sku, string Name, decimal Price,
    string Currency, string Category, DateTimeOffset CreatedAt);

public sealed record ProductUpdatedPayload(
    Guid ProductId, string Sku, string Name, decimal Price,
    string Currency, string Category, string Status, DateTimeOffset UpdatedAt);

public sealed record ProductDeletedPayload(Guid ProductId, string Sku, DateTimeOffset DeletedAt);
