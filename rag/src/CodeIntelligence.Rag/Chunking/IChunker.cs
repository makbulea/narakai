using CodeIntelligence.Rag.Scanning;

namespace CodeIntelligence.Rag.Chunking;

/// <summary>
/// Splits one source file into meaningful, self-contained chunks. Implementations are
/// language-aware where practical (see <see cref="CSharp.CSharpChunker"/>) and fall back
/// to a size-based split (see <see cref="Generic.GenericTextChunker"/>) otherwise.
/// </summary>
public interface IChunker
{
    bool CanHandle(string extension);

    IReadOnlyList<ParsedChunk> Chunk(ScannedFile file, string sourceText, ChunkingOptions options);
}
