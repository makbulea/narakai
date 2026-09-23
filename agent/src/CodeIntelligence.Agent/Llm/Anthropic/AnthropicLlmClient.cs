using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Agent.Llm.Anthropic;

/// <summary>
/// <see cref="ILlmClient"/> backed by the official Anthropic SDK's plain (non-beta)
/// Messages API. Deliberately not the SDK's BetaToolRunner: this project exists partly
/// so the developer can read a real, hand-written agent loop (see
/// <see cref="Orchestration.AgentOrchestrator"/> and agent/AGENT_IMPLEMENTATION.md §7) —
/// a helper that hides the loop would defeat that. See agent/docs/AGENT.md for the
/// request/response mapping this class does.
/// </summary>
public sealed class AnthropicLlmClient : ILlmClient
{
    private readonly AnthropicClient _client;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicLlmClient> _logger;

    public AnthropicLlmClient(IOptions<AnthropicOptions> options, ILogger<AnthropicLlmClient> logger)
    {
        _options = options.Value;
        _client = new AnthropicClient { ApiKey = _options.ApiKey };
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            System = request.SystemPrompt,
            Messages = request.Messages.Select(ToMessageParam).ToList(),
            Tools = request.Tools.Count == 0 ? null : request.Tools.Select(ToTool).ToList()
        };

        _logger.LogDebug(
            "Calling Anthropic model {Model} with {MessageCount} message(s), {ToolCount} tool(s)",
            _options.Model, parameters.Messages.Count, request.Tools.Count);

        var response = await _client.Messages.Create(parameters).WaitAsync(cancellationToken).ConfigureAwait(false);

        return MapResponse(response);
    }

    private static MessageParam ToMessageParam(LlmMessage message)
    {
        var role = message.Role == LlmRole.User ? Role.User : Role.Assistant;

        // A plain single text turn maps to a plain string — matches every basic-usage
        // example in the SDK docs and keeps the common case (no tool use yet) simple.
        if (message.Content is [LlmTextBlock singleText])
        {
            return new MessageParam { Role = role, Content = singleText.Text };
        }

        var blocks = new List<ContentBlockParam>();
        foreach (var block in message.Content)
        {
            switch (block)
            {
                case LlmTextBlock text:
                    blocks.Add(new TextBlockParam { Text = text.Text });
                    break;

                case LlmToolUseBlock toolUse:
                    blocks.Add(new ToolUseBlockParam
                    {
                        ID = toolUse.Id,
                        Name = toolUse.Name,
                        Input = toolUse.Input
                    });
                    break;

                case LlmToolResultBlock toolResult:
                    blocks.Add(new ToolResultBlockParam
                    {
                        ToolUseID = toolResult.ToolUseId,
                        Content = toolResult.Content,
                        IsError = toolResult.IsError
                    });
                    break;
            }
        }

        return new MessageParam { Role = role, Content = blocks };
    }

    private static ToolUnion ToTool(LlmToolDefinition definition) => new Tool
    {
        Name = definition.Name,
        Description = definition.Description,
        InputSchema = new()
        {
            Properties = definition.Properties.ToDictionary(kv => kv.Key, kv => kv.Value),
            Required = definition.Required.ToList()
        }
    };

    private static LlmResponse MapResponse(Message response)
    {
        var blocks = new List<LlmContentBlock>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out TextBlock? text))
            {
                blocks.Add(new LlmTextBlock(text.Text));
            }
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
            {
                blocks.Add(new LlmToolUseBlock(toolUse.ID, toolUse.Name, toolUse.Input));
            }

            // Thinking blocks are intentionally not surfaced here: they are Claude's
            // internal reasoning trace, and the top-level claude.md §15 rule is explicit
            // that chain-of-thought must never be exposed or persisted. Only safe,
            // structured events (which tool, what result) are logged — see
            // AgentOrchestrator.
        }

        // response.StopReason is ApiEnum<string, StopReason>, not a plain string — it
        // has an implicit `==` comparison against a string literal (used here), but
        // isn't a constant the compiler accepts in a switch pattern (CS9135).
        var stopReason =
            response.StopReason == "tool_use" ? LlmStopReason.ToolUse
            : response.StopReason == "end_turn" ? LlmStopReason.EndTurn
            : response.StopReason == "max_tokens" ? LlmStopReason.MaxTokens
            : LlmStopReason.Other;

        return new LlmResponse(stopReason, blocks);
    }
}
