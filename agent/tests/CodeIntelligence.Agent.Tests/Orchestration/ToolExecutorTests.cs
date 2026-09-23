using System.Text.Json;
using CodeIntelligence.Agent.Orchestration;
using CodeIntelligence.Agent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Agent.Tests.Orchestration;

public sealed class ToolExecutorTests
{
    private static ToolExecutor CreateExecutor(TimeSpan? timeout = null) => new(
        Options.Create(new AgentOptions { ToolTimeout = timeout ?? TimeSpan.FromSeconds(5) }),
        NullLogger<ToolExecutor>.Instance);

    [Fact]
    public async Task Missing_required_argument_is_rejected_before_the_tool_runs()
    {
        var executor = CreateExecutor();
        var tool = new StubTool("t", _ => throw new InvalidOperationException("should not be called"));

        var result = await executor.ExecuteAsync(tool, new Dictionary<string, JsonElement>(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("arg", result.Content);
        Assert.Equal(0, tool.CallCount);
    }

    [Fact]
    public async Task Exception_from_the_tool_becomes_a_safe_error_result()
    {
        var executor = CreateExecutor();
        var tool = new StubTool("t", _ => throw new InvalidOperationException("connection string leaked here"));
        var input = new Dictionary<string, JsonElement> { ["arg"] = JsonSerializer.SerializeToElement("x") };

        var result = await executor.ExecuteAsync(tool, input, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.DoesNotContain("connection string leaked here", result.Content);
    }

    [Fact]
    public async Task Tool_that_exceeds_the_timeout_is_cancelled_and_reported_as_an_error()
    {
        var executor = CreateExecutor(TimeSpan.FromMilliseconds(50));
        var tool = new SlowTool();
        var input = new Dictionary<string, JsonElement> { ["arg"] = JsonSerializer.SerializeToElement("x") };

        var result = await executor.ExecuteAsync(tool, input, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("timed out", result.Content);
    }

    private sealed class SlowTool : ITool
    {
        public CodeIntelligence.Agent.Llm.LlmToolDefinition Definition { get; } = new(
            "slow_tool", "Never completes in time.",
            new Dictionary<string, JsonElement> { ["arg"] = JsonSerializer.SerializeToElement(new { type = "string" }) },
            ["arg"]);

        public async Task<ToolResult> ExecuteAsync(
            IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return ToolResult.Ok("too late");
        }
    }
}
