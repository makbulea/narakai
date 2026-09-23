namespace CodeIntelligence.Rag.Embeddings;

/// <summary>Thrown when an embedding provider call fails permanently or after exhausting retries.</summary>
public sealed class EmbeddingProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);
