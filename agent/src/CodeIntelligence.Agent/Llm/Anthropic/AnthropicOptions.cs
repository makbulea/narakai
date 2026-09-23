namespace CodeIntelligence.Agent.Llm.Anthropic;

/// <summary>
/// Configuration for <see cref="AnthropicLlmClient"/>. Bind from "Agent:Llm:Anthropic".
/// </summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Agent:Llm:Anthropic";

    /// <summary>
    /// Not "required init": DI wiring falls back to the ANTHROPIC_API_KEY environment
    /// variable via IOptions PostConfigure, which needs a settable property. Missing/empty
    /// is validated (and throws) at service registration time — see
    /// DependencyInjection/ServiceCollectionExtensions.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>claude-opus-5 is Anthropic's current recommended default; override only for a named reason.</summary>
    public string Model { get; init; } = "claude-opus-5";

    public int MaxTokens { get; init; } = 16000;
}
