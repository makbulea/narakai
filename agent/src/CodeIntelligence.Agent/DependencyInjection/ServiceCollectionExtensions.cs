using CodeIntelligence.Agent.Conversation;
using CodeIntelligence.Agent.Llm;
using CodeIntelligence.Agent.Llm.Anthropic;
using CodeIntelligence.Agent.Orchestration;
using CodeIntelligence.Agent.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodeIntelligence.Agent.DependencyInjection;

/// <summary>
/// Wires up the agent's own services: LLM client, tools, orchestration, in-memory
/// conversation store. Deliberately does NOT register <c>IRagServiceClient</c>'s
/// HttpClient — that needs AddHttpClient + AddStandardResilience from
/// BuildingBlocks.Web, which this library does not (and should not) depend on. The host
/// (CodeIntelligence.Agent.Api) registers it, the same way OrderService registers its
/// own downstream HttpClients in Program.cs.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCodeIntelligenceAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));

        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty;
                }
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey),
                "Anthropic API key is missing. Set Agent:Llm:Anthropic:ApiKey or the ANTHROPIC_API_KEY environment variable.");

        services.AddSingleton<ILlmClient, AnthropicLlmClient>();

        services.AddSingleton<ITool, SearchCodeTool>();
        services.AddSingleton<ITool, ReadFileTool>();
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<ToolExecutor>();
        services.AddSingleton<PromptManager>();

        services.AddSingleton<IConversationStore, InMemoryConversationStore>();

        services.AddSingleton<AgentOrchestrator>();

        return services;
    }
}
