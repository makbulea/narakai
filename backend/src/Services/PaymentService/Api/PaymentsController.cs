using BuildingBlocks.Web.Auth;
using ECommerce.Payments.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerce.Payments.Api;

[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public sealed class PaymentsController(PaymentService payments) : ControllerBase
{
    /// <summary>
    /// Charges a payment. Safe to retry with the same idempotency key.
    ///
    /// The key may come from the Idempotency-Key header or the body. The header wins,
    /// because it is the convention callers and proxies already understand, and it lets
    /// a duplicate be spotted before the body is parsed.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentResponse>> Process(
        [FromBody] ProcessPaymentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var effective = string.IsNullOrWhiteSpace(idempotencyKey)
            ? request
            : request with { IdempotencyKey = idempotencyKey };

        // 200, not 201: a retry returns the same resource, and answering 201 twice for
        // one payment would be a lie about what happened.
        return Ok(await payments.ProcessAsync(effective, ct));
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentResponse>> GetById(Guid id, CancellationToken ct) =>
        Ok(await payments.GetAsync(id, ct));

    [HttpGet("order/{orderId:guid}")]
    [Authorize]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentResponse>> GetByOrder(Guid orderId, CancellationToken ct) =>
        Ok(await payments.GetByOrderAsync(orderId, ct));

    /// <summary>Refunds a captured payment. Admin and Support only — never the customer.</summary>
    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = Policies.IssueRefunds)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PaymentResponse>> Refund(Guid id, CancellationToken ct) =>
        Ok(await payments.RefundAsync(id, ct));
}
