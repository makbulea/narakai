using CodeIntelligence.Rag.Chunking.CSharp;
using CodeIntelligence.Rag.Chunking.Generic;

namespace CodeIntelligence.Rag.Chunking;

public sealed class ChunkerFactory : IChunkerFactory
{
    private readonly IReadOnlyList<IChunker> _chunkers;
    private readonly IChunker _fallback = new GenericTextChunker();

    public ChunkerFactory(IEnumerable<IChunker>? chunkers = null)
    {
        _chunkers = chunkers?.ToList() ?? [new CSharpChunker(), new GenericTextChunker()];
    }

    public IChunker GetChunker(string extension) =>
        _chunkers.FirstOrDefault(c => c.CanHandle(extension)) ?? _fallback;
}
