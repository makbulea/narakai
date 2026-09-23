namespace CodeIntelligence.Rag.Models;

/// <summary>Input to <see cref="Retrieval.ISemanticSearch"/> and hybrid search.</summary>
public sealed class SearchQuery
{
    public required Guid RepositoryId { get; init; }
    public required string Query { get; init; }
    public int TopK { get; init; } = 5;
    public SearchFilters? Filters { get; init; }
    public DistanceMetric Metric { get; init; } = DistanceMetric.Cosine;
}
