namespace CodeIntelligence.Agent.RagIntegration;

/// <summary>
/// The agent's view of the RAG service (see rag/docs/agent-integration.md). Called over
/// HTTP — the same "typed client + resilience pipeline" shape backend services use to
/// call each other (see backend/src/Services/OrderService/Infrastructure/ServiceClients.cs)
/// — rather than an in-process library reference, so RAG stays an independently
/// deployable/versioned service.
/// </summary>
public interface IRagServiceClient
{
    Task<IReadOnlyList<RagSearchResult>> HybridSearchAsync(
        Guid repositoryId, string query, int topK, CancellationToken cancellationToken = default);

    /// <summary>Null means "no such repository" — a legitimate answer, not a transport failure.</summary>
    Task<RagRepository?> GetRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default);
}
