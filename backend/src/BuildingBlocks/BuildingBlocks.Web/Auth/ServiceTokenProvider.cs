using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Web.Auth;

/// <summary>
/// Supplies a token representing the *service itself*, for calls that have no user
/// behind them.
///
/// Needed because Kafka consumers have no incoming request to borrow a token from:
/// NotificationService handling OrderConfirmed still has to ask CustomerService for an
/// email address, and that endpoint requires authentication.
///
/// This mints its own token using the shared symmetric key, which is a development
/// shortcut. A real deployment would use the OAuth client-credentials flow against the
/// identity provider; the seam is here, and nothing that consumes the token changes.
/// </summary>
public sealed class ServiceTokenProvider(IOptions<JwtOptions> options, string serviceName)
{
    private readonly JwtOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cached;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        // Refresh a minute early so a token cannot expire mid-flight.
        if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-1))
            return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cached is not null && DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-1))
                return _cached;

            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.ExpiryMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, $"service:{serviceName}"),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.Role, Roles.Service)
            };

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                expires: expiresAt.UtcDateTime,
                signingCredentials: credentials);

            _cached = new JwtSecurityTokenHandler().WriteToken(token);
            _expiresAt = expiresAt;

            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
