using System.Text.Json;
using CodeIntelligence.Agent.Conversation;
using CodeIntelligence.Agent.Llm;
using CodeIntelligence.Agent.Orchestration;
using CodeIntelligence.Agent.RagIntegration;
using CodeIntelligence.Agent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CodeIntelligence.Agent.Tests.Orchestration;

/// <summary>
/// Covers the scenarios agent/AGENT_IMPLEMENTATION.md §16 calls out explicitly: a plain
/// answer with no tool needed, a single tool call, multiple tool calls, a failing tool,
/// hitting MaxIterations, an unknown repository, and an unknown/invalid tool call.
/// </summary>
public sealed class AgentOrchestratorTests
{
    private static readonly Guid RepositoryId = Guid.NewGuid();
    private static readonly RagRepository Repository = new(RepositoryId, "demo", "/repo", "abc123", "Completed");

    private static (AgentOrchestrator Orchestrator, Mock<ILlmClient> Llm, Mock<IRagServiceClient> Rag, StubTool Tool)
        Build(int maxIterations = 8, Func<IReadOnlyDictionary<string, JsonElement>, ToolResult>? toolHandler = null)
    {
        var llm = new Mock<ILlmClient>();
        var rag = new Mock<IRagServiceClient>();
        rag.Setup(r => r.GetRepositoryAsync(RepositoryId, It.IsAny<CancellationToken>())).ReturnsAsync(Repository);

        var tool = new StubTool("stub_tool", toolHandler ?? (_ => ToolResult.Ok("stub result")));
        var registry = new ToolRegistry([tool]);
        var executor = new ToolExecutor(
            Options.Create(new AgentOptions { ToolTimeout = TimeSpan.FromSeconds(5) }),
            NullLogger<ToolExecutor>.Instance);

        var orchestrator = new AgentOrchestrator(
            llm.Object,
            rag.Object,
            registry,
            executor,
            new PromptManager(),
            new InMemoryConversationStore(),
            Options.Create(new AgentOptions { MaxIterations = maxIterations }),
            NullLogger<AgentOrchestrator>.Instance);

        return (orchestrator, llm, rag, tool);
    }

    private static LlmResponse TextAnswer(string text) =>
        new(LlmStopReason.EndTurn, [new LlmTextBlock(text)]);

    private static LlmResponse ToolCall(string toolName, string toolUseId = "call-1") =>
        new(LlmStopReason.ToolUse, [new LlmToolUseBlock(toolUseId, toolName,
            new Dictionary<string, JsonElement> { ["arg"] = JsonSerializer.SerializeToElement("value") })]);

    private static LlmResponse TwoToolCalls(string toolName) =>
        new(LlmStopReason.ToolUse,
        [
            new LlmToolUseBlock("call-1", toolName, new Dictionary<string, JsonElement> { ["arg"] = JsonSerializer.SerializeToElement("a") }),
            new LlmToolUseBlock("call-2", toolName, new Dictionary<string, JsonElement> { ["arg"] = JsonSerializer.SerializeToElement("b") })
        ]);

    [Fact]
    public async Task Plain_question_returns_answer_without_any_tool_call()
    {
        var (orchestrator, llm, _, tool) = Build();
        llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextAnswer("The answer is X."));

        var result = await orchestrator.AskAsync(Guid.NewGuid(), RepositoryId, "What is X?");

        Assert.Equal("The answer is X.", result.Answer);
        Assert.Equal(0, tool.CallCount);
        llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Single_tool_call_then_final_answer()
    {
        var (orchestrator, llm, _, tool) = Build();
        llm.SetupSequence(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCall("stub_tool"))
            .ReturnsAsync(TextAnswer("Found it."));

        var result = await orchestrator.AskAsync(Guid.NewGuid(), RepositoryId, "Where is X?");

        Assert.Equal("Found it.", result.Answer);
        Assert.Equal(1, tool.CallCount);
        Assert.Single(result.ToolCalls);
        Assert.Equal("stub_tool", result.ToolCalls[0].ToolName);
    }

    [Fact]
    public async Task Multiple_tool_calls_in_one_turn_are_all_executed()
    {
        var (orchestrator, llm, _, tool) = Build();
        llm.SetupSequence(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TwoToolCalls("stub_tool"))
            .ReturnsAsync(TextAnswer("Combined answer."));

        var result = await orchestrator.AskAsync(Guid.NewGuid(), RepositoryId, "Compare A and B");

        Assert.Equal("Combined answer.", result.Answer);
        Assert.Equal(2, tool.CallCount);
        Assert.Equal(2, result.ToolCalls.Count);
    }

    [Fact]
    public async Task Tool_failure_is_reported_back_to_llm_and_loop_continues()
    {
        var (orchestrator, llm, _, tool) = Build(toolHandler: _ => throw new InvalidOperationException("boom"));
        llm.SetupSequence(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCall("stub_tool"))
            .ReturnsAsync(TextAnswer("Recovered after the failure."));

        var result = await orchestrator.AskAsync(Guid.NewGuid(), RepositoryId, "Question");

        Assert.Equal("Recovered after the failure.", result.Answer);
        Assert.True(result.ToolCalls[0].IsError);
        // The failure message must not leak the exception's own text (claude.md §15/§17).
        Assert.DoesNotContain("boom", result.ToolCalls[0].ResultSummary);
    }

    [Fact]
    public async Task Unknown_tool_name_produces_an_error_result_without_throwing()
    {
        var (orchestrator, llm, _, _) = Build();
        llm.SetupSequence(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCall("does_not_exist"))
            .ReturnsAsync(TextAnswer("Handled gracefully."));

        var result = await orchestrator.AskAsync(Guid.NewGuid(), RepositoryId, "Question");

        Assert.Equal("Handled gracefully.", result.Answer);
        Assert.True(result.ToolCalls[0].IsError);
    }

    [Fact]
    public async Task MaxIterations_reached_returns_a_give_up_message_instead_of_looping_forever()
    {
        var (orchestrator, llm, _, tool) = Build(maxIterations: 3);
        llm.Setup(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCall("stub_tool")); // never stops asking for the tool

        var result = await orchestrator.AskAsync(Guid.NewGuid(), RepositoryId, "Question");

        Assert.Equal(3, tool.CallCount);
        Assert.Contains("wasn't able to reach a confident answer", result.Answer);
    }

    [Fact]
    public async Task Unknown_repository_short_circuits_without_calling_the_llm()
    {
        var (orchestrator, llm, rag, _) = Build();
        var missingRepositoryId = Guid.NewGuid();
        rag.Setup(r => r.GetRepositoryAsync(missingRepositoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RagRepository?)null);

        var result = await orchestrator.AskAsync(Guid.NewGuid(), missingRepositoryId, "Question");

        Assert.Contains("was not found", result.Answer);
        llm.Verify(l => l.CompleteAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
