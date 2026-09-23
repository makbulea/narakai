namespace CodeIntelligence.Agent.Models;

/// <summary>Final result of one <see cref="Orchestration.AgentOrchestrator.AskAsync"/> call.</summary>
public sealed record AgentAnswer(
    string Answer,
    IReadOnlyList<SourceCitation> Sources,
    IReadOnlyList<ToolCallLog> ToolCalls);
