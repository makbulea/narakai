using BuildingBlocks.Persistence;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Auth;
using ECommerce.Customers.Api;
using ECommerce.Customers.Application;
using ECommerce.Customers.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("CustomerService", typeof(CreateCustomerRequestValidator).Assembly);

builder.Services.AddDbContext<CustomerDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("CustomerDb"),
        // Retry transient connection faults. Postgres restarting during a compose
        // bring-up is the common case; without this the service dies on first query.
        npgsql => npgsql.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null))
        // Postgres convention — see the other services for why.
        .UseSnakeCaseNamingConvention());

// Outbox publisher + Kafka dedup store, both against this service's own database.
builder.Services.AddOutboxAndIdempotency<CustomerDbContext>(builder.Configuration);

builder.Services.AddScoped<CustomerService>();

// CustomerService is the only service that mints tokens, and only outside Production.
if (!builder.Environment.IsProduction())
    builder.Services.AddSingleton<TokenIssuer>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CustomerDbContext>("customer-db");

var app = builder.Build();

// Migrate on startup. Acceptable here because each service owns its schema outright
// and the compose file starts a single replica. A multi-replica deployment would run
// migrations as a separate job so two instances cannot race the same DDL.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
    await db.Database.MigrateAsync();
}

app.UseServiceDefaults();

app.Run();

/// <summary>Exposed so integration tests can spin the host up with WebApplicationFactory.</summary>
public partial class Program;
