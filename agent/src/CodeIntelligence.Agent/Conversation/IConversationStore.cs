using CodeIntelligence.Agent.Llm;

namespace CodeIntelligence.Agent.Conversation;

/// <summary>
/// Per-conversation message history. In-memory only for Phase 1 — see
/// agent/AGENT_IMPLEMENTATION.md §9: no long-term memory yet, but the same conversation
/// can be continued with follow-up questions within one process lifetime. Swappable
/// behind this interface if/when persistent memory is added in a later phase.
/// </summary>
public interface IConversationStore
{
    Task<IReadOnlyList<LlmMessage>> GetAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid conversationId, IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default);
}
