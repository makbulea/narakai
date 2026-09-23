using BuildingBlocks.Core.Correlation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace BuildingBlocks.Web.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Structured logging to stdout as JSON in containers, human-readable locally.
    ///
    /// Every line carries the service name and the correlation id, which is what makes
    /// "show me everything that happened to order 4711" a single query across six
    /// services instead of six separate searches.
    /// </summary>
    public static void ConfigureSerilog(this IHostBuilder host, string serviceName)
    {
        host.UseSerilog((context, services, config) =>
        {
            config
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithProperty("service", serviceName)
                .Enrich.With<CorrelationIdEnricher>()
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:HH:mm:ss} {Level:u3}] [{service}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}");
        });
    }

    /// <summary>
    /// Traces and metrics over OTLP.
    ///
    /// ASP.NET Core and HttpClient instrumentation together give a distributed trace
    /// for free across synchronous hops: OrderService → InventoryService shows up as
    /// parent and child spans because W3C traceparent rides along on the outgoing call.
    ///
    /// Kafka hops are not automatically linked — the correlation id in the message
    /// envelope covers that gap.
    /// </summary>
    public static IServiceCollection AddObservability(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var otlpEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        // Health probes fire every few seconds and would drown the traces
                        // that matter.
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            });

        return services;
    }
}

/// <summary>Puts the ambient correlation id on every log event.</summary>
public sealed class CorrelationIdEnricher : Serilog.Core.ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, Serilog.Core.ILogEventPropertyFactory factory) =>
        logEvent.AddOrUpdateProperty(
            factory.CreateProperty("CorrelationId", CorrelationContext.CorrelationId));
}
