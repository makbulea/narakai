using System.Text.Json;
using CodeIntelligence.Agent.Conversation;
using CodeIntelligence.Agent.Llm;
using CodeIntelligence.Agent.Models;
using CodeIntelligence.Agent.RagIntegration;
using CodeIntelligence.Agent.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Agent.Orchestration;

/// <summary>
/// The agent's bounded execution loop (agent/AGENT_IMPLEMENTATION.md §7): ask the LLM,
/// and while it asks for tools, run them and feed the results back — until it produces a
/// final answer or <see cref="AgentOptions.MaxIterations"/> is hit. This loop, not any
/// single LLM call, is what makes this an "agent" rather than a plain chat completion —
/// see agent/docs/AGENT.md for the concept.
/// </summary>
public sealed class AgentOrchestrator(
    ILlmClient llmClient,
    IRagServiceClient rag,
    ToolRegistry toolRegistry,
    ToolExecutor toolExecutor,
    PromptManager promptManager,
    IConversationStore conversationStore,
    IOptions<AgentOptions> options,
    ILogger<AgentOrchestrator> logger)
{
    private const string GiveUpMessage =
        "I wasn't able to reach a confident answer within the allowed number of research " +
        "steps. Try narrowing the question or asking about a more specific class or method.";

    private readonly AgentOptions _options = options.Value;

    public async Task<AgentAnswer> AskAsync(
        Guid conversationId, Guid repositoryId, string userMessage, CancellationToken cancellationToken = default)
    {
        // Fail fast and cheaply — no point spending an LLM call to discover the
        // repository the user named was never indexed (spec §16 test scenario: repository
        // not found).
        var repository = await rag.GetRepositoryAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (repository is null)
        {
            logger.LogWarning("[{ConversationId}] repository {RepositoryId} not found", conversationId, repositoryId);
            return new AgentAnswer(
                $"Repository '{repositoryId}' was not found. Index it first " +
                "(rag-cli index <path>, or POST /repositories/index on the RAG service).",
                [],
                []);
        }

        var history = await conversationStore.GetAsync(conversationId, cancellationToken).ConfigureAwait(false);
        var messages = new List<LlmMessage>(history) { LlmMessage.FromText(LlmRole.User, userMessage) };

        var systemPrompt = promptManager.BuildSystemPrompt(toolRegistry.All, repositoryId);
        var toolDefinitions = toolRegistry.All.Select(t => t.Definition).ToList();
        var toolCallLog = new List<ToolCallLog>();

        for (var iteration = 1; iteration <= _options.MaxIterations; iteration++)
        {
            logger.LogInformation(
                "[{ConversationId}] iteration {Iteration}/{MaxIterations}",
                conversationId, iteration, _options.MaxIterations);

            var response = await llmClient
                .CompleteAsync(new LlmRequest(systemPrompt, messages, toolDefinitions), cancellationToken)
                .ConfigureAwait(false);

            messages.Add(new LlmMessage(LlmRole.Assistant, response.Content));

            if (response.StopReason != LlmStopReason.ToolUse)
            {
                await conversationStore.SaveAsync(conversationId, messages, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "[{ConversationId}] final answer after {Iteration} iteration(s)", conversationId, iteration);
                return new AgentAnswer(response.TextContent, CollectSources(toolCallLog), toolCallLog);
            }

            var toolResultBlocks = new List<LlmContentBlock>();
            foreach (var toolUse in response.ToolUses)
            {
                var startedAt = DateTimeOffset.UtcNow;
                var tool = toolRegistry.Find(toolUse.Name);

                var result = tool is null
                    ? ToolResult.Error($"Unknown tool '{toolUse.Name}'.")
                    : await toolExecutor.ExecuteAsync(tool, toolUse.Input, cancellationToken).ConfigureAwait(false);

                var completedAt = DateTimeOffset.UtcNow;
                logger.LogInformation(
                    "[{ConversationId}] tool {ToolName} -> {ResultLength} chars, IsError={IsError}, {DurationMs} ms",
                    conversationId, toolUse.Name, result.Content.Length, result.IsError,
                    (completedAt - startedAt).TotalMilliseconds);

                toolCallLog.Add(new ToolCallLog(
                    toolUse.Name,
                    Summarize(toolUse.Input),
                    Truncate(result.Content, 500),
                    result.IsError,
                    result.Sources,
                    startedAt,
                    completedAt));

                toolResultBlocks.Add(new LlmToolResultBlock(toolUse.Id, result.Content, result.IsError));
            }

            messages.Add(new LlmMessage(LlmRole.User, toolResultBlocks));
        }

        logger.LogWarning(
            "[{ConversationId}] MaxIterations ({MaxIterations}) reached without a final answer",
            conversationId, _options.MaxIterations);

        await conversationStore.SaveAsync(conversationId, messages, cancellationToken).ConfigureAwait(false);
        return new AgentAnswer(GiveUpMessage, CollectSources(toolCallLog), toolCallLog);
    }

    private static IReadOnlyList<SourceCitation> CollectSources(IReadOnlyList<ToolCallLog> toolCalls) =>
        toolCalls.SelectMany(c => c.Sources).Distinct().ToList();

    private static string Summarize(IReadOnlyDictionary<string, JsonElement> input) =>
        string.Join(", ", input.Select(kv => $"{kv.Key}={Truncate(kv.Value.ToString(), 80)}"));

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
