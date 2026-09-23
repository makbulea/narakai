namespace CodeIntelligence.Agent.Orchestration;

/// <summary>Configuration for <see cref="AgentOrchestrator"/> and <see cref="ToolExecutor"/>. Bind from "Agent:Orchestration".</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent:Orchestration";

    /// <summary>
    /// Hard ceiling on LLM round-trips per chat turn. Never an unbounded loop — see
    /// agent/AGENT_IMPLEMENTATION.md §7.
    /// </summary>
    public int MaxIterations { get; init; } = 8;

    /// <summary>Per-tool-call execution timeout, enforced by <see cref="ToolExecutor"/>.</summary>
    public TimeSpan ToolTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
