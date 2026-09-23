namespace CodeIntelligence.Rag.Models;

/// <summary>
/// A single code-aware unit of retrieval: one logical symbol (a method, a class,
/// a config file section, ...) with enough context to be understood in isolation
/// and enough location metadata to be mapped back to the exact source it came from.
/// </summary>
public sealed class Chunk
{
    public required Guid Id { get; init; }
    public required Guid RepositoryId { get; init; }

    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required string Content { get; init; }

    public required string Language { get; init; }

    /// <summary>
    /// Best-effort service/module name derived from the file path (e.g. the first
    /// directory under a conventional "Services" root, such as "InventoryService").
    /// Null when no such convention is detected. Enables filters like the one in
    /// the class-level docs: service = "InventoryService".
    /// </summary>
    public string? Service { get; init; }

    public string? Namespace { get; init; }
    public string? ClassName { get; init; }
    public string? MethodName { get; init; }
    public required SymbolKind SymbolKind { get; init; }

    /// <summary>SHA-256 of <see cref="Content"/>, used for incremental indexing.</summary>
    public required string ContentHash { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
