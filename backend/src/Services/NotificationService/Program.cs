using BuildingBlocks.Persistence;
using BuildingBlocks.Web;
using BuildingBlocks.Web.Resilience;
using ECommerce.Notifications.Application;
using ECommerce.Notifications.Consumers;
using ECommerce.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("NotificationService", typeof(NotificationService).Assembly);

builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("NotificationDb"),
        npgsql => npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null))
        // Postgres convention. Without it EF maps properties to PascalCase columns,
        // which then need quoting in every hand-written query — and the Outbox
        // processor uses raw SQL. One convention everywhere is simpler.
        .UseSnakeCaseNamingConvention());

builder.Services.AddOutboxAndIdempotency<NotificationDbContext>(builder.Configuration);

builder.Services.AddHttpClient<CustomerLookup>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Customer"]!))
    .AddStandardResilience();

builder.Services.AddHttpClient<OrderLookup>(c =>
        c.BaseAddress = new Uri(builder.Configuration["Services:Order"]!))
    .AddStandardResilience();

// Both channels are registered; NotificationService picks by Notification.Type.
builder.Services.AddSingleton<INotificationProvider, FakeEmailProvider>();
builder.Services.AddSingleton<INotificationProvider, FakeSmsProvider>();

builder.Services.AddScoped<NotificationService>();

builder.Services.AddHostedService<OrderNotificationConsumer>();
builder.Services.AddHostedService<PaymentNotificationConsumer>();
builder.Services.AddHostedService<NotificationRetryWorker>();

builder.Services.AddHealthChecks().AddDbContextCheck<NotificationDbContext>("notification-db");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
    await db.Database.MigrateAsync();
}

app.UseServiceDefaults();
app.Run();

public partial class Program;
