using BuildingBlocks.Persistence;
using BuildingBlocks.Persistence.Idempotency;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Notifications.Infrastructure;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => x.Id);

            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Recipient).HasMaxLength(320).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            e.Property(x => x.Content).HasMaxLength(4000).IsRequired();
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.TriggeringEventType).HasMaxLength(100);

            // Drives the retry worker's query: due, unsent, oldest first.
            e.HasIndex(x => new { x.Status, x.NextAttemptAt }).HasDatabaseName("ix_notifications_due");

            // Notification history for a customer.
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt }).HasDatabaseName("ix_notifications_customer");
        });

        builder.ApplyOutboxAndIdempotency();

        base.OnModelCreating(builder);
    }
}
