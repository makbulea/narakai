using CodeIntelligence.Rag.Models;

namespace CodeIntelligence.Rag.Retrieval;

/// <summary>
/// Pure semantic (vector) retrieval: embed the query, run pgvector similarity search, apply
/// metadata filters, return the top K. See docs/vector-search.md.
/// </summary>
public interface ISemanticSearch
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default);
}
