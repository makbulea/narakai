namespace CodeIntelligence.Rag.Reranking;

/// <summary>Configuration for <see cref="SimpleLexicalReranker"/>. Bind from "Rag:Reranking".</summary>
public sealed class RerankerOptions
{
    public const string SectionName = "Rag:Reranking";

    /// <summary>Weight given to the candidate's incoming retrieval score (already in [0, 1]-ish range).</summary>
    public double RetrievalScoreWeight { get; init; } = 0.6;

    /// <summary>Weight given to the fraction of query tokens found in the chunk content.</summary>
    public double TokenOverlapWeight { get; init; } = 0.3;

    /// <summary>Flat bonus added when the full query string appears verbatim (case-insensitive) in the content — rewards exact identifier matches.</summary>
    public double ExactMatchBonus { get; init; } = 0.1;
}
