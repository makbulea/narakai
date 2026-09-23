using BuildingBlocks.Core.Paging;
using BuildingBlocks.Web.Auth;
using ECommerce.Products.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Products.Api;

[ApiController]
[Route("api/products")]
[Produces("application/json")]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Policies.ManageCatalog)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create(
        [FromBody] CreateProductRequest request, CancellationToken ct)
    {
        var created = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Read-through cached. Anonymous: the catalogue is public.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(Guid id, CancellationToken ct) =>
        Ok(await products.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageCatalog)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id, [FromBody] UpdateProductRequest request, CancellationToken ct) =>
        Ok(await products.UpdateAsync(id, request, ct));

    /// <summary>Moves a Draft product to Active, making it orderable.</summary>
    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.ManageCatalog)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await products.ActivateAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ManageCatalog)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await products.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<ProductResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List(
        [FromQuery] ProductQuery query, CancellationToken ct) =>
        Ok(await products.ListAsync(query, ct));

    /// <summary>
    /// Batch lookup consumed by OrderService when validating a basket. POST rather than
    /// GET because the id list would blow past URL length limits on a large order.
    /// </summary>
    [HttpPost("bulk")]
    [Authorize]
    [ProducesResponseType<IReadOnlyList<ProductResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> GetMany(
        [FromBody] BulkProductRequest request, CancellationToken ct) =>
        Ok(await products.GetManyAsync(request.ProductIds, ct));
}
