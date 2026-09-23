using System.Text.Json;
using CodeIntelligence.Agent.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Agent.Orchestration;

/// <summary>
/// Runs one tool call safely: validates the LLM supplied every required argument, bounds
/// execution with a timeout, and turns any failure into a <see cref="ToolResult"/> the
/// LLM can see and reason about instead of crashing the agent loop
/// (agent/AGENT_IMPLEMENTATION.md §14).
/// </summary>
public sealed class ToolExecutor(IOptions<AgentOptions> options, ILogger<ToolExecutor> logger)
{
    private readonly AgentOptions _options = options.Value;

    public async Task<ToolResult> ExecuteAsync(
        ITool tool, IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken)
    {
        var missing = tool.Definition.Required.Where(name => !input.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            return ToolResult.Error($"Missing required argument(s): {string.Join(", ", missing)}.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.ToolTimeout);

        try
        {
            return await tool.ExecuteAsync(input, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Tool {ToolName} timed out after {Timeout}", tool.Definition.Name, _options.ToolTimeout);
            return ToolResult.Error($"Tool '{tool.Definition.Name}' timed out.");
        }
        catch (Exception ex)
        {
            // Deliberately generic message back to the LLM: internal exception text
            // (stack traces, connection strings) must never reach a prompt
            // (top-level claude.md §15/§17).
            logger.LogError(ex, "Tool {ToolName} failed", tool.Definition.Name);
            return ToolResult.Error($"Tool '{tool.Definition.Name}' failed: an internal error occurred.");
        }
    }
}
