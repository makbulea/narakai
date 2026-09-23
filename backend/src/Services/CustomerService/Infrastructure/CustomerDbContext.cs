using BuildingBlocks.Persistence;
using BuildingBlocks.Persistence.Idempotency;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Customers.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Customers.Infrastructure;

/// <summary>
/// customer_db. Nothing outside this service opens a connection to it — other services
/// ask over HTTP or react to events.
/// </summary>
public class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    // The outbox lives in this same database on purpose: that is what makes writing a
    // customer and queueing CustomerCreated a single atomic commit.
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.HasKey(x => x.Id);

            e.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            // Unique on email regardless of status: a deleted customer keeps their
            // address reserved. Freeing it would let a new signup inherit the order
            // history of the old one in any report that joins on email.
            e.HasIndex(x => x.Email).IsUnique().HasDatabaseName("ux_customers_email");

            // Supports the common "list active customers, newest first" query.
            e.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("ix_customers_status_created");

            // xmin is a system column Postgres bumps on every update — free optimistic
            // concurrency without maintaining our own version column.
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
