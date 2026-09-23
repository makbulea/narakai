using System.Diagnostics;
using CodeIntelligence.Rag.Embeddings;
using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Persistence;
using Microsoft.Extensions.Logging;

namespace CodeIntelligence.Rag.Retrieval;

public sealed class SemanticSearchService(
    IChunkRepository chunkRepository,
    IEmbeddingProvider embeddingProvider,
    ILogger<SemanticSearchService> logger) : ISemanticSearch
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var embeddings = await embeddingProvider
            .EmbedAsync([query.Query], EmbeddingInputType.Query, cancellationToken)
            .ConfigureAwait(false);
        var queryEmbedding = embeddings[0];

        var candidates = await chunkRepository
            .SearchByVectorAsync(query.RepositoryId, queryEmbedding, query.TopK, query.Filters, query.Metric, cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();
        logger.LogInformation(
            "Semantic search for repository {RepositoryId} returned {Count} candidates in {ElapsedMs} ms",
            query.RepositoryId, candidates.Count, stopwatch.ElapsedMilliseconds);

        return candidates
            .Select(c => new SearchResult
            {
                ChunkId = c.Chunk.Id,
                FilePath = c.Chunk.FilePath,
                StartLine = c.Chunk.StartLine,
                EndLine = c.Chunk.EndLine,
                Content = c.Chunk.Content,
                Score = c.Score,
                Metadata = ChunkMetadata.FromChunk(c.Chunk)
            })
            .ToList();
    }
}
