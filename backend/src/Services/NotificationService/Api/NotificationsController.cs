using BuildingBlocks.Core.Paging;
using BuildingBlocks.Web.Auth;
using ECommerce.Notifications.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Notifications.Api;

/// <summary>
/// Read-only. Notifications are created by Kafka consumers, never by an API call —
/// exposing a "send notification" endpoint would let anyone with a token mail customers.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    /// <summary>Delivery history, filterable by customer, status and channel.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.ViewAnyCustomer)]
    [ProducesResponseType<PagedResult<NotificationResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationResponse>>> List(
        [FromQuery] NotificationQuery query, CancellationToken ct) =>
        Ok(await notifications.ListAsync(query, ct));
}
