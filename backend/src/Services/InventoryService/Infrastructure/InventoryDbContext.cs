using BuildingBlocks.Persistence;
using BuildingBlocks.Persistence.Idempotency;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Inventory.Infrastructure;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockReservation> Reservations => Set<StockReservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<InventoryItem>(e =>
        {
            e.ToTable("inventory_items", t =>
            {
                // Belt and braces. The domain refuses to go negative and the row lock
                // makes the check reliable, but a bad migration or a manual UPDATE
                // would bypass both. The database is the last thing standing.
                t.HasCheckConstraint("ck_inventory_available_non_negative", "available_quantity >= 0");
                t.HasCheckConstraint("ck_inventory_reserved_non_negative", "reserved_quantity >= 0");
            });

            e.HasKey(x => x.Id);

            // One row per product: the reservation path locks by product id, and a
            // duplicate row would let two orders lock different rows for the same stock.
            e.HasIndex(x => x.ProductId).IsUnique().HasDatabaseName("ux_inventory_product");

            e.Property(x => x.AvailableQuantity).HasColumnName("available_quantity");
            e.Property(x => x.ReservedQuantity).HasColumnName("reserved_quantity");
        });

        builder.Entity<StockReservation>(e =>
        {
            e.ToTable("stock_reservations");
            e.HasKey(x => x.Id);

            // Makes reservation itself idempotent: a retried reserve call for the same
            // order line hits this constraint instead of double-reserving.
            e.HasIndex(x => new { x.OrderId, x.ProductId })
                .IsUnique()
                .HasDatabaseName("ux_reservation_order_product");

            e.HasIndex(x => x.Status).HasDatabaseName("ix_reservation_status");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        builder.ApplyOutboxAndIdempotency();

        base.OnModelCreating(builder);
    }
}
