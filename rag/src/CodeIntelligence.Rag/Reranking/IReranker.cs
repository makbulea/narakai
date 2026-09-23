using CodeIntelligence.Rag.Models;

namespace CodeIntelligence.Rag.Reranking;

/// <summary>
/// Reorders a larger candidate set down to the final, most-relevant few. Retrieval (vector or
/// hybrid) is tuned for recall over a wide, cheap-to-score candidate pool; a reranker applies
/// a more precise — but more expensive per item — relevance signal to just that pool, which
/// only pays off because the pool is small. See docs/reranking.md.
///
/// The pipeline is: retrieve top 20-50 candidates → rerank → keep the top 5-10. This interface
/// is provider-agnostic so <see cref="SimpleLexicalReranker"/> (deterministic, no external
/// call) can later be swapped for a hosted cross-encoder/reranking API without touching callers.
/// </summary>
public interface IReranker
{
    Task<IReadOnlyList<SearchResult>> RerankAsync(
        string query, IReadOnlyList<SearchResult> candidates, int topK, CancellationToken cancellationToken = default);
}
