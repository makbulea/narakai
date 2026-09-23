using System.Text.Json;

namespace CodeIntelligence.Agent.Llm;

public enum LlmRole
{
    User,
    Assistant
}

/// <summary>
/// One block of message content. A message is a list of these rather than a plain
/// string because a single assistant turn can mix text and tool calls, and a single
/// user turn can carry multiple tool results (see <see cref="Orchestration.AgentOrchestrator"/>).
/// </summary>
public abstract record LlmContentBlock;

public sealed record LlmTextBlock(string Text) : LlmContentBlock;

/// <summary>The model asking to invoke a tool. <c>Input</c> mirrors the wire shape (a JSON object) directly.</summary>
public sealed record LlmToolUseBlock(string Id, string Name, IReadOnlyDictionary<string, JsonElement> Input) : LlmContentBlock;

/// <summary>The result of one tool call, sent back to the model in the next user turn.</summary>
public sealed record LlmToolResultBlock(string ToolUseId, string Content, bool IsError = false) : LlmContentBlock;

public sealed record LlmMessage(LlmRole Role, IReadOnlyList<LlmContentBlock> Content)
{
    public static LlmMessage FromText(LlmRole role, string text) => new(role, [new LlmTextBlock(text)]);
}

/// <summary>
/// A tool's schema as the LLM sees it. <c>Properties</c>/<c>Required</c> use the same
/// shape as a JSON Schema "object" — the convention both Anthropic and OpenAI-style
/// function calling share — so this stays meaningful if a second <see cref="ILlmClient"/>
/// implementation is ever added.
/// </summary>
public sealed record LlmToolDefinition(
    string Name,
    string Description,
    IReadOnlyDictionary<string, JsonElement> Properties,
    IReadOnlyList<string> Required);

public enum LlmStopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
    Other
}

public sealed record LlmResponse(LlmStopReason StopReason, IReadOnlyList<LlmContentBlock> Content)
{
    public IEnumerable<LlmToolUseBlock> ToolUses => Content.OfType<LlmToolUseBlock>();

    public string TextContent => string.Concat(Content.OfType<LlmTextBlock>().Select(t => t.Text));
}

public sealed record LlmRequest(
    string SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmToolDefinition> Tools);

/// <summary>
/// Provider-agnostic seam over the LLM (claude.md §11 — never hard-code a provider
/// throughout the agent). <see cref="Anthropic.AnthropicLlmClient"/> is the only
/// implementation today; swapping providers means writing a new one of these, nothing
/// in <see cref="Orchestration.AgentOrchestrator"/> changes.
/// </summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
