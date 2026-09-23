using BuildingBlocks.Core.Paging;
using BuildingBlocks.Web.Auth;
using ECommerce.Orders.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Orders.Api;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public sealed class OrdersController(OrderService orders) : ControllerBase
{
    /// <summary>
    /// Places an order: validates, reserves stock, takes payment, confirms.
    ///
    /// Returns 201 even when the order ends up Cancelled — the order resource was
    /// created and is retrievable, and its Status field carries the outcome. Returning
    /// 4xx would leave the client unable to reference an order that exists.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.PlaceOrders)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        var order = await orders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetById(Guid id, CancellationToken ct) =>
        Ok(await orders.GetAsync(id, ct));

    /// <summary>Cancels a non-terminal order and releases any held stock.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.PlaceOrders)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrderResponse>> Cancel(
        Guid id, [FromBody] CancelOrderRequest request, CancellationToken ct) =>
        Ok(await orders.CancelAsync(id, request.Reason, ct));

    [HttpGet]
    [Authorize(Policy = Policies.ViewAnyCustomer)]
    [ProducesResponseType<PagedResult<OrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List(
        [FromQuery] OrderQuery query, CancellationToken ct) =>
        Ok(await orders.ListAsync(query, ct));

    /// <summary>A single customer's orders, newest first.</summary>
    [HttpGet("customer/{customerId:guid}")]
    [Authorize]
    [ProducesResponseType<PagedResult<OrderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderResponse>>> History(
        Guid customerId, [FromQuery] PageRequest page, CancellationToken ct) =>
        Ok(await orders.GetHistoryAsync(customerId, page, ct));
}
