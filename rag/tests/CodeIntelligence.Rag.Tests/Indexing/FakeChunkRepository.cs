using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Persistence;

namespace CodeIntelligence.Rag.Tests.Indexing;

/// <summary>
/// Minimal in-memory stand-in for <see cref="IChunkRepository"/>, used instead of a Moq mock
/// for IndexingPipeline tests because those tests need state (upserted chunks) to persist
/// across two separate IndexAsync calls to exercise incremental-indexing behavior — something
/// a stateless mock setup would make far more awkward than this small fake.
/// </summary>
public sealed class FakeChunkRepository : IChunkRepository
{
    public List<Chunk> Chunks { get; } = [];
    public List<IReadOnlyList<Guid>> DeleteManyCalls { get; } = [];
    public List<(Guid RepositoryId, string FilePath)> DeleteByFileCalls { get; } = [];
    public int UpsertManyCallCount { get; private set; }

    /// <summary>Embedding written alongside each chunk id in the most recent UpsertManyAsync that touched it (null = "kept existing").</summary>
    public Dictionary<Guid, float[]?> LastWrittenEmbeddings { get; } = [];

    public Task<IReadOnlyList<Chunk>> GetChunksByFileAsync(Guid repositoryId, string filePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Chunk>>(
            Chunks.Where(c => c.RepositoryId == repositoryId && c.FilePath == filePath).ToList());

    public Task UpsertManyAsync(IReadOnlyList<ChunkWriteRecord> records, CancellationToken cancellationToken = default)
    {
        UpsertManyCallCount++;
        foreach (var record in records)
        {
            Chunks.RemoveAll(c => c.Id == record.Chunk.Id);
            Chunks.Add(record.Chunk);
            LastWrittenEmbeddings[record.Chunk.Id] = record.Embedding;
        }

        return Task.CompletedTask;
    }

    public Task DeleteManyAsync(IReadOnlyList<Guid> chunkIds, CancellationToken cancellationToken = default)
    {
        DeleteManyCalls.Add(chunkIds);
        Chunks.RemoveAll(c => chunkIds.Contains(c.Id));
        return Task.CompletedTask;
    }

    public Task DeleteByFileAsync(Guid repositoryId, string filePath, CancellationToken cancellationToken = default)
    {
        DeleteByFileCalls.Add((repositoryId, filePath));
        Chunks.RemoveAll(c => c.RepositoryId == repositoryId && c.FilePath == filePath);
        return Task.CompletedTask;
    }

    public Task<int> DeleteAllForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Chunks.RemoveAll(c => c.RepositoryId == repositoryId));

    public Task<int> CountForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Chunks.Count(c => c.RepositoryId == repositoryId));

    public Task<IReadOnlyList<string>> ListFilePathsAsync(Guid repositoryId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            Chunks.Where(c => c.RepositoryId == repositoryId).Select(c => c.FilePath).Distinct().ToList());

    public Task<IReadOnlyList<(Chunk Chunk, double Score)>> SearchByVectorAsync(
        Guid repositoryId, float[] queryEmbedding, int topK, SearchFilters? filters, DistanceMetric metric,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(Chunk, double)>>([]);

    public Task<IReadOnlyList<(Chunk Chunk, double Score)>> SearchByTextAsync(
        Guid repositoryId, string queryText, int topK, SearchFilters? filters,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(Chunk, double)>>([]);
}
