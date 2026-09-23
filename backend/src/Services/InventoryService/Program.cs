using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web;
using ECommerce.Inventory.Application;
using ECommerce.Inventory.Consumers;
using ECommerce.Inventory.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("InventoryService", typeof(ReserveStockRequestValidator).Assembly);

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("InventoryDb"),
        npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null))
        // Postgres convention. Without it EF maps properties to PascalCase columns,
        // which then need quoting in every hand-written query — and the Outbox
        // processor uses raw SQL. One convention everywhere is simpler.
        .UseSnakeCaseNamingConvention());

builder.Services.AddOutboxAndIdempotency<InventoryDbContext>(builder.Configuration);

// Redis here is a distributed lock, not a cache. Inventory levels are the one thing in
// this system that must never be served stale, so nothing on this path is cached.
builder.Services.AddRedisCaching(builder.Configuration);

builder.Services.AddScoped<InventoryService>();

// Compensation path: release stock when an order is cancelled asynchronously.
builder.Services.AddHostedService<OrderCancelledConsumer>();

builder.Services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>("inventory-db");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.MigrateAsync();
}

app.UseServiceDefaults();
app.Run();

public partial class Program;
