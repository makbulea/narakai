using CodeIntelligence.Rag.Models;

namespace CodeIntelligence.Rag.HybridSearch;

/// <summary>
/// Combines semantic (vector) similarity with lexical (PostgreSQL full-text) matching, so an
/// exact identifier like "OrderCreatedEvent" ranks well even when its embedding similarity to
/// the query is unremarkable. See docs/hybrid-search.md.
/// </summary>
public interface IHybridSearch
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
