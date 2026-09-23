using BuildingBlocks.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Customers.Api;

public sealed record TokenRequest(string Email, string Role);
public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string Role);

/// <summary>
/// Development-only token endpoint.
///
/// There is no password check because there is no user store — this exists so the API
/// can be exercised end to end without deploying Keycloak. It is registered only when
/// the environment is not Production (see Program.cs), so it cannot ship by accident.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController(TokenIssuer issuer) : ControllerBase
{
    [HttpPost("token")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<TokenResponse> IssueToken([FromBody] TokenRequest request)
    {
        string[] allowed = [Roles.Admin, Roles.Customer, Roles.Support];

        if (!allowed.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails
            {
                Title = "Unknown role",
                Detail = $"Role must be one of: {string.Join(", ", allowed)}.",
                Status = StatusCodes.Status400BadRequest
            });

        var role = allowed.First(r => r.Equals(request.Role, StringComparison.OrdinalIgnoreCase));
        var (token, expiresAt) = issuer.Issue(request.Email, request.Email, role);

        return Ok(new TokenResponse(token, expiresAt, role));
    }
}
