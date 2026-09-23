using BuildingBlocks.Core.Paging;
using BuildingBlocks.Web.Auth;
using ECommerce.Customers.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Customers.Api;

[ApiController]
[Route("api/customers")]
[Produces("application/json")]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    /// <summary>Creates a customer and publishes CustomerCreated.</summary>
    [HttpPost]
    [Authorize(Policy = Policies.ViewAnyCustomer)]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var created = await customers.CreateAsync(request, ct);

        // 201 with a Location header, not 200 — the resource did not exist before.
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> GetById(Guid id, CancellationToken ct) =>
        Ok(await customers.GetAsync(id, ct));

    [HttpPut("{id:guid}")]
    [Authorize]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct) =>
        Ok(await customers.UpdateAsync(id, request, ct));

    /// <summary>Soft-deletes the customer. Admin only — this is not reversible via the API.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await customers.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>
    /// Paged list with filtering and sorting. Also serves search: pass ?search=term.
    /// A separate /search route would duplicate every filter for no benefit.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = Policies.ViewAnyCustomer)]
    [ProducesResponseType<PagedResult<CustomerResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CustomerResponse>>> List(
        [FromQuery] CustomerQuery query, CancellationToken ct) =>
        Ok(await customers.ListAsync(query, ct));
}
