using BuildingBlocks.Web.Auth;
using CodeIntelligence.Agent.Api.Contracts;
using CodeIntelligence.Agent.Orchestration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeIntelligence.Agent.Api.Controllers;

[ApiController]
[Route("api/agent")]
[Produces("application/json")]
public sealed class AgentController(AgentOrchestrator orchestrator) : ControllerBase
{
    /// <summary>
    /// Answers one question about an indexed repository by researching it with tools
    /// (search_code, read_file) rather than answering from the model's own memory — see
    /// agent/docs/AGENT.md for the request/response shape and an example trace.
    ///
    /// Reuses the ViewAnyCustomer policy (Admin/Support/Service) — the closest existing
    /// "internal, read-only" policy — rather than a new one: this endpoint has no write
    /// capability of its own, matching agent/AGENT_IMPLEMENTATION.md §17's "read-only
    /// first version" rule.
    /// </summary>
    [HttpPost("chat")]
    [Authorize(Policy = Policies.ViewAnyCustomer)]
    [ProducesResponseType<ChatResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ChatResponse>> Chat([FromBody] ChatRequest request, CancellationToken ct)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid();
        var answer = await orchestrator.AskAsync(conversationId, request.RepositoryId, request.Message, ct);

        return Ok(ChatResponse.FromAnswer(conversationId, answer));
    }
}
