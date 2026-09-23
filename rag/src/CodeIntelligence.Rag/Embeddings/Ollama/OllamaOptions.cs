namespace CodeIntelligence.Rag.Embeddings.Ollama;

/// <summary>
/// Configuration for <see cref="OllamaEmbeddingProvider"/>. Bind from "Rag:Embeddings:Ollama".
/// No API key: Ollama is a local server, not a hosted provider.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Rag:Embeddings:Ollama";

    public string Model { get; init; } = "nomic-embed-text";

    /// <summary>Dimensionality nomic-embed-text produces. Must match the pgvector column — see rag/docs/vector-search.md.</summary>
    public int Dimension { get; init; } = 768;

    /// <summary>
    /// Texts per request. Kept modest (unlike Voyage's 128): Ollama runs the model
    /// on local hardware, and a very large batch competes for the same CPU/GPU/RAM the
    /// rest of the machine is using, rather than hitting a remote rate limit.
    /// </summary>
    public int BatchSize { get; init; } = 16;

    public Uri BaseUrl { get; init; } = new("http://localhost:11434");

    /// <summary>
    /// Generous default: Ollama's first request after startup pays a one-time cost to
    /// load the model into memory, which can take much longer than a normal embedding call.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(120);
}
