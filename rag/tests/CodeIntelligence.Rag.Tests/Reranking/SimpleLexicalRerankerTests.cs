using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Reranking;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.Tests.Reranking;

public sealed class SimpleLexicalRerankerTests
{
    private static SearchResult MakeResult(string content, double score = 0.0) => new()
    {
        ChunkId = Guid.NewGuid(),
        FilePath = "Foo.cs",
        StartLine = 1,
        EndLine = 1,
        Content = content,
        Score = score,
        Metadata = new ChunkMetadata { Language = "csharp", SymbolKind = SymbolKind.Method }
    };

    private static SimpleLexicalReranker CreateReranker(RerankerOptions? options = null) =>
        new(Options.Create(options ?? new RerankerOptions()));

    [Fact]
    public async Task RerankAsync_TokenOverlap_HigherOverlapScoresHigher()
    {
        var reranker = CreateReranker(new RerankerOptions { RetrievalScoreWeight = 0, TokenOverlapWeight = 1, ExactMatchBonus = 0 });

        var fullOverlap = MakeResult("order service create cancel");
        var partialOverlap = MakeResult("order service only");
        var noOverlap = MakeResult("completely unrelated text");

        var results = await reranker.RerankAsync(
            "order service create cancel", [noOverlap, partialOverlap, fullOverlap], topK: 3);

        Assert.Equal(fullOverlap.ChunkId, results[0].ChunkId);
        Assert.Equal(1.0, results[0].Score, precision: 6);
        Assert.Equal(partialOverlap.ChunkId, results[1].ChunkId);
        Assert.Equal(noOverlap.ChunkId, results[2].ChunkId);
        Assert.Equal(0.0, results[2].Score, precision: 6);
    }

    [Fact]
    public async Task RerankAsync_ExactMatchBonus_AppliedWhenFullQueryAppearsVerbatim()
    {
        var options = new RerankerOptions { RetrievalScoreWeight = 0, TokenOverlapWeight = 0, ExactMatchBonus = 0.5 };
        var reranker = CreateReranker(options);

        var exact = MakeResult("this contains the exact phrase order created event somewhere");
        var notExact = MakeResult("this contains order and created and event but not together");

        var results = await reranker.RerankAsync("order created event", [notExact, exact], topK: 2);

        Assert.Equal(exact.ChunkId, results[0].ChunkId);
        Assert.Equal(0.5, results[0].Score, precision: 6);
        Assert.Equal(0.0, results[1].Score, precision: 6);
    }

    [Fact]
    public async Task RerankAsync_ExactMatchIsCaseInsensitive()
    {
        var options = new RerankerOptions { RetrievalScoreWeight = 0, TokenOverlapWeight = 0, ExactMatchBonus = 1.0 };
        var reranker = CreateReranker(options);

        var candidate = MakeResult("class ORDERSERVICE handles things");

        var results = await reranker.RerankAsync("orderservice", [candidate], topK: 1);

        Assert.Equal(1.0, results[0].Score, precision: 6);
    }

    [Fact]
    public async Task RerankAsync_TokenOverlapIsCaseInsensitive()
    {
        var options = new RerankerOptions { RetrievalScoreWeight = 0, TokenOverlapWeight = 1, ExactMatchBonus = 0 };
        var reranker = CreateReranker(options);

        var candidate = MakeResult("CREATE and CANCEL are both here");

        var results = await reranker.RerankAsync("create cancel", [candidate], topK: 1);

        Assert.Equal(1.0, results[0].Score, precision: 6);
    }

    [Fact]
    public async Task RerankAsync_BlendsRetrievalScoreTokenOverlapAndExactMatchByConfiguredWeights()
    {
        var options = new RerankerOptions { RetrievalScoreWeight = 0.6, TokenOverlapWeight = 0.3, ExactMatchBonus = 0.1 };
        var reranker = CreateReranker(options);

        // Query tokens: "order", "created". Content has both tokens plus the exact phrase.
        var candidate = MakeResult("the order created flow", score: 0.5);

        var results = await reranker.RerankAsync("order created", [candidate], topK: 1);

        // retrieval: 0.5 * 0.6 = 0.30; overlap: 2/2 * 0.3 = 0.30; exact match bonus: 0.1
        Assert.Equal(0.70, results[0].Score, precision: 6);
    }

    [Fact]
    public async Task RerankAsync_OrdersDescendingByComputedScoreAndTruncatesToTopK()
    {
        var reranker = CreateReranker(new RerankerOptions { RetrievalScoreWeight = 1, TokenOverlapWeight = 0, ExactMatchBonus = 0 });

        var low = MakeResult("irrelevant", score: 0.1);
        var mid = MakeResult("irrelevant", score: 0.5);
        var high = MakeResult("irrelevant", score: 0.9);

        var results = await reranker.RerankAsync("query", [low, high, mid], topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal(high.ChunkId, results[0].ChunkId);
        Assert.Equal(mid.ChunkId, results[1].ChunkId);
    }

    [Fact]
    public async Task RerankAsync_EmptyQuery_ProducesZeroOverlapWithoutThrowing()
    {
        var reranker = CreateReranker(new RerankerOptions { RetrievalScoreWeight = 0, TokenOverlapWeight = 1, ExactMatchBonus = 0 });

        var candidate = MakeResult("some content");

        var results = await reranker.RerankAsync(string.Empty, [candidate], topK: 1);

        Assert.Equal(0.0, results[0].Score, precision: 6);
    }

    [Fact]
    public async Task RerankAsync_SingleCharacterTokensAreIgnored()
    {
        // The tokenizer keeps only tokens with length > 1, so a query of single characters
        // should behave like an empty token set (zero overlap contribution).
        var reranker = CreateReranker(new RerankerOptions { RetrievalScoreWeight = 0, TokenOverlapWeight = 1, ExactMatchBonus = 0 });

        var candidate = MakeResult("a b c");

        var results = await reranker.RerankAsync("a b c", [candidate], topK: 1);

        Assert.Equal(0.0, results[0].Score, precision: 6);
    }
}
