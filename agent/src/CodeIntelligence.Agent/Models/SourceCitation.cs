namespace CodeIntelligence.Agent.Models;

/// <summary>
/// One grounded citation backing a claim in the final answer. Always built from an
/// actual tool result (see <see cref="Tools.ToolResult.Sources"/>) — never invented by
/// the LLM directly (top-level claude.md §16, Hallucination Control).
/// </summary>
public sealed record SourceCitation(string FilePath, int StartLine, int EndLine);
