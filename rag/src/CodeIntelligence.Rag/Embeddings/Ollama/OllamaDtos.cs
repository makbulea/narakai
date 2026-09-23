using System.Text.Json.Serialization;

namespace CodeIntelligence.Rag.Embeddings.Ollama;

/// <summary>Wire format for Ollama's batch-capable POST /api/embed endpoint (input accepts a single string or an array).</summary>
internal sealed class OllamaEmbedRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input")]
    public required IReadOnlyList<string> Input { get; init; }
}

internal sealed class OllamaEmbedResponse
{
    [JsonPropertyName("embeddings")]
    public List<List<float>> Embeddings { get; init; } = [];
}
