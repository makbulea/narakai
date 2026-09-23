namespace CodeIntelligence.Rag.Embeddings;

/// <summary>
/// Selects which <see cref="IEmbeddingProvider"/> implementation gets wired up. Bind from
/// "Rag:Embeddings". Kept separate from the per-provider options (<see cref="Voyage.VoyageOptions"/>,
/// <see cref="Ollama.OllamaOptions"/>) so adding a third provider later only means adding
/// another case here, not touching the existing ones.
/// </summary>
public sealed class EmbeddingProviderOptions
{
    public const string SectionName = "Rag:Embeddings";

    /// <summary>"Voyage" (default, hosted, needs an API key) or "Ollama" (local, no API key, no rate limits).</summary>
    public string Provider { get; init; } = "Voyage";
}
