using System.Text.Json;
using CodeIntelligence.Agent.RagIntegration;
using CodeIntelligence.Agent.Tools;
using Moq;

namespace CodeIntelligence.Agent.Tests.Tools;

public sealed class ReadFileToolTests : IDisposable
{
    private static readonly Guid RepositoryId = Guid.NewGuid();
    private readonly string _repoRoot;

    public ReadFileToolTests()
    {
        _repoRoot = Directory.CreateTempSubdirectory("agent-readfile-tests-").FullName;
        File.WriteAllLines(Path.Combine(_repoRoot, "Sample.cs"), ["line1", "line2", "line3", "line4", "line5"]);

        var secretsDir = Directory.GetParent(_repoRoot)!.FullName;
        File.WriteAllText(Path.Combine(secretsDir, "outside-secret.txt"), "should never be readable");
    }

    public void Dispose() => Directory.Delete(_repoRoot, recursive: true);

    private ReadFileTool BuildTool()
    {
        var rag = new Mock<IRagServiceClient>();
        rag.Setup(r => r.GetRepositoryAsync(RepositoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagRepository(RepositoryId, "demo", _repoRoot, null, "Completed"));
        return new ReadFileTool(rag.Object);
    }

    private static Dictionary<string, JsonElement> Input(
        string repositoryId, string filePath, int? startLine = null, int? endLine = null)
    {
        var dict = new Dictionary<string, JsonElement>
        {
            ["repositoryId"] = JsonSerializer.SerializeToElement(repositoryId),
            ["filePath"] = JsonSerializer.SerializeToElement(filePath)
        };
        if (startLine is not null) dict["startLine"] = JsonSerializer.SerializeToElement(startLine.Value);
        if (endLine is not null) dict["endLine"] = JsonSerializer.SerializeToElement(endLine.Value);
        return dict;
    }

    [Fact]
    public async Task Reads_the_whole_file_when_no_line_range_is_given()
    {
        var tool = BuildTool();
        var result = await tool.ExecuteAsync(Input(RepositoryId.ToString(), "Sample.cs"));

        Assert.False(result.IsError);
        Assert.Equal("line1\nline2\nline3\nline4\nline5", result.Content);
        Assert.Equal(1, result.Sources[0].StartLine);
        Assert.Equal(5, result.Sources[0].EndLine);
    }

    [Fact]
    public async Task Reads_only_the_requested_line_range()
    {
        var tool = BuildTool();
        var result = await tool.ExecuteAsync(Input(RepositoryId.ToString(), "Sample.cs", startLine: 2, endLine: 3));

        Assert.Equal("line2\nline3", result.Content);
    }

    [Theory]
    [InlineData("../outside-secret.txt")]
    [InlineData("../../outside-secret.txt")]
    [InlineData("subdir/../../outside-secret.txt")]
    public async Task Path_traversal_outside_the_repository_root_is_rejected(string maliciousPath)
    {
        var tool = BuildTool();
        var result = await tool.ExecuteAsync(Input(RepositoryId.ToString(), maliciousPath));

        Assert.True(result.IsError);
        Assert.Contains("outside the repository root", result.Content);
    }

    [Fact]
    public async Task Nonexistent_file_is_reported_as_an_error()
    {
        var tool = BuildTool();
        var result = await tool.ExecuteAsync(Input(RepositoryId.ToString(), "DoesNotExist.cs"));

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Unknown_repository_is_reported_as_an_error()
    {
        var rag = new Mock<IRagServiceClient>();
        rag.Setup(r => r.GetRepositoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RagRepository?)null);
        var tool = new ReadFileTool(rag.Object);

        var result = await tool.ExecuteAsync(Input(Guid.NewGuid().ToString(), "Sample.cs"));

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content);
    }
}
