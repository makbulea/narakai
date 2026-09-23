using BuildingBlocks.Persistence;
using BuildingBlocks.Persistence.Idempotency;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Payments.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Payments.Infrastructure;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasKey(x => x.Id);

            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.TransactionId).HasMaxLength(100);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();

            // THE guarantee against double charging. Redis is checked first because it
            // is faster, but this index is what makes the promise true even if Redis is
            // empty, restarted, or lying.
            e.HasIndex(x => x.IdempotencyKey)
                .IsUnique()
                .HasDatabaseName("ux_payments_idempotency_key");

            e.HasIndex(x => x.OrderId).HasDatabaseName("ix_payments_order");
            e.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_payments_status_created");

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
