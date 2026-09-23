using CodeIntelligence.Rag.Models;

namespace CodeIntelligence.Rag.Persistence;

/// <summary>A chunk paired with the embedding to persist for it (null when reusing an unchanged chunk's existing embedding — see IndexingPipeline).</summary>
public sealed record ChunkWriteRecord(Chunk Chunk, float[]? Embedding);

public interface IChunkRepository
{
    /// <summary>All chunks currently stored for one file, used by the indexing pipeline to diff against freshly parsed chunks.</summary>
    Task<IReadOnlyList<Chunk>> GetChunksByFileAsync(Guid repositoryId, string filePath, CancellationToken cancellationToken = default);

    /// <summary>Inserts new chunks / updates existing ones (by id) in a single transaction. A null embedding leaves the stored embedding untouched.</summary>
    Task UpsertManyAsync(IReadOnlyList<ChunkWriteRecord> records, CancellationToken cancellationToken = default);

    Task DeleteManyAsync(IReadOnlyList<Guid> chunkIds, CancellationToken cancellationToken = default);

    /// <summary>Removes every chunk for a file, used when the file itself was deleted from the repository.</summary>
    Task DeleteByFileAsync(Guid repositoryId, string filePath, CancellationToken cancellationToken = default);

    Task<int> DeleteAllForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    Task<int> CountForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    /// <summary>Distinct file paths currently indexed for a repository, used to detect files removed from disk.</summary>
    Task<IReadOnlyList<string>> ListFilePathsAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(Chunk Chunk, double Score)>> SearchByVectorAsync(
        Guid repositoryId, float[] queryEmbedding, int topK, SearchFilters? filters, DistanceMetric metric,
        CancellationToken cancellationToken = default);

    /// <summary>PostgreSQL full-text search (ts_rank over the generated content_tsv column).</summary>
    Task<IReadOnlyList<(Chunk Chunk, double Score)>> SearchByTextAsync(
        Guid repositoryId, string queryText, int topK, SearchFilters? filters,
        CancellationToken cancellationToken = default);
}
