using CodeIntelligence.Agent.Models;

namespace CodeIntelligence.Agent.Api.Contracts;

public sealed record SourceCitationDto(string FilePath, int StartLine, int EndLine)
{
    public static SourceCitationDto FromModel(SourceCitation source) => new(source.FilePath, source.StartLine, source.EndLine);
}

public sealed record ToolCallDto(string ToolName, string ArgumentsSummary, bool IsError, double DurationMs)
{
    public static ToolCallDto FromModel(ToolCallLog log) => new(log.ToolName, log.ArgumentsSummary, log.IsError, log.Duration.TotalMilliseconds);
}

/// <summary>Matches agent/AGENT_IMPLEMENTATION.md §17's response shape.</summary>
public sealed record ChatResponse(
    Guid ConversationId, string Answer, IReadOnlyList<SourceCitationDto> Sources, IReadOnlyList<ToolCallDto> ToolCalls)
{
    public static ChatResponse FromAnswer(Guid conversationId, AgentAnswer answer) => new(
        conversationId,
        answer.Answer,
        answer.Sources.Select(SourceCitationDto.FromModel).ToList(),
        answer.ToolCalls.Select(ToolCallDto.FromModel).ToList());
}
