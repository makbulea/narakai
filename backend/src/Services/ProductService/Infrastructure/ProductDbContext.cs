using BuildingBlocks.Persistence;
using BuildingBlocks.Persistence.Idempotency;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Products.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Products.Infrastructure;

public class ProductDbContext(DbContextOptions<ProductDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Sku).HasMaxLength(64).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            // numeric(18,2): exact decimal arithmetic. See Product.Price for why.
            e.Property(x => x.Price).HasPrecision(18, 2);

            e.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("ux_products_sku");

            // The catalogue is browsed by category and filtered to orderable items.
            // Composite so the planner can satisfy both predicates from one index.
            e.HasIndex(x => new { x.Category, x.Status }).HasDatabaseName("ix_products_category_status");

            // Supports "cheapest first within a category".
            e.HasIndex(x => x.Price).HasDatabaseName("ix_products_price");

            // Postgres bumps the xmin system column on every update, so it works as a
            // free optimistic concurrency token — no version column to maintain.
            //
            // Note InventoryItem deliberately has none: its reservation path uses raw
            // SQL, and "SELECT *" does not return system columns, so EF would look for
            // an xmin that is not in the result set. A row lock guards it instead.
            e.Property(x => x.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
        });

        builder.ApplyOutboxAndIdempotency();

        base.OnModelCreating(builder);
    }
}
