using System.Text;
using BuildingBlocks.Web.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Web.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
            throw new InvalidOperationException(
                "Jwt:SigningKey is not configured. Set it via environment variable — it must never be committed.");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),

                    // Default is five minutes of grace, which quietly extends every
                    // token's life. Thirty seconds covers clock drift and no more.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.ManageCatalog, p => p.RequireRole(Roles.Admin))
            .AddPolicy(Policies.ViewAnyCustomer, p => p.RequireRole(Roles.Admin, Roles.Support, Roles.Service))
            .AddPolicy(Policies.PlaceOrders, p => p.RequireRole(Roles.Admin, Roles.Customer, Roles.Support, Roles.Service))
            .AddPolicy(Policies.IssueRefunds, p => p.RequireRole(Roles.Admin, Roles.Support));

        return services;
    }
}
