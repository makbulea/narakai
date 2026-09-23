namespace CodeIntelligence.Agent.RagIntegration;

// Local shapes for what this service consumes from CodeIntelligence.Rag.Api — mirror
// rag/src/CodeIntelligence.Rag.Api/Contracts/Contracts.cs's SearchResultResponse /
// RepositoryResponse. Deliberately not shared types, same reasoning backend's own
// ServiceClients.cs uses: the agent needs a citation and some text, not RAG's full
// internal model, so a field added upstream cannot break deserialization here.

public sealed record RagSearchResult(
    Guid ChunkId,
    string FilePath,
    int StartLine,
    int EndLine,
    string Content,
    double Score,
    RagChunkMetadata Metadata);

public sealed record RagChunkMetadata(
    string Language,
    string? Service,
    string? Namespace,
    string? ClassName,
    string? MethodName,
    RagSymbolKind SymbolKind);

/// <summary>
/// Mirrors rag/src/CodeIntelligence.Rag/Models/SymbolKind.cs exactly (same member order).
/// RAG's ChunkMetadata.SymbolKind is a real enum with no JsonStringEnumConverter
/// configured on the API host, so it serializes as a plain number over the wire — a
/// string-typed field here would throw deserializing it. This is not a shared type
/// reference (see RagDtos.cs's opening comment); it must be kept in sync by hand if
/// RAG's enum ever changes.
/// </summary>
public enum RagSymbolKind
{
    File,
    Namespace,
    Class,
    Interface,
    Record,
    Struct,
    Enum,
    Method,
    Constructor,
    Property,
    Field,
    Event,
    Other
}

public sealed record RagRepository(Guid Id, string Name, string RootPath, string? GitCommit, string Status);
