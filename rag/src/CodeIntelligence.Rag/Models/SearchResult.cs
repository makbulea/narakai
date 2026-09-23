namespace CodeIntelligence.Rag.Models;

/// <summary>
/// One retrieved chunk, ready to hand to the Agent or the API. Deliberately does
/// not expose the raw embedding vector — callers only need location + content + score.
/// </summary>
public sealed record SearchResult
{
    public required Guid ChunkId { get; init; }
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }

    /// <summary>Similarity in [0, 1] (or [-1, 1] for inner product) — higher is better. See docs/vector-search.md.</summary>
    public required double Score { get; init; }

    public required ChunkMetadata Metadata { get; init; }
}

/// <summary>Non-vector metadata about a chunk, returned alongside search results.</summary>
public sealed class ChunkMetadata
{
    public required string Language { get; init; }
    public string? Service { get; init; }
    public string? Namespace { get; init; }
    public string? ClassName { get; init; }
    public string? MethodName { get; init; }
    public required SymbolKind SymbolKind { get; init; }

    public static ChunkMetadata FromChunk(Chunk chunk) => new()
    {
        Language = chunk.Language,
        Service = chunk.Service,
        Namespace = chunk.Namespace,
        ClassName = chunk.ClassName,
        MethodName = chunk.MethodName,
        SymbolKind = chunk.SymbolKind
    };
}
