using BuildingBlocks.Persistence;
using BuildingBlocks.Persistence.Idempotency;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Orders.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Orders.Infrastructure;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(x => x.Id);

            e.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.Property(x => x.CancellationReason).HasMaxLength(500);

            e.HasIndex(x => x.OrderNumber).IsUnique().HasDatabaseName("ux_orders_number");

            // "My orders, newest first" — the single most common query in the system.
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt }).HasDatabaseName("ix_orders_customer_created");

            // Supports operational sweeps: find orders stuck in PaymentPending.
            e.HasIndex(x => new { x.Status, x.UpdatedAt }).HasDatabaseName("ix_orders_status_updated");

            // Postgres bumps the xmin system column on every update, so it works as a
            // free optimistic concurrency token — no version column to maintain.
            //
            // Note InventoryItem deliberately has none: its reservation path uses raw
            // SQL, and "SELECT *" does not return system columns, so EF would look for
            // an xmin that is not in the result set. A row lock guards it instead.
            e.Property(x => x.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");

            // Items are part of the Order aggregate and are never loaded on their own.
            e.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(i => i.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(x => x.Id);

            e.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.TotalPrice).HasPrecision(18, 2);

            e.HasIndex(x => x.ProductId).HasDatabaseName("ix_order_items_product");
        });

        builder.ApplyOutboxAndIdempotency();

        base.OnModelCreating(builder);
    }
}
