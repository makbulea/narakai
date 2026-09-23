using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Persistence;

namespace CodeIntelligence.Rag.IntegrationTests;

/// <summary>
/// Exercises ChunkRepository against a real PostgreSQL + pgvector database (see PostgresFixture)
/// instead of mocks, covering the SQL that no unit test can: vector distance operators,
/// ts_rank full-text search, metadata filtering, and the (repository_id, file_path, start_line,
/// end_line) upsert-in-place constraint. Tagged so it can be excluded in environments without
/// Docker: `dotnet test --filter Category!=Integration`.
///
/// Each test uses its own freshly created repository row so tests can share one container/
/// database (started once per test class) without cleaning up the `chunks` table between them —
/// every query here is scoped by repository_id.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ChunkRepositoryIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const int VectorDimension = 1024;

    private async Task<Guid> CreateRepositoryAsync([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var store = new RepositoryStore(fixture.DataSource);
        var repository = await store.GetOrCreateAsync(
            name: $"{testName}-{Guid.NewGuid()}",
            rootPath: "/tmp/repo",
            gitCommit: null);
        return repository.Id;
    }

    private static float[] MakeEmbedding(int hotIndex, float value = 1f)
    {
        var vector = new float[VectorDimension];
        vector[hotIndex] = value;
        return vector;
    }

    private static Chunk MakeChunk(
        Guid repositoryId,
        string filePath,
        string content,
        int startLine = 1,
        int endLine = 1,
        string language = "csharp",
        string? service = null,
        string? ns = null,
        string? className = null,
        string? methodName = null,
        SymbolKind symbolKind = SymbolKind.Method,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        RepositoryId = repositoryId,
        FilePath = filePath,
        StartLine = startLine,
        EndLine = endLine,
        Content = content,
        Language = language,
        Service = service,
        Namespace = ns,
        ClassName = className,
        MethodName = methodName,
        SymbolKind = symbolKind,
        ContentHash = CodeIntelligence.Rag.Hashing.ContentHasher.Sha256Hex(content),
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SearchByVectorAsync_ReturnsTopKOrderedByCosineSimilarity()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var near = MakeChunk(repositoryId, "near.cs", "content near");
        var mid = MakeChunk(repositoryId, "mid.cs", "content mid");
        var far = MakeChunk(repositoryId, "far.cs", "content far");

        await repository.UpsertManyAsync(
        [
            new ChunkWriteRecord(near, MakeEmbedding(0, 1.0f)),
            new ChunkWriteRecord(mid, MakeEmbedding(1, 1.0f)),
            new ChunkWriteRecord(far, MakeEmbedding(2, 1.0f))
        ]);

        // Query vector points mostly at dimension 0 (matches `near`), with a small component on
        // dimension 1 (partial overlap with `mid`), and nothing on dimension 2 (`far`).
        var query = MakeEmbedding(0, 0.9f);
        query[1] = 0.1f;

        var results = await repository.SearchByVectorAsync(repositoryId, query, topK: 3, filters: null, DistanceMetric.Cosine);

        Assert.Equal(3, results.Count);
        Assert.Equal(near.Id, results[0].Chunk.Id);
        Assert.Equal(mid.Id, results[1].Chunk.Id);
        Assert.Equal(far.Id, results[2].Chunk.Id);
        Assert.True(results[0].Score > results[1].Score);
        Assert.True(results[1].Score > results[2].Score);
    }

    [Fact]
    public async Task SearchByTextAsync_FullTextSearch_MatchesExpectedRows()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var orderChunk = MakeChunk(repositoryId, "OrderService.cs", "public class OrderService handles order creation and cancellation");
        var inventoryChunk = MakeChunk(repositoryId, "InventoryService.cs", "public class InventoryService tracks warehouse stock levels");

        await repository.UpsertManyAsync(
        [
            new ChunkWriteRecord(orderChunk, MakeEmbedding(0)),
            new ChunkWriteRecord(inventoryChunk, MakeEmbedding(1))
        ]);

        var results = await repository.SearchByTextAsync(repositoryId, "order cancellation", topK: 10, filters: null);

        var match = Assert.Single(results);
        Assert.Equal(orderChunk.Id, match.Chunk.Id);
        Assert.True(match.Score > 0);
    }

    [Fact]
    public async Task SearchByTextAsync_NoMatchingRows_ReturnsEmpty()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var chunk = MakeChunk(repositoryId, "Foo.cs", "completely unrelated content about widgets");
        await repository.UpsertManyAsync([new ChunkWriteRecord(chunk, MakeEmbedding(0))]);

        var results = await repository.SearchByTextAsync(repositoryId, "nonexistent gibberish query term", topK: 10, filters: null);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchByVectorAsync_FiltersRestrictResultsByLanguageServiceNamespaceClassNameAndSymbolKind()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var target = MakeChunk(
            repositoryId, "Target.cs", "target content", language: "csharp", service: "OrderService",
            ns: "MyApp.Orders", className: "OrderHandler", methodName: "Handle", symbolKind: SymbolKind.Method);

        var wrongLanguage = MakeChunk(repositoryId, "wrong.md", "other content", language: "markdown", service: "OrderService", ns: "MyApp.Orders", className: "OrderHandler");
        var wrongService = MakeChunk(repositoryId, "wrong2.cs", "other content", language: "csharp", service: "InventoryService", ns: "MyApp.Orders", className: "OrderHandler");
        var wrongClass = MakeChunk(repositoryId, "wrong3.cs", "other content", language: "csharp", service: "OrderService", ns: "MyApp.Orders", className: "SomethingElse");
        var wrongKind = MakeChunk(repositoryId, "wrong4.cs", "other content", language: "csharp", service: "OrderService", ns: "MyApp.Orders", className: "OrderHandler", symbolKind: SymbolKind.Field);

        await repository.UpsertManyAsync(
        [
            new ChunkWriteRecord(target, MakeEmbedding(3)),
            new ChunkWriteRecord(wrongLanguage, MakeEmbedding(4)),
            new ChunkWriteRecord(wrongService, MakeEmbedding(5)),
            new ChunkWriteRecord(wrongClass, MakeEmbedding(6)),
            new ChunkWriteRecord(wrongKind, MakeEmbedding(7))
        ]);

        var filters = new SearchFilters
        {
            Language = "csharp",
            Service = "OrderService",
            Namespace = "MyApp.Orders",
            ClassName = "OrderHandler",
            SymbolKind = SymbolKind.Method
        };

        var results = await repository.SearchByVectorAsync(repositoryId, MakeEmbedding(3), topK: 10, filters, DistanceMetric.Cosine);

        var match = Assert.Single(results);
        Assert.Equal(target.Id, match.Chunk.Id);
    }

    [Fact]
    public async Task SearchByVectorAsync_FilePathFilter_RestrictsToExactFile()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var a = MakeChunk(repositoryId, "a.cs", "content a");
        var b = MakeChunk(repositoryId, "b.cs", "content b");
        await repository.UpsertManyAsync(
        [
            new ChunkWriteRecord(a, MakeEmbedding(8)),
            new ChunkWriteRecord(b, MakeEmbedding(9))
        ]);

        var results = await repository.SearchByVectorAsync(
            repositoryId, MakeEmbedding(8), topK: 10, new SearchFilters { FilePath = "a.cs" }, DistanceMetric.Cosine);

        var match = Assert.Single(results);
        Assert.Equal(a.Id, match.Chunk.Id);
    }

    [Fact]
    public async Task DeleteManyAsync_RemovesSpecifiedChunks()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var keep = MakeChunk(repositoryId, "keep.cs", "keep me");
        var remove = MakeChunk(repositoryId, "remove.cs", "remove me");
        await repository.UpsertManyAsync(
        [
            new ChunkWriteRecord(keep, MakeEmbedding(10)),
            new ChunkWriteRecord(remove, MakeEmbedding(11))
        ]);

        await repository.DeleteManyAsync([remove.Id]);

        Assert.Equal(1, await repository.CountForRepositoryAsync(repositoryId));
        var remaining = await repository.GetChunksByFileAsync(repositoryId, "keep.cs");
        Assert.Single(remaining);
        var removed = await repository.GetChunksByFileAsync(repositoryId, "remove.cs");
        Assert.Empty(removed);
    }

    [Fact]
    public async Task DeleteByFileAsync_RemovesAllChunksForThatFileOnly()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var fileAChunk1 = MakeChunk(repositoryId, "a.cs", "chunk 1", startLine: 1, endLine: 1);
        var fileAChunk2 = MakeChunk(repositoryId, "a.cs", "chunk 2", startLine: 2, endLine: 2);
        var fileBChunk = MakeChunk(repositoryId, "b.cs", "other file");

        await repository.UpsertManyAsync(
        [
            new ChunkWriteRecord(fileAChunk1, MakeEmbedding(12)),
            new ChunkWriteRecord(fileAChunk2, MakeEmbedding(13)),
            new ChunkWriteRecord(fileBChunk, MakeEmbedding(14))
        ]);

        await repository.DeleteByFileAsync(repositoryId, "a.cs");

        Assert.Empty(await repository.GetChunksByFileAsync(repositoryId, "a.cs"));
        Assert.Single(await repository.GetChunksByFileAsync(repositoryId, "b.cs"));
    }

    [Fact]
    public async Task DeleteAllForRepositoryAsync_RemovesEveryChunkAndReturnsCount()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        await repository.UpsertManyAsync(
        [
            new ChunkWriteRecord(MakeChunk(repositoryId, "a.cs", "a"), MakeEmbedding(15)),
            new ChunkWriteRecord(MakeChunk(repositoryId, "b.cs", "b"), MakeEmbedding(16)),
            new ChunkWriteRecord(MakeChunk(repositoryId, "c.cs", "c"), MakeEmbedding(17))
        ]);

        var deletedCount = await repository.DeleteAllForRepositoryAsync(repositoryId);

        Assert.Equal(3, deletedCount);
        Assert.Equal(0, await repository.CountForRepositoryAsync(repositoryId));
    }

    [Fact]
    public async Task UpsertManyAsync_ConflictOnRepositoryFileAndLineRange_UpdatesInPlaceRatherThanDuplicating()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var original = MakeChunk(repositoryId, "same.cs", "version 1", startLine: 10, endLine: 20, id: Guid.NewGuid());
        await repository.UpsertManyAsync([new ChunkWriteRecord(original, MakeEmbedding(18))]);

        // Same repository/file/line range, but a brand-new chunk id — simulating the indexing
        // pipeline writing a "new" chunk whose position happens to coincide with an existing row.
        var conflicting = MakeChunk(repositoryId, "same.cs", "version 2", startLine: 10, endLine: 20, id: Guid.NewGuid());
        await repository.UpsertManyAsync([new ChunkWriteRecord(conflicting, MakeEmbedding(19))]);

        var rows = await repository.GetChunksByFileAsync(repositoryId, "same.cs");
        var row = Assert.Single(rows);

        Assert.Equal(original.Id, row.Id); // the ON CONFLICT target is the (repo,file,line) unique constraint, not id — id is left untouched
        Assert.Equal("version 2", row.Content);
        Assert.Equal(1, await repository.CountForRepositoryAsync(repositoryId));
    }

    [Fact]
    public async Task UpsertManyAsync_NullEmbedding_LeavesExistingEmbeddingUntouched()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        var chunk = MakeChunk(repositoryId, "kept.cs", "content", id: Guid.NewGuid());
        await repository.UpsertManyAsync([new ChunkWriteRecord(chunk, MakeEmbedding(20))]);

        // Re-upsert the same chunk id with a null embedding (as IndexingPipeline does for
        // unchanged content) and confirm the vector search still finds it via the original vector.
        var reWritten = MakeChunk(repositoryId, "kept.cs", "content", id: chunk.Id);
        await repository.UpsertManyAsync([new ChunkWriteRecord(reWritten, Embedding: null)]);

        var results = await repository.SearchByVectorAsync(repositoryId, MakeEmbedding(20), topK: 1, filters: null, DistanceMetric.Cosine);

        var match = Assert.Single(results);
        Assert.Equal(chunk.Id, match.Chunk.Id);
        Assert.True(match.Score > 0.99); // still near-identical to the original embedding, proving it wasn't nulled out
    }

    [Fact]
    public async Task ListFilePathsAsync_ReturnsDistinctFilesForRepository()
    {
        var repositoryId = await CreateRepositoryAsync();
        var repository = new ChunkRepository(fixture.DataSource);

        await repository.UpsertManyAsync(
        [
            new ChunkWriteRecord(MakeChunk(repositoryId, "a.cs", "1", startLine: 1, endLine: 1), MakeEmbedding(21)),
            new ChunkWriteRecord(MakeChunk(repositoryId, "a.cs", "2", startLine: 2, endLine: 2), MakeEmbedding(22)),
            new ChunkWriteRecord(MakeChunk(repositoryId, "b.cs", "3"), MakeEmbedding(23))
        ]);

        var filePaths = await repository.ListFilePathsAsync(repositoryId);

        Assert.Equal(2, filePaths.Count);
        Assert.Contains("a.cs", filePaths);
        Assert.Contains("b.cs", filePaths);
    }
}
