using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Web.Auth;

/// <summary>
/// Attaches credentials to every outgoing service-to-service call.
///
/// Two cases, and the order matters:
///
///  1. **A user request is in flight** — forward that user's bearer token unchanged.
///     Downstream authorisation then sees the real caller, so a Customer cannot reach
///     an Admin-only endpoint by going through OrderService. It also keeps the audit
///     trail intact: CustomerService's logs show whose request caused the lookup.
///
///  2. **No request context** — a Kafka consumer or background worker. Fall back to a
///     token identifying the service itself.
///
/// Without this the calls go out unauthenticated and every downstream endpoint answers
/// 401, which surfaces as a confusing 502 at the edge.
/// </summary>
public sealed class ServiceAuthenticationHandler(
    IHttpContextAccessor httpContextAccessor,
    ServiceTokenProvider serviceTokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var incoming = httpContextAccessor.HttpContext?.Request.Headers.Authorization.FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(incoming) &&
                incoming.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation("Authorization", incoming);
            }
            else
            {
                var token = await serviceTokens.GetTokenAsync(cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
