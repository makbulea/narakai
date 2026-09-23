namespace CodeIntelligence.Rag.Chunking;

/// <summary>Resolves the right <see cref="IChunker"/> for a file extension.</summary>
public interface IChunkerFactory
{
    IChunker GetChunker(string extension);
}
