namespace CodeIntelligence.Rag.Models;

/// <summary>
/// pgvector distance operators. Cosine is the default — see docs/vector-search.md
/// for why. All three are exposed because pgvector supports all of them and the
/// right choice depends on how the embedding model was trained.
/// </summary>
public enum DistanceMetric
{
    /// <summary>pgvector "&lt;=&gt;" operator. similarity = 1 - distance.</summary>
    Cosine,

    /// <summary>pgvector "&lt;#&gt;" operator (negative inner product).</summary>
    InnerProduct,

    /// <summary>pgvector "&lt;-&gt;" operator (L2 / Euclidean distance).</summary>
    Euclidean
}
