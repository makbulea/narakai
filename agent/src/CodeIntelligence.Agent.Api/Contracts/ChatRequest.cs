namespace CodeIntelligence.Agent.Api.Contracts;

/// <summary>Matches agent/AGENT_IMPLEMENTATION.md §17's request shape.</summary>
public sealed record ChatRequest(Guid RepositoryId, Guid? ConversationId, string Message);
