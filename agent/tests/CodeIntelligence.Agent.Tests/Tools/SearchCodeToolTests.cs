using System.Text.Json;
using CodeIntelligence.Agent.RagIntegration;
using CodeIntelligence.Agent.Tools;
using Moq;

namespace CodeIntelligence.Agent.Tests.Tools;

public sealed class SearchCodeToolTests
{
    private static readonly Guid RepositoryId = Guid.NewGuid();

    private static Dictionary<string, JsonElement> Input(string repositoryId, string query, int? topK = null)
    {
        var dict = new Dictionary<string, JsonElement>
        {
            ["repositoryId"] = JsonSerializer.SerializeToElement(repositoryId),
            ["query"] = JsonSerializer.SerializeToElement(query)
        };
        if (topK is not null)
        {
            dict["topK"] = JsonSerializer.SerializeToElement(topK.Value);
        }

        return dict;
    }

    [Fact]
    public async Task Returns_content_and_sources_for_each_match()
    {
        var rag = new Mock<IRagServiceClient>();
        rag.Setup(r => r.HybridSearchAsync(RepositoryId, "order creation", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RagSearchResult(
                    Guid.NewGuid(), "src/OrderService.cs", 10, 20, "public class OrderService { }", 0.9,
                    new RagChunkMetadata("csharp", "OrderService", "ECommerce.Orders", "OrderService", "CreateAsync", RagSymbolKind.Method))
            ]);

        var tool = new SearchCodeTool(rag.Object);
        var result = await tool.ExecuteAsync(Input(RepositoryId.ToString(), "order creation"));

        Assert.False(result.IsError);
        Assert.Contains("src/OrderService.cs:10-20", result.Content);
        Assert.Single(result.Sources);
        Assert.Equal("src/OrderService.cs", result.Sources[0].FilePath);
    }

    [Fact]
    public async Task No_results_is_reported_as_a_clean_ok_result_not_an_error()
    {
        var rag = new Mock<IRagServiceClient>();
        rag.Setup(r => r.HybridSearchAsync(RepositoryId, "nonexistent", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new SearchCodeTool(rag.Object);
        var result = await tool.ExecuteAsync(Input(RepositoryId.ToString(), "nonexistent"));

        Assert.False(result.IsError);
        Assert.Empty(result.Sources);
        Assert.Contains("No matching code found", result.Content);
    }

    [Fact]
    public async Task Invalid_repository_id_is_rejected_without_calling_rag()
    {
        var rag = new Mock<IRagServiceClient>();
        var tool = new SearchCodeTool(rag.Object);

        var result = await tool.ExecuteAsync(Input("not-a-guid", "query"));

        Assert.True(result.IsError);
        rag.Verify(
            r => r.HybridSearchAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Empty_query_is_rejected()
    {
        var rag = new Mock<IRagServiceClient>();
        var tool = new SearchCodeTool(rag.Object);

        var result = await tool.ExecuteAsync(Input(RepositoryId.ToString(), "   "));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task TopK_defaults_to_five_when_not_supplied()
    {
        var rag = new Mock<IRagServiceClient>();
        rag.Setup(r => r.HybridSearchAsync(RepositoryId, "q", 5, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var tool = new SearchCodeTool(rag.Object);
        await tool.ExecuteAsync(Input(RepositoryId.ToString(), "q"));

        rag.Verify(r => r.HybridSearchAsync(RepositoryId, "q", 5, It.IsAny<CancellationToken>()), Times.Once);
    }
}
