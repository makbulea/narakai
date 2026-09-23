namespace CodeIntelligence.Rag.Chunking;

/// <summary>Configuration for code-aware chunking. Bind from "Rag:Chunking".</summary>
public sealed class ChunkingOptions
{
    public const string SectionName = "Rag:Chunking";

    /// <summary>
    /// Soft maximum size (in characters) for a single chunk. Character count, not a token
    /// budget, is used deliberately: it is embedding-provider-agnostic and cheap to check
    /// while walking syntax nodes. A member/file under this size is never split.
    /// </summary>
    public int MaxChunkSizeChars { get; init; } = 4000;

    /// <summary>
    /// When a member/file must be split because it exceeds <see cref="MaxChunkSizeChars"/>,
    /// this many trailing lines of the previous slice are repeated at the start of the next
    /// one, so a symbol split mid-body still reads with some continuity.
    /// </summary>
    public int OverlapLines { get; init; } = 5;
}
