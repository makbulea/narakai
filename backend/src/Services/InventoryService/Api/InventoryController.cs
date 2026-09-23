using BuildingBlocks.Web.Auth;
using ECommerce.Inventory.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Inventory.Api;

[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public sealed class InventoryController(InventoryService inventory) : ControllerBase
{
    [HttpGet("{productId:guid}")]
    [Authorize]
    [ProducesResponseType<StockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockResponse>> GetStock(Guid productId, CancellationToken ct) =>
        Ok(await inventory.GetStockAsync(productId, ct));

    /// <summary>
    /// Reserves stock for an order. Called synchronously by OrderService.
    ///
    /// Returns 200 with Success=false rather than a 4xx when stock is short: this is an
    /// expected business outcome, not a client error, and OrderService needs the
    /// available quantity in the body to explain the failure to the customer.
    /// </summary>
    [HttpPost("reserve")]
    [Authorize]
    [ProducesResponseType<ReserveStockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReserveStockResponse>> Reserve(
        [FromBody] ReserveStockRequest request, CancellationToken ct) =>
        Ok(await inventory.ReserveAsync(request, ct));

    /// <summary>Compensating action. Safe to call more than once.</summary>
    [HttpPost("release")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Release([FromBody] ReleaseStockRequest request, CancellationToken ct)
    {
        await inventory.ReleaseAsync(request.OrderId, request.Reason, ct);
        return NoContent();
    }

    /// <summary>Order shipped: reserved units permanently leave stock.</summary>
    [HttpPost("commit/{orderId:guid}")]
    [Authorize(Policy = Policies.ManageCatalog)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Commit(Guid orderId, CancellationToken ct)
    {
        await inventory.CommitAsync(orderId, ct);
        return NoContent();
    }

    /// <summary>Goods received.</summary>
    [HttpPost("{productId:guid}/increase")]
    [Authorize(Policy = Policies.ManageCatalog)]
    [ProducesResponseType<StockResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StockResponse>> Increase(
        Guid productId, [FromBody] AdjustStockRequest request, CancellationToken ct) =>
        Ok(await inventory.IncreaseAsync(productId, request.Quantity, ct));

    /// <summary>Shrinkage or manual correction. Cannot consume reserved stock.</summary>
    [HttpPost("{productId:guid}/decrease")]
    [Authorize(Policy = Policies.ManageCatalog)]
    [ProducesResponseType<StockResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<StockResponse>> Decrease(
        Guid productId, [FromBody] AdjustStockRequest request, CancellationToken ct) =>
        Ok(await inventory.DecreaseAsync(productId, request.Quantity, ct));
}
