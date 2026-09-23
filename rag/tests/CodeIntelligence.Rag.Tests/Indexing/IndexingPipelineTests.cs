using CodeIntelligence.Rag.Chunking;
using CodeIntelligence.Rag.Embeddings;
using CodeIntelligence.Rag.Indexing;
using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Persistence;
using CodeIntelligence.Rag.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CodeIntelligence.Rag.Tests.Indexing;

public sealed class IndexingPipelineTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("rag-indexing-tests-");
    private readonly Mock<IEmbeddingProvider> _embeddingProvider = new();
    private readonly Mock<IRepositoryStore> _repositoryStore = new();
    private readonly FakeChunkRepository _chunkRepository = new();
    private readonly Repository _repository;

    public IndexingPipelineTests()
    {
        _repository = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            RootPath = _root.FullName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _repositoryStore
            .Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_repository);

        _embeddingProvider
            .Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), EmbeddingInputType.Document, It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<string> texts, EmbeddingInputType _, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<float[]>>(texts.Select(t => new float[] { t.Length }).ToList()));
    }

    public void Dispose() => _root.Delete(recursive: true);

    private IndexingPipeline CreatePipeline(IRepositoryScanner scanner) =>
        new(
            scanner,
            new ChunkerFactory(),
            _embeddingProvider.Object,
            _chunkRepository,
            _repositoryStore.Object,
            Options.Create(new ChunkingOptions()),
            NullLogger<IndexingPipeline>.Instance);

    private static Mock<IRepositoryScanner> MakeScanner(params ScannedFile[] files)
    {
        var scanner = new Mock<IRepositoryScanner>();
        scanner
            .Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => ToAsyncEnumerable(files));
        return scanner;
    }

    private static async IAsyncEnumerable<ScannedFile> ToAsyncEnumerable(IEnumerable<ScannedFile> files)
    {
        await Task.Yield();
        foreach (var file in files)
        {
            yield return file;
        }
    }

    private ScannedFile WriteFile(string relativePath, string content)
    {
        var absolutePath = Path.Combine(_root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, content);
        var info = new FileInfo(absolutePath);
        return new ScannedFile(absolutePath, relativePath, info.Extension, info.Length, info.LastWriteTimeUtc);
    }

    [Fact]
    public async Task IndexAsync_NewFile_EmbedsChunkAndMarksCompleted()
    {
        var file = WriteFile("notes.md", "Hello World");
        var scanner = MakeScanner(file);
        var pipeline = CreatePipeline(scanner.Object);

        var result = await pipeline.IndexAsync(_root.FullName);

        Assert.Equal(IndexingStatus.Completed, result.Status);
        Assert.Equal(1, result.FilesDiscovered);
        Assert.Equal(1, result.ChunksNew);
        Assert.Equal(0, result.ChunksUnchanged);
        Assert.Equal(1, result.EmbeddingsGenerated);

        var chunk = Assert.Single(_chunkRepository.Chunks);
        Assert.NotNull(_chunkRepository.LastWrittenEmbeddings[chunk.Id]);

        _repositoryStore.Verify(s => s.UpdateStatusAsync(_repository.Id, IndexingStatus.Processing, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryStore.Verify(s => s.UpdateStatusAsync(_repository.Id, IndexingStatus.Completed, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IndexAsync_UnchangedContentOnRerun_DoesNotReEmbed()
    {
        var file = WriteFile("notes.md", "Hello World");
        var scanner = MakeScanner(file);
        var pipeline = CreatePipeline(scanner.Object);

        var first = await pipeline.IndexAsync(_root.FullName);
        Assert.Equal(1, first.ChunksNew);
        Assert.Equal(1, first.EmbeddingsGenerated);

        var embedCallsAfterFirstRun = _embeddingProvider.Invocations.Count(i => i.Method.Name == nameof(IEmbeddingProvider.EmbedAsync));

        var second = await pipeline.IndexAsync(_root.FullName);

        Assert.Equal(0, second.ChunksNew);
        Assert.Equal(1, second.ChunksUnchanged);
        Assert.Equal(0, second.EmbeddingsGenerated);

        var embedCallsAfterSecondRun = _embeddingProvider.Invocations.Count(i => i.Method.Name == nameof(IEmbeddingProvider.EmbedAsync));
        Assert.Equal(embedCallsAfterFirstRun, embedCallsAfterSecondRun);

        // Still exactly one chunk stored (no duplicate created for the unchanged content).
        Assert.Single(_chunkRepository.Chunks);
    }

    [Fact]
    public async Task IndexAsync_ChangedContentOnRerun_ReEmbedsAndDeletesStaleChunk()
    {
        var file = WriteFile("notes.md", "Hello World");
        var scanner = MakeScanner(file);
        var pipeline = CreatePipeline(scanner.Object);

        var first = await pipeline.IndexAsync(_root.FullName);
        var originalChunkId = Assert.Single(_chunkRepository.Chunks).Id;

        File.WriteAllText(file.AbsolutePath, "Goodbye World, this content is different");

        var second = await pipeline.IndexAsync(_root.FullName);

        Assert.Equal(1, second.ChunksNew);
        Assert.Equal(0, second.ChunksUnchanged);
        Assert.Equal(1, second.EmbeddingsGenerated);
        Assert.Equal(1, second.ChunksDeleted);

        var remaining = Assert.Single(_chunkRepository.Chunks);
        Assert.NotEqual(originalChunkId, remaining.Id);
        Assert.Contains("Goodbye World", remaining.Content);
        Assert.Contains(_chunkRepository.DeleteManyCalls, ids => ids.Contains(originalChunkId));
    }

    [Fact]
    public async Task IndexAsync_FileRemovedFromScan_DeletesItsChunks()
    {
        var fileA = WriteFile("a.md", "Content A");
        var fileB = WriteFile("b.md", "Content B");

        // First run indexes both files.
        var firstScanner = MakeScanner(fileA, fileB);
        await CreatePipeline(firstScanner.Object).IndexAsync(_root.FullName);
        Assert.Equal(2, _chunkRepository.Chunks.Count);

        // Second run's scan only finds fileA — fileB was deleted from the repository.
        var secondScanner = MakeScanner(fileA);
        var result = await CreatePipeline(secondScanner.Object).IndexAsync(_root.FullName);

        Assert.Equal(1, result.ChunksDeleted);
        Assert.Single(_chunkRepository.Chunks);
        Assert.Equal("a.md", _chunkRepository.Chunks[0].FilePath);
        Assert.Contains(_chunkRepository.DeleteByFileCalls, c => c.FilePath == "b.md");
    }

    [Fact]
    public async Task IndexAsync_ExceptionMidPipeline_MarksRepositoryFailedAndRethrows()
    {
        var file = WriteFile("notes.md", "Hello World");
        var scanner = MakeScanner(file);

        _embeddingProvider
            .Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), EmbeddingInputType.Document, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding provider exploded"));

        var pipeline = CreatePipeline(scanner.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.IndexAsync(_root.FullName));

        _repositoryStore.Verify(s => s.UpdateStatusAsync(_repository.Id, IndexingStatus.Failed, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryStore.Verify(s => s.UpdateStatusAsync(_repository.Id, IndexingStatus.Completed, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IndexAsync_ServiceIsInferredFromServicesConventionInPath()
    {
        var file = WriteFile(Path.Combine("src", "Services", "OrderService", "Handler.md"), "handles orders");
        var scanner = MakeScanner(file);
        var pipeline = CreatePipeline(scanner.Object);

        await pipeline.IndexAsync(_root.FullName);

        var chunk = Assert.Single(_chunkRepository.Chunks);
        Assert.Equal("OrderService", chunk.Service);
    }

    [Fact]
    public async Task IndexAsync_FileOutsideServicesConvention_HasNullService()
    {
        var file = WriteFile("docs/readme.md", "some docs");
        var scanner = MakeScanner(file);
        var pipeline = CreatePipeline(scanner.Object);

        await pipeline.IndexAsync(_root.FullName);

        var chunk = Assert.Single(_chunkRepository.Chunks);
        Assert.Null(chunk.Service);
    }
}
