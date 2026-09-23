namespace CodeIntelligence.Rag.HybridSearch;

/// <summary>Configuration for <see cref="HybridSearchService"/>. Bind from "Rag:HybridSearch".</summary>
public sealed class HybridSearchOptions
{
    public const string SectionName = "Rag:HybridSearch";

    /// <summary>Weight applied to the (min-max normalized) semantic similarity score.</summary>
    public double SemanticWeight { get; init; } = 0.7;

    /// <summary>Weight applied to the (min-max normalized) lexical (ts_rank) score.</summary>
    public double LexicalWeight { get; init; } = 0.3;

    /// <summary>
    /// Each side (semantic, lexical) retrieves topK * this many candidates before the two
    /// ranked lists are merged, so a chunk that ranks highly on one axis but outside the
    /// final topK on the other still gets a fair combined score.
    /// </summary>
    public int CandidateMultiplier { get; init; } = 4;
}
