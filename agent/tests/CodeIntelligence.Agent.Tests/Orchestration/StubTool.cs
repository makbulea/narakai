using System.Text.Json;
using CodeIntelligence.Agent.Llm;
using CodeIntelligence.Agent.Tools;

namespace CodeIntelligence.Agent.Tests.Orchestration;

/// <summary>
/// A scriptable ITool for orchestrator tests — lets a test control exactly what a tool
/// call returns (including throwing) without depending on SearchCodeTool/ReadFileTool's
/// own RAG-calling logic, which is covered separately in Tools/*Tests.cs.
/// </summary>
internal sealed class StubTool(string name, Func<IReadOnlyDictionary<string, JsonElement>, ToolResult> handler) : ITool
{
    public int CallCount { get; private set; }

    public LlmToolDefinition Definition { get; } = new(
        Name: name,
        Description: "Stub tool for tests.",
        Properties: new Dictionary<string, JsonElement>
        {
            ["arg"] = JsonSerializer.SerializeToElement(new { type = "string" })
        },
        Required: ["arg"]);

    public Task<ToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(handler(input));
    }
}
