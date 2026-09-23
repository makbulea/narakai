using BuildingBlocks.Messaging.Kafka;
using BuildingBlocks.Web.Auth;
using BuildingBlocks.Web.Errors;
using BuildingBlocks.Web.Middleware;
using BuildingBlocks.Web.Observability;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Serilog;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web;

/// <summary>
/// The pipeline every service shares.
///
/// Centralised so that "how does this system handle errors / auth / correlation" has
/// one answer rather than six drifting copies. Anything genuinely service-specific
/// stays in that service's Program.cs.
/// </summary>
public static class ServiceDefaults
{
    public static WebApplicationBuilder AddServiceDefaults(
        this WebApplicationBuilder builder, string serviceName, Assembly validatorAssembly)
    {
        builder.Host.ConfigureSerilog(serviceName);

        builder.Services.AddObservability(builder.Configuration, serviceName);

        builder.Services.Configure<KafkaOptions>(
            builder.Configuration.GetSection(KafkaOptions.SectionName));
        builder.Services.AddSingleton<KafkaEventPublisher>();
        builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<KafkaEventPublisher>());

        builder.Services.AddJwtAuth(builder.Configuration);

        // Needed by ServiceAuthenticationHandler to see the incoming request's token.
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddSingleton(sp => new ServiceTokenProvider(
            sp.GetRequiredService<IOptions<JwtOptions>>(), serviceName));

        builder.Services.AddTransient<ServiceAuthenticationHandler>();

        // Validators are discovered from the service's own assembly. FluentValidation
        // runs them via the endpoint filter below rather than MVC's model state, so the
        // failure shape is the same ProblemDetails as every other error.
        builder.Services.AddValidatorsFromAssembly(validatorAssembly);

        builder.Services.AddControllers(o => o.Filters.Add<ValidationFilter>());

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();

        builder.Services.AddHealthChecks();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo { Title = serviceName, Version = "v1" });

            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the token from POST /api/auth/token on CustomerService."
            });

            // OpenAPI.NET v2 (shipped with Swashbuckle 10) replaced the old
            // Reference-object pattern with typed reference classes.
            // Swashbuckle 10 takes a factory here so the requirement can reference a
            // scheme registered on the document being generated.
            o.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                { new OpenApiSecuritySchemeReference("Bearer"), new List<string>() }
            });
        });

        return builder;
    }

    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        // Order matters. Correlation first so everything after it — including the
        // exception handler — logs under the right id.
        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseExceptionHandler();

        app.UseSerilogRequestLogging(o =>
        {
            o.GetLevel = (ctx, _, ex) =>
                ex is not null ? Serilog.Events.LogEventLevel.Error
                : ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
                : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose
                : Serilog.Events.LogEventLevel.Information;
        });

        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        // Liveness: is the process up. Used by docker-compose to decide "started".
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        // Readiness: are dependencies reachable. Used by dependent services to wait.
        app.MapHealthChecks("/health/ready");

        return app;
    }
}
