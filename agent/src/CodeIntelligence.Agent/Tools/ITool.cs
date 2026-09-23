using System.Text.Json;
using CodeIntelligence.Agent.Llm;

namespace CodeIntelligence.Agent.Tools;

/// <summary>
/// One focused, independently invocable capability the agent can call — never a
/// "do everything" tool (top-level claude.md §13 / agent/AGENT_IMPLEMENTATION.md §4).
/// Each tool owns its own schema and its own input validation; the orchestrator only
/// knows how to look one up by name via <see cref="ToolRegistry"/> and call it through
/// <see cref="Orchestration.ToolExecutor"/>.
/// </summary>
public interface ITool
{
    LlmToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken = default);
}
