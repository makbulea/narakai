using System.Text.Json.Serialization;

namespace CodeIntelligence.Rag.Embeddings.Voyage;

internal sealed class VoyageEmbeddingRequest
{
    [JsonPropertyName("input")]
    public required IReadOnlyList<string> Input { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("input_type")]
    public required string InputType { get; init; }
}

internal sealed class VoyageEmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<VoyageEmbeddingData> Data { get; init; } = [];

    [JsonPropertyName("model")]
    public string? Model { get; init; }
}

internal sealed class VoyageEmbeddingData
{
    [JsonPropertyName("embedding")]
    public List<float> Embedding { get; init; } = [];

    [JsonPropertyName("index")]
    public int Index { get; init; }
}
