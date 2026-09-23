using CodeIntelligence.Rag.Models;

namespace CodeIntelligence.Rag.Chunking;

/// <summary>
/// A chunk as produced by an <see cref="IChunker"/>, before the indexing pipeline assigns
/// it an id, resolves its repository, hashes it, and stamps timestamps.
/// </summary>
public sealed record ParsedChunk(
    string FilePath,
    int StartLine,
    int EndLine,
    string Content,
    string Language,
    string? Namespace,
    string? ClassName,
    string? MethodName,
    SymbolKind SymbolKind);
