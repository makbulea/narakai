using System.Text.Json;
using CodeIntelligence.Agent.Llm;
using CodeIntelligence.Agent.Models;
using CodeIntelligence.Agent.RagIntegration;

namespace CodeIntelligence.Agent.Tools;

/// <summary>
/// Wraps RAG's hybrid search (rag/docs/hybrid-search.md) as an agent tool — the primary
/// way the agent locates relevant code before reading anything in full. See
/// agent/AGENT_IMPLEMENTATION.md §4.1.
/// </summary>
public sealed class SearchCodeTool(IRagServiceClient rag) : ITool
{
    public LlmToolDefinition Definition { get; } = new(
        Name: "search_code",
        Description:
            "Search the indexed repository for code relevant to a natural-language query, " +
            "using combined semantic + keyword (hybrid) search. Use this first to find where " +
            "in the codebase a concept, class, or behavior lives.",
        Properties: new Dictionary<string, JsonElement>
        {
            ["repositoryId"] = Schema("string", "The repository's id (GUID) for this conversation."),
            ["query"] = Schema("string", "What to search for, e.g. 'who publishes OrderCreated'."),
            ["topK"] = Schema("integer", "Maximum number of results to return (default 5).")
        },
        Required: ["repositoryId", "query"]);

    public async Task<ToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(input["repositoryId"].GetString(), out var repositoryId))
        {
            return ToolResult.Error("'repositoryId' must be a valid GUID.");
        }

        var query = input["query"].GetString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Error("'query' must not be empty.");
        }

        var topK = input.TryGetValue("topK", out var topKElement) && topKElement.TryGetInt32(out var parsedTopK)
            ? parsedTopK
            : 5;

        var results = await rag.HybridSearchAsync(repositoryId, query, topK, cancellationToken).ConfigureAwait(false);

        if (results.Count == 0)
        {
            return ToolResult.Ok("No matching code found for this query in the indexed repository.");
        }

        var sources = results.Select(r => new SourceCitation(r.FilePath, r.StartLine, r.EndLine)).ToList();

        var content = string.Join("\n\n", results.Select(r =>
            $"[{r.Score:F3}] {r.FilePath}:{r.StartLine}-{r.EndLine} " +
            $"({r.Metadata.SymbolKind} {r.Metadata.ClassName}.{r.Metadata.MethodName})\n{r.Content}"));

        return ToolResult.Ok(content, sources);
    }

    private static JsonElement Schema(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });
}
