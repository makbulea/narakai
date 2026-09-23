using CodeIntelligence.Rag.Embeddings;
using CodeIntelligence.Rag.HybridSearch;
using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CodeIntelligence.Rag.Tests.HybridSearch;

public sealed class HybridSearchServiceTests
{
    private static Chunk MakeChunk(string name) => new()
    {
        Id = Guid.NewGuid(),
        RepositoryId = Guid.NewGuid(),
        FilePath = $"{name}.cs",
        StartLine = 1,
        EndLine = 5,
        Content = $"content for {name}",
        Language = "csharp",
        SymbolKind = Models.SymbolKind.Method,
        ContentHash = "hash",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static HybridSearchService CreateService(
        Mock<IChunkRepository> chunkRepository,
        Mock<IEmbeddingProvider> embeddingProvider,
        HybridSearchOptions options)
    {
        return new HybridSearchService(
            chunkRepository.Object,
            embeddingProvider.Object,
            Options.Create(options),
            NullLogger<HybridSearchService>.Instance);
    }

    private static SearchQuery MakeQuery(Guid repositoryId, int topK = 10) => new()
    {
        RepositoryId = repositoryId,
        Query = "find me something",
        TopK = topK
    };

    [Fact]
    public async Task SearchAsync_CombinesSemanticAndLexicalScoresUsingConfiguredWeights()
    {
        var repositoryId = Guid.NewGuid();
        var chunkA = MakeChunk("A"); // semantic only
        var chunkB = MakeChunk("B"); // both semantic and lexical
        var chunkC = MakeChunk("C"); // both, weak on both
        var chunkD = MakeChunk("D"); // lexical only

        var semantic = new List<(Chunk, double)> { (chunkA, 0.9), (chunkB, 0.5), (chunkC, 0.1) };
        var lexical = new List<(Chunk, double)> { (chunkB, 2.0), (chunkC, 1.0), (chunkD, 0.5) };

        var chunkRepository = new Mock<IChunkRepository>();
        chunkRepository
            .Setup(r => r.SearchByVectorAsync(repositoryId, It.IsAny<float[]>(), It.IsAny<int>(), null, DistanceMetric.Cosine, It.IsAny<CancellationToken>()))
            .ReturnsAsync(semantic);
        chunkRepository
            .Setup(r => r.SearchByTextAsync(repositoryId, It.IsAny<string>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lexical);

        var embeddingProvider = new Mock<IEmbeddingProvider>();
        embeddingProvider
            .Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new float[] { 1f, 0f }]);

        var options = new HybridSearchOptions { SemanticWeight = 0.7, LexicalWeight = 0.3, CandidateMultiplier = 4 };
        var service = CreateService(chunkRepository, embeddingProvider, options);

        var results = await service.SearchAsync(MakeQuery(repositoryId, topK: 4));

        // Expected combined scores (min-max normalized within each side, then weighted):
        // A: semantic norm 1.0 * 0.7 = 0.70
        // B: semantic norm 0.5 * 0.7 + lexical norm 1.0 * 0.3 = 0.35 + 0.30 = 0.65
        // C: semantic norm 0.0 * 0.7 + lexical norm (1/3) * 0.3 ≈ 0.10
        // D: lexical norm 0.0 * 0.3 = 0.00
        Assert.Equal(4, results.Count);
        Assert.Equal(chunkA.Id, results[0].ChunkId);
        Assert.Equal(chunkB.Id, results[1].ChunkId);
        Assert.Equal(chunkC.Id, results[2].ChunkId);
        Assert.Equal(chunkD.Id, results[3].ChunkId);

        Assert.Equal(0.70, results[0].Score, precision: 3);
        Assert.Equal(0.65, results[1].Score, precision: 3);
        Assert.Equal(0.10, results[2].Score, precision: 2);
        Assert.Equal(0.00, results[3].Score, precision: 3);
    }

    [Fact]
    public async Task SearchAsync_TruncatesToTopK()
    {
        var repositoryId = Guid.NewGuid();
        var chunkA = MakeChunk("A");
        var chunkB = MakeChunk("B");
        var chunkC = MakeChunk("C");

        var semantic = new List<(Chunk, double)> { (chunkA, 0.9), (chunkB, 0.5), (chunkC, 0.1) };

        var chunkRepository = new Mock<IChunkRepository>();
        chunkRepository
            .Setup(r => r.SearchByVectorAsync(repositoryId, It.IsAny<float[]>(), It.IsAny<int>(), null, DistanceMetric.Cosine, It.IsAny<CancellationToken>()))
            .ReturnsAsync(semantic);
        chunkRepository
            .Setup(r => r.SearchByTextAsync(repositoryId, It.IsAny<string>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var embeddingProvider = new Mock<IEmbeddingProvider>();
        embeddingProvider
            .Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new float[] { 1f, 0f }]);

        var service = CreateService(chunkRepository, embeddingProvider, new HybridSearchOptions());

        var results = await service.SearchAsync(MakeQuery(repositoryId, topK: 2));

        Assert.Equal(2, results.Count);
        Assert.Equal(chunkA.Id, results[0].ChunkId);
        Assert.Equal(chunkB.Id, results[1].ChunkId);
    }

    [Fact]
    public async Task SearchAsync_AllScoresEqualOnOneSide_NormalizesToOneInsteadOfDividingByZero()
    {
        var repositoryId = Guid.NewGuid();
        var chunkX = MakeChunk("X");
        var chunkY = MakeChunk("Y");

        // Both semantic candidates have the identical raw score: max - min = 0, which would
        // divide by zero without the guard in HybridSearchService.MinMaxNormalize.
        var semantic = new List<(Chunk, double)> { (chunkX, 5.0), (chunkY, 5.0) };
        var lexical = new List<(Chunk, double)> { (chunkX, 1.0), (chunkY, 3.0) };

        var chunkRepository = new Mock<IChunkRepository>();
        chunkRepository
            .Setup(r => r.SearchByVectorAsync(repositoryId, It.IsAny<float[]>(), It.IsAny<int>(), null, DistanceMetric.Cosine, It.IsAny<CancellationToken>()))
            .ReturnsAsync(semantic);
        chunkRepository
            .Setup(r => r.SearchByTextAsync(repositoryId, It.IsAny<string>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lexical);

        var embeddingProvider = new Mock<IEmbeddingProvider>();
        embeddingProvider
            .Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new float[] { 1f, 0f }]);

        var options = new HybridSearchOptions { SemanticWeight = 0.7, LexicalWeight = 0.3 };
        var service = CreateService(chunkRepository, embeddingProvider, options);

        var results = await service.SearchAsync(MakeQuery(repositoryId, topK: 2));

        // Both get semantic contribution 1.0 * 0.7 = 0.7 (no NaN/Infinity). Lexical is normalized
        // normally (X: 0.0, Y: 1.0), so Y should rank above X once the lexical weight is added.
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.False(double.IsNaN(r.Score) || double.IsInfinity(r.Score)));
        Assert.Equal(chunkY.Id, results[0].ChunkId);
        Assert.Equal(0.7 + 0.3, results[0].Score, precision: 3);
        Assert.Equal(0.7, results[1].Score, precision: 3);
    }

    [Fact]
    public async Task SearchAsync_UsesCandidatePoolSizeBasedOnMultiplier()
    {
        var repositoryId = Guid.NewGuid();
        int? capturedTopK = null;

        var chunkRepository = new Mock<IChunkRepository>();
        chunkRepository
            .Setup(r => r.SearchByVectorAsync(repositoryId, It.IsAny<float[]>(), It.IsAny<int>(), null, DistanceMetric.Cosine, It.IsAny<CancellationToken>()))
            .Callback<Guid, float[], int, SearchFilters?, DistanceMetric, CancellationToken>((_, _, topK, _, _, _) => capturedTopK = topK)
            .ReturnsAsync([]);
        chunkRepository
            .Setup(r => r.SearchByTextAsync(repositoryId, It.IsAny<string>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var embeddingProvider = new Mock<IEmbeddingProvider>();
        embeddingProvider
            .Setup(e => e.EmbedAsync(It.IsAny<IReadOnlyList<string>>(), EmbeddingInputType.Query, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new float[] { 1f }]);

        var options = new HybridSearchOptions { CandidateMultiplier = 5 };
        var service = CreateService(chunkRepository, embeddingProvider, options);

        await service.SearchAsync(MakeQuery(repositoryId, topK: 3));

        Assert.Equal(15, capturedTopK);
    }
}
