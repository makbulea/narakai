using BuildingBlocks.Caching.Redis;
using BuildingBlocks.Core.Errors;
using BuildingBlocks.Core.Paging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Products.Contracts;
using ECommerce.Products.Domain;
using ECommerce.Products.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Products.Application;

/// <summary>
/// Catalogue use cases.
///
/// This is the one service in the system that caches. The justification, spelled out
/// because "we have Redis" is not one: GetById is called by OrderService for every line
/// item of every order, the catalogue changes a few times a day, and a customer seeing
/// a five-minute-old description is harmless. Writes evict rather than update, so a
/// price change is visible on the next read instead of after the TTL.
/// </summary>
public sealed class ProductService(
    ProductDbContext db,
    IOutboxWriter outbox,
    ICacheService cache,
    ILogger<ProductService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static string CacheKey(Guid id) => $"product:{id}";
    private const string ListCachePrefix = "product:list:";

    public async Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken ct)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();

        if (await db.Products.AnyAsync(p => p.Sku == sku, ct))
            throw new ConflictException($"A product with SKU '{sku}' already exists.");

        var product = Product.Create(
            request.Name, request.Description, request.Sku,
            request.Price, request.Currency, request.Category);

        db.Products.Add(product);

        outbox.Enqueue(
            Topics.ProductEvents,
            EventTypes.ProductCreated,
            product.Id.ToString(),
            new ProductCreatedPayload(
                product.Id, product.Sku, product.Name, product.Price,
                product.Currency, product.Category, product.CreatedAt));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new ConflictException($"A product with SKU '{sku}' already exists.");
        }

        // A new product changes every list page it could appear on.
        await cache.RemoveByPrefixAsync(ListCachePrefix, ct);

        logger.LogInformation("Product {ProductId} created with SKU {Sku}", product.Id, product.Sku);

        return ProductResponse.From(product);
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var cached = await cache.GetOrSetAsync(
            CacheKey(id),
            async token =>
            {
                var product = await db.Products
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == id && p.Status != ProductStatus.Deleted, token);

                return product is null ? null : ProductResponse.From(product);
            },
            CacheTtl, ct);

        // Deliberately not cached as a negative result: a product created a moment ago
        // would otherwise keep returning 404 until the TTL expired.
        return cached ?? throw new NotFoundException("Product", id);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = await db.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.Status != ProductStatus.Deleted, ct)
            ?? throw new NotFoundException("Product", id);

        product.Update(request.Name, request.Description, request.Price, request.Category);

        outbox.Enqueue(
            Topics.ProductEvents,
            EventTypes.ProductUpdated,
            product.Id.ToString(),
            new ProductUpdatedPayload(
                product.Id, product.Sku, product.Name, product.Price, product.Currency,
                product.Category, product.Status.ToString(), product.UpdatedAt));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This product was modified by someone else. Reload and try again.");
        }

        // Evict, do not overwrite. Writing the new value here would still be stale if a
        // second update committed between our SaveChanges and this line; deleting is
        // always correct because the next read repopulates from the source of truth.
        await cache.RemoveAsync(CacheKey(id), ct);
        await cache.RemoveByPrefixAsync(ListCachePrefix, ct);

        return ProductResponse.From(product);
    }

    public async Task ActivateAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("Product", id);

        product.Activate();
        await db.SaveChangesAsync(ct);

        await cache.RemoveAsync(CacheKey(id), ct);
        await cache.RemoveByPrefixAsync(ListCachePrefix, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products
            .FirstOrDefaultAsync(p => p.Id == id && p.Status != ProductStatus.Deleted, ct)
            ?? throw new NotFoundException("Product", id);

        // Soft delete: existing orders reference this product and must keep resolving.
        product.MarkDeleted();

        outbox.Enqueue(
            Topics.ProductEvents,
            EventTypes.ProductDeleted,
            product.Id.ToString(),
            new ProductDeletedPayload(product.Id, product.Sku, product.UpdatedAt));

        await db.SaveChangesAsync(ct);

        await cache.RemoveAsync(CacheKey(id), ct);
        await cache.RemoveByPrefixAsync(ListCachePrefix, ct);

        logger.LogInformation("Product {ProductId} ({Sku}) soft-deleted", product.Id, product.Sku);
    }

    public async Task<PagedResult<ProductResponse>> ListAsync(ProductQuery query, CancellationToken ct)
    {
        var q = db.Products.AsNoTracking().Where(p => p.Status != ProductStatus.Deleted);

        if (!string.IsNullOrWhiteSpace(query.Category))
            q = q.Where(p => p.Category == query.Category);

        if (query.Status is { } status)
            q = q.Where(p => p.Status == status);

        if (query.MinPrice is { } min)
            q = q.Where(p => p.Price >= min);

        if (query.MaxPrice is { } max)
            q = q.Where(p => p.Price <= max);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            q = q.Where(p =>
                EF.Functions.ILike(p.Name, term) ||
                EF.Functions.ILike(p.Sku, term) ||
                (p.Description != null && EF.Functions.ILike(p.Description, term)));
        }

        var total = await q.LongCountAsync(ct);
        if (total == 0)
            return PagedResult<ProductResponse>.Empty(query.Page, query.PageSize);

        q = ApplySort(q, query);

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(p => ProductResponse.From(p))
            .ToListAsync(ct);

        return new PagedResult<ProductResponse>(items, query.Page, query.PageSize, total);
    }

    /// <summary>
    /// Batch lookup for OrderService. Returns only orderable products; the caller
    /// compares counts to work out which ids were rejected and why.
    /// </summary>
    public async Task<IReadOnlyList<ProductResponse>> GetManyAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        // Guard against an unbounded IN clause. A caller asking for 10,000 ids is a bug
        // on their side, and letting it through would plan a query Postgres struggles with.
        if (ids.Count > 200)
            throw new BusinessRuleException(
                "bulk_lookup_limit", "At most 200 product ids may be requested at once.");

        var products = await db.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Status != ProductStatus.Deleted)
            .ToListAsync(ct);

        return products.Select(ProductResponse.From).ToList();
    }

    private static IQueryable<Product> ApplySort(IQueryable<Product> q, ProductQuery query) =>
        (query.SortBy?.ToLowerInvariant(), query.SortDescending) switch
        {
            ("price", false) => q.OrderBy(p => p.Price),
            ("price", true) => q.OrderByDescending(p => p.Price),
            ("name", false) => q.OrderBy(p => p.Name),
            ("name", true) => q.OrderByDescending(p => p.Name),
            ("createdat", false) => q.OrderBy(p => p.CreatedAt),
            _ => q.OrderByDescending(p => p.CreatedAt)
        };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
