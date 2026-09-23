using BuildingBlocks.Persistence;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Resilience;
using ECommerce.Orders.Application;
using ECommerce.Orders.Consumers;
using ECommerce.Orders.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("OrderService", typeof(CreateOrderRequestValidator).Assembly);

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("OrderDb"),
        npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null))
        // Postgres convention. Without it EF maps properties to PascalCase columns,
        // which then need quoting in every hand-written query — and the Outbox
        // processor uses raw SQL. One convention everywhere is simpler.
        .UseSnakeCaseNamingConvention());

builder.Services.AddOutboxAndIdempotency<OrderDbContext>(builder.Configuration);

// Every outbound call gets timeout + retry-with-jitter + circuit breaker + correlation
// forwarding. See BuildingBlocks.Web.Resilience for why each layer is there.
builder.Services.AddHttpClient<CustomerServiceClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Customer"]!))
    .AddStandardResilience();

builder.Services.AddHttpClient<ProductServiceClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Product"]!))
    .AddStandardResilience();

builder.Services.AddHttpClient<InventoryServiceClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Inventory"]!))
    .AddStandardResilience();

builder.Services.AddHttpClient<PaymentServiceClient>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Payment"]!))
    .AddStandardResilience();

builder.Services.AddScoped<OrderService>();

// Resolves orders whose payment outcome arrived after the HTTP call gave up.
builder.Services.AddHostedService<PaymentEventsConsumer>();

builder.Services.AddHealthChecks().AddDbContextCheck<OrderDbContext>("order-db");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.MigrateAsync();
}

app.UseServiceDefaults();
app.Run();

public partial class Program;
