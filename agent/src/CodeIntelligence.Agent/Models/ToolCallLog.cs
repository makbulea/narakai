namespace CodeIntelligence.Agent.Models;

/// <summary>
/// One tool invocation, recorded for observability (claude.md §15: log the operational
/// trace — tool, arguments, result summary, timing — never the model's private
/// reasoning) and returned to the API caller so a client can render "how the agent got
/// this answer".
/// </summary>
public sealed record ToolCallLog(
    string ToolName,
    string ArgumentsSummary,
    string ResultSummary,
    bool IsError,
    IReadOnlyList<SourceCitation> Sources,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
}
