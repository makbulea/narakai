using CodeIntelligence.Agent.Models;

namespace CodeIntelligence.Agent.Tools;

/// <summary>
/// Outcome of one tool execution. <c>Content</c> is what goes back to the LLM as the
/// tool_result; <c>Sources</c> is the structured citation data the orchestrator collects
/// for the final answer — kept separate so citations never depend on parsing the LLM
/// -facing text back out again.
/// </summary>
public sealed record ToolResult
{
    public required string Content { get; init; }
    public bool IsError { get; init; }
    public IReadOnlyList<SourceCitation> Sources { get; init; } = [];

    public static ToolResult Ok(string content, IReadOnlyList<SourceCitation>? sources = null) =>
        new() { Content = content, Sources = sources ?? [] };

    public static ToolResult Error(string message) => new() { Content = message, IsError = true };
}
