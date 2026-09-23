using BuildingBlocks.Messaging.Idempotency;
using BuildingBlocks.Persistence.Idempotency;
using BuildingBlocks.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Persistence;

public static class PersistenceExtensions
{
    /// <summary>
    /// Registers the Outbox writer, the dedup store and the background publisher for
    /// a service's own DbContext.
    /// </summary>
    public static IServiceCollection AddOutboxAndIdempotency<TDbContext>(
        this IServiceCollection services, IConfiguration configuration)
        where TDbContext : DbContext
    {
        services.Configure<OutboxProcessorOptions>(
            configuration.GetSection(OutboxProcessorOptions.SectionName));

        services.AddScoped<IOutboxWriter, OutboxWriter<TDbContext>>();
        services.AddScoped<IProcessedMessageStore, EfProcessedMessageStore<TDbContext>>();
        services.AddHostedService<OutboxProcessor<TDbContext>>();

        return services;
    }

    /// <summary>
    /// Maps the outbox and dedup tables. Call from each service's OnModelCreating so
    /// the tables land in that service's own database.
    /// </summary>
    public static ModelBuilder ApplyOutboxAndIdempotency(this ModelBuilder builder)
    {
        builder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(x => x.Id);

            // The processor scans for unpublished rows on every tick. Without this the
            // scan is a sequential read over every event the service has ever emitted.
            e.HasIndex(x => new { x.ProcessedAt, x.OccurredAt })
                .HasDatabaseName("ix_outbox_unprocessed");
        });

        builder.Entity<ProcessedMessage>(e =>
        {
            e.ToTable("processed_messages");
            e.HasKey(x => new { x.EventId, x.ConsumerName });
            e.HasIndex(x => x.ProcessedAt).HasDatabaseName("ix_processed_at");
        });

        return builder;
    }
}
