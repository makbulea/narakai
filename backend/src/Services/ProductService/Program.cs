using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web;
using ECommerce.Products.Application;
using ECommerce.Products.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("ProductService", typeof(CreateProductRequestValidator).Assembly);

builder.Services.AddDbContext<ProductDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("ProductDb"),
        npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null))
        // Postgres convention. Without it EF maps properties to PascalCase columns,
        // which then need quoting in every hand-written query — and the Outbox
        // processor uses raw SQL. One convention everywhere is simpler.
        .UseSnakeCaseNamingConvention());

builder.Services.AddOutboxAndIdempotency<ProductDbContext>(builder.Configuration);

// The catalogue is the only read-heavy, rarely-changing dataset in the system, so it
// is the only place a cache pays for itself. See ProductService for the reasoning.
builder.Services.AddRedisCaching(builder.Configuration);

builder.Services.AddScoped<ProductService>();

builder.Services.AddHealthChecks().AddDbContextCheck<ProductDbContext>("product-db");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ProductDbContext>();
    await db.Database.MigrateAsync();
}

app.UseServiceDefaults();
app.Run();

public partial class Program;
