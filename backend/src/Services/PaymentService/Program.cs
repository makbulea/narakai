using BuildingBlocks.Caching;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web;
using ECommerce.Payments.Application;
using ECommerce.Payments.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("PaymentService", typeof(ProcessPaymentRequestValidator).Assembly);

builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("PaymentDb"),
        npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null))
        // Postgres convention. Without it EF maps properties to PascalCase columns,
        // which then need quoting in every hand-written query — and the Outbox
        // processor uses raw SQL. One convention everywhere is simpler.
        .UseSnakeCaseNamingConvention());

builder.Services.AddOutboxAndIdempotency<PaymentDbContext>(builder.Configuration);

// Redis here holds short-lived idempotency reservations, not cached data. The unique
// index on payments.idempotency_key is the real guarantee; Redis just makes the common
// duplicate cheap to reject.
builder.Services.AddRedisCaching(builder.Configuration);

builder.Services.Configure<PaymentProviderOptions>(
    builder.Configuration.GetSection(PaymentProviderOptions.SectionName));

builder.Services.AddSingleton<FakePaymentProvider>();
builder.Services.AddScoped<PaymentService>();

builder.Services.AddHealthChecks().AddDbContextCheck<PaymentDbContext>("payment-db");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
    await db.Database.MigrateAsync();
}

app.UseServiceDefaults();
app.Run();

public partial class Program;
