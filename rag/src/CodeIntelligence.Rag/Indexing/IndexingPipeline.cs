using System.Diagnostics;
using CodeIntelligence.Rag.Chunking;
using CodeIntelligence.Rag.Embeddings;
using CodeIntelligence.Rag.Hashing;
using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Persistence;
using CodeIntelligence.Rag.Scanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.Indexing;

public sealed class IndexingPipeline(
    IRepositoryScanner scanner,
    IChunkerFactory chunkerFactory,
    IEmbeddingProvider embeddingProvider,
    IChunkRepository chunkRepository,
    IRepositoryStore repositoryStore,
    IOptions<ChunkingOptions> chunkingOptions,
    ILogger<IndexingPipeline> logger) : IIndexingPipeline
{
    private readonly ChunkingOptions _chunkingOptions = chunkingOptions.Value;

    public async Task<IndexingResult> IndexAsync(
        string repositoryPath, string? repositoryName = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var fullPath = Path.GetFullPath(repositoryPath);
        var name = repositoryName ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        var gitCommit = await GitInfo.TryGetCurrentCommitAsync(fullPath, cancellationToken).ConfigureAwait(false);

        var repository = await repositoryStore.GetOrCreateAsync(name, fullPath, gitCommit, cancellationToken).ConfigureAwait(false);
        await repositoryStore.UpdateStatusAsync(repository.Id, IndexingStatus.Processing, gitCommit, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Repository indexing started: {Name} ({RepositoryId}) at {Path}", name, repository.Id, fullPath);

        var filesDiscovered = 0;
        var chunksCreated = 0;
        var chunksUnchanged = 0;
        var chunksNew = 0;
        var chunksDeleted = 0;
        var embeddingsGenerated = 0;
        var seenFiles = new HashSet<string>();

        try
        {
            await foreach (var file in scanner.ScanAsync(fullPath, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                filesDiscovered++;
                seenFiles.Add(file.RelativePath);

                var (fileChunksCreated, fileChunksUnchanged, fileChunksNew, fileChunksDeleted, fileEmbeddings) =
                    await IndexFileAsync(repository, file, cancellationToken).ConfigureAwait(false);

                chunksCreated += fileChunksCreated;
                chunksUnchanged += fileChunksUnchanged;
                chunksNew += fileChunksNew;
                chunksDeleted += fileChunksDeleted;
                embeddingsGenerated += fileEmbeddings;
            }

            chunksDeleted += await DeleteChunksForRemovedFilesAsync(repository.Id, seenFiles, cancellationToken).ConfigureAwait(false);

            await repositoryStore.UpdateStatusAsync(repository.Id, IndexingStatus.Completed, gitCommit, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            logger.LogInformation(
                "Repository indexing completed: {Name} ({RepositoryId}) — {Files} files, {Total} chunks " +
                "({Unchanged} unchanged, {New} new, {Deleted} deleted), {Embeddings} embeddings generated, {ElapsedMs} ms",
                name, repository.Id, filesDiscovered, chunksCreated, chunksUnchanged, chunksNew, chunksDeleted,
                embeddingsGenerated, stopwatch.ElapsedMilliseconds);

            return new IndexingResult(
                repository.Id, filesDiscovered, chunksCreated, chunksUnchanged, chunksNew, chunksDeleted,
                embeddingsGenerated, stopwatch.Elapsed, IndexingStatus.Completed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await repositoryStore.UpdateStatusAsync(repository.Id, IndexingStatus.Failed, gitCommit, cancellationToken).ConfigureAwait(false);
            logger.LogError(ex, "Repository indexing failed: {Name} ({RepositoryId})", name, repository.Id);
            throw;
        }
    }

    private async Task<(int Created, int Unchanged, int New, int Deleted, int Embeddings)> IndexFileAsync(
        Repository repository, ScannedFile file, CancellationToken cancellationToken)
    {
        var sourceText = await File.ReadAllTextAsync(file.AbsolutePath, cancellationToken).ConfigureAwait(false);
        var chunker = chunkerFactory.GetChunker(file.Extension);
        var parsedChunks = chunker.Chunk(file, sourceText, _chunkingOptions);
        var service = InferService(file.RelativePath);
        var now = DateTimeOffset.UtcNow;

        var existingChunks = await chunkRepository
            .GetChunksByFileAsync(repository.Id, file.RelativePath, cancellationToken)
            .ConfigureAwait(false);
        // A file can legitimately contain two chunks with identical content (e.g. two empty
        // constructors); GroupBy + First keeps this a best-effort reuse rather than a strict
        // 1:1 match, which is an acceptable trade-off for avoiding needless re-embeddings.
        var existingByHash = existingChunks
            .GroupBy(c => c.ContentHash)
            .ToDictionary(g => g.Key, g => g.First());
        var matchedExistingIds = new HashSet<Guid>();

        var writeRecords = new List<ChunkWriteRecord>();
        var pendingChunks = new List<Chunk>();
        var pendingTexts = new List<string>();
        var unchanged = 0;

        foreach (var parsed in parsedChunks)
        {
            var hash = ContentHasher.Sha256Hex(parsed.Content);

            if (existingByHash.TryGetValue(hash, out var existing) && matchedExistingIds.Add(existing.Id))
            {
                writeRecords.Add(new ChunkWriteRecord(
                    ToChunk(repository.Id, parsed, service, hash, existing.Id, existing.CreatedAt, now),
                    Embedding: null)); // unchanged content: keep the embedding already stored
                unchanged++;
                continue;
            }

            var chunk = ToChunk(repository.Id, parsed, service, hash, Guid.NewGuid(), now, now);
            pendingChunks.Add(chunk);
            pendingTexts.Add(chunk.Content);
        }

        if (pendingTexts.Count > 0)
        {
            var embeddings = await embeddingProvider
                .EmbedAsync(pendingTexts, EmbeddingInputType.Document, cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < pendingChunks.Count; i++)
            {
                writeRecords.Add(new ChunkWriteRecord(pendingChunks[i], embeddings[i]));
            }
        }

        var staleIds = existingChunks.Select(c => c.Id).Where(id => !matchedExistingIds.Contains(id)).ToList();
        if (staleIds.Count > 0)
        {
            await chunkRepository.DeleteManyAsync(staleIds, cancellationToken).ConfigureAwait(false);
        }

        if (writeRecords.Count > 0)
        {
            await chunkRepository.UpsertManyAsync(writeRecords, cancellationToken).ConfigureAwait(false);
        }

        return (parsedChunks.Count, unchanged, pendingChunks.Count, staleIds.Count, pendingTexts.Count);
    }

    private async Task<int> DeleteChunksForRemovedFilesAsync(
        Guid repositoryId, HashSet<string> seenFiles, CancellationToken cancellationToken)
    {
        var indexedFiles = await chunkRepository.ListFilePathsAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        var removedFiles = indexedFiles.Where(f => !seenFiles.Contains(f)).ToList();
        var deleted = 0;

        foreach (var removedFile in removedFiles)
        {
            var count = (await chunkRepository.GetChunksByFileAsync(repositoryId, removedFile, cancellationToken).ConfigureAwait(false)).Count;
            await chunkRepository.DeleteByFileAsync(repositoryId, removedFile, cancellationToken).ConfigureAwait(false);
            deleted += count;
        }

        return deleted;
    }

    private static Chunk ToChunk(
        Guid repositoryId, ParsedChunk parsed, string? service, string hash, Guid id, DateTimeOffset createdAt, DateTimeOffset updatedAt) => new()
    {
        Id = id,
        RepositoryId = repositoryId,
        FilePath = parsed.FilePath,
        StartLine = parsed.StartLine,
        EndLine = parsed.EndLine,
        Content = parsed.Content,
        Language = parsed.Language,
        Service = service,
        Namespace = parsed.Namespace,
        ClassName = parsed.ClassName,
        MethodName = parsed.MethodName,
        SymbolKind = parsed.SymbolKind,
        ContentHash = hash,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
    };

    private static string? InferService(string relativeFilePath)
    {
        // Convention: src/Services/<ServiceName>/... (this project's own backend layout).
        // Files outside that convention simply get a null Service, which the "service" filter
        // then never matches. See docs/architecture.md.
        var segments = relativeFilePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].Equals("Services", StringComparison.OrdinalIgnoreCase))
            {
                return segments[i + 1];
            }
        }

        return null;
    }
}
