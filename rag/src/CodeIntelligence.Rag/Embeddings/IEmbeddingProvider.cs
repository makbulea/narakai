namespace CodeIntelligence.Rag.Embeddings;

/// <summary>
/// Whether the text being embedded is something being indexed or the user's search query.
/// Some providers (Voyage included) produce better retrieval quality when told which side
/// of the asymmetric-retrieval pair a text is on.
/// </summary>
public enum EmbeddingInputType
{
    Document,
    Query
}

/// <summary>
/// Abstraction over an embedding model provider. Deliberately provider-agnostic — the rest
/// of the pipeline (chunking, persistence, retrieval) never references Voyage directly, so
/// swapping providers later only means writing a new implementation of this interface.
/// </summary>
public interface IEmbeddingProvider
{
    string ModelName { get; }

    /// <summary>Dimensionality of the vectors this provider returns. Must match the pgvector column.</summary>
    int Dimension { get; }

    /// <summary>
    /// Embeds a batch of texts, preserving input order in the result. Implementations must
    /// batch internally rather than requiring one call per text.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingInputType inputType,
        CancellationToken cancellationToken = default);
}
