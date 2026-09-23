using System.Diagnostics;
using CodeIntelligence.Rag.Embeddings;
using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.HybridSearch;

public sealed class HybridSearchService(
    IChunkRepository chunkRepository,
    IEmbeddingProvider embeddingProvider,
    IOptions<HybridSearchOptions> options,
    ILogger<HybridSearchService> logger) : IHybridSearch
{
    private readonly HybridSearchOptions _options = options.Value;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var candidatePoolSize = Math.Max(query.TopK * _options.CandidateMultiplier, query.TopK);

        var embeddings = await embeddingProvider
            .EmbedAsync([query.Query], EmbeddingInputType.Query, cancellationToken)
            .ConfigureAwait(false);
        var queryEmbedding = embeddings[0];

        var semanticTask = chunkRepository.SearchByVectorAsync(
            query.RepositoryId, queryEmbedding, candidatePoolSize, query.Filters, query.Metric, cancellationToken);
        var lexicalTask = chunkRepository.SearchByTextAsync(
            query.RepositoryId, query.Query, candidatePoolSize, query.Filters, cancellationToken);
        await Task.WhenAll(semanticTask, lexicalTask).ConfigureAwait(false);

        var semantic = semanticTask.Result;
        var lexical = lexicalTask.Result;
        var ranked = Combine(semantic, lexical)
            .OrderByDescending(r => r.Score)
            .Take(query.TopK)
            .ToList();

        stopwatch.Stop();
        logger.LogInformation(
            "Hybrid search for repository {RepositoryId} merged {SemanticCount} semantic + {LexicalCount} lexical candidates into {Count} results in {ElapsedMs} ms",
            query.RepositoryId, semantic.Count, lexical.Count, ranked.Count, stopwatch.ElapsedMilliseconds);

        return ranked
            .Select(r => new SearchResult
            {
                ChunkId = r.Chunk.Id,
                FilePath = r.Chunk.FilePath,
                StartLine = r.Chunk.StartLine,
                EndLine = r.Chunk.EndLine,
                Content = r.Chunk.Content,
                Score = r.Score,
                Metadata = ChunkMetadata.FromChunk(r.Chunk)
            })
            .ToList();
    }

    private List<(Chunk Chunk, double Score)> Combine(
        IReadOnlyList<(Chunk Chunk, double Score)> semantic, IReadOnlyList<(Chunk Chunk, double Score)> lexical)
    {
        var semanticNorm = MinMaxNormalize(semantic);
        var lexicalNorm = MinMaxNormalize(lexical);

        var combined = new Dictionary<Guid, (Chunk Chunk, double Score)>();

        foreach (var (chunk, score) in semanticNorm)
        {
            combined[chunk.Id] = (chunk, score * _options.SemanticWeight);
        }

        foreach (var (chunk, score) in lexicalNorm)
        {
            var contribution = score * _options.LexicalWeight;
            combined[chunk.Id] = combined.TryGetValue(chunk.Id, out var existing)
                ? (chunk, existing.Score + contribution)
                : (chunk, contribution);
        }

        return combined.Values.ToList();
    }

    /// <summary>
    /// Rescales one side's raw scores to [0, 1] within the retrieved candidate set. Cosine
    /// similarity and ts_rank live on unrelated scales, so combining them with fixed weights
    /// only makes sense after each side is normalized relative to its own candidates.
    /// </summary>
    private static List<(Chunk Chunk, double Score)> MinMaxNormalize(IReadOnlyList<(Chunk Chunk, double Score)> results)
    {
        if (results.Count == 0)
        {
            return [];
        }

        var min = results.Min(r => r.Score);
        var max = results.Max(r => r.Score);

        if (max - min < 1e-9)
        {
            return results.Select(r => (r.Chunk, 1.0)).ToList();
        }

        return results.Select(r => (r.Chunk, (r.Score - min) / (max - min))).ToList();
    }
}
