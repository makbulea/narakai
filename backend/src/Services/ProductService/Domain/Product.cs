namespace ECommerce.Products.Domain;

public enum ProductStatus
{
    Draft = 0,
    Active = 1,

    /// <summary>Visible in existing orders and history, not orderable any more.</summary>
    Discontinued = 2,
    Deleted = 3
}

public class Product
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Stock keeping unit. The identifier humans and warehouses actually use.</summary>
    public string Sku { get; private set; } = string.Empty;

    /// <summary>
    /// decimal, never double. Binary floating point cannot represent 0.1 exactly, so
    /// summing prices drifts — and the drift lands in a customer's invoice. Postgres
    /// maps this to numeric(18,2).
    /// </summary>
    public decimal Price { get; private set; }

    public string Currency { get; private set; } = "EUR";
    public string Category { get; private set; } = string.Empty;
    public ProductStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>
    /// Optimistic concurrency token, mapped to the Postgres xmin system column via
    /// UseXminAsConcurrencyToken. Two people editing the same row no longer silently
    /// overwrite each other; the second save fails and the caller gets a 409.
    /// </summary>
    public uint Version { get; private set; }

    private Product() { }

    public static Product Create(
        string name, string? description, string sku,
        decimal price, string currency, string category)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");

        var now = DateTimeOffset.UtcNow;

        return new Product
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            // SKUs are matched by warehouse staff and integrations; normalising the case
            // here is what makes the unique index meaningful.
            Sku = sku.Trim().ToUpperInvariant(),
            Price = decimal.Round(price, 2, MidpointRounding.ToEven),
            Currency = currency.Trim().ToUpperInvariant(),
            Category = category.Trim(),
            Status = ProductStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string name, string? description, decimal price, string category)
    {
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Price = decimal.Round(price, 2, MidpointRounding.ToEven);
        Category = category.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Activate()
    {
        if (Status == ProductStatus.Deleted)
            throw new InvalidOperationException("A deleted product cannot be activated.");

        Status = ProductStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Discontinue()
    {
        Status = ProductStatus.Discontinued;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkDeleted()
    {
        Status = ProductStatus.Deleted;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>OrderService checks this before accepting a line item.</summary>
    public bool IsOrderable => Status == ProductStatus.Active;
}
