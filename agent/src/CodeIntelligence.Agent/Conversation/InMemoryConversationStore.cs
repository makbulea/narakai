using System.Collections.Concurrent;
using CodeIntelligence.Agent.Llm;

namespace CodeIntelligence.Agent.Conversation;

/// <summary>Process-lifetime conversation store. Must be registered as a singleton — see DI wiring.</summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<LlmMessage>> _conversations = new();

    public Task<IReadOnlyList<LlmMessage>> GetAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_conversations.GetValueOrDefault(conversationId, []));

    public Task SaveAsync(Guid conversationId, IReadOnlyList<LlmMessage> messages, CancellationToken cancellationToken = default)
    {
        _conversations[conversationId] = messages;
        return Task.CompletedTask;
    }
}
