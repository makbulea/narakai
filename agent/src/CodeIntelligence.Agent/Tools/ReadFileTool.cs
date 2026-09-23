using System.Text.Json;
using CodeIntelligence.Agent.Llm;
using CodeIntelligence.Agent.Models;
using CodeIntelligence.Agent.RagIntegration;

namespace CodeIntelligence.Agent.Tools;

/// <summary>
/// Reads exact source text from an indexed repository's filesystem, to give the agent
/// full context around a search_code result before it relies on that result. Assumes
/// this process has filesystem access to the same repositories RAG indexed — true for
/// local dev today; see agent/docs/AGENT.md for the trade-off and the REST-based
/// alternative if that assumption stops holding.
/// </summary>
public sealed class ReadFileTool(IRagServiceClient rag) : ITool
{
    private const int MaxContentChars = 20_000;

    public LlmToolDefinition Definition { get; } = new(
        Name: "read_file",
        Description:
            "Read the exact source text of a file (optionally a line range) from an indexed " +
            "repository. Use this to see full context around a search_code result.",
        Properties: new Dictionary<string, JsonElement>
        {
            ["repositoryId"] = Schema("string", "The repository's id (GUID) for this conversation."),
            ["filePath"] = Schema("string", "Path relative to the repository root, as returned by search_code."),
            ["startLine"] = Schema("integer", "Optional 1-based first line to return."),
            ["endLine"] = Schema("integer", "Optional 1-based last line to return (inclusive).")
        },
        Required: ["repositoryId", "filePath"]);

    public async Task<ToolResult> ExecuteAsync(
        IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(input["repositoryId"].GetString(), out var repositoryId))
        {
            return ToolResult.Error("'repositoryId' must be a valid GUID.");
        }

        var relativePath = input["filePath"].GetString();
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return ToolResult.Error("'filePath' must not be empty.");
        }

        var repository = await rag.GetRepositoryAsync(repositoryId, cancellationToken).ConfigureAwait(false);
        if (repository is null)
        {
            return ToolResult.Error($"Repository '{repositoryId}' was not found.");
        }

        var fullPath = ResolveWithinRoot(repository.RootPath, relativePath);
        if (fullPath is null)
        {
            // agent/AGENT_IMPLEMENTATION.md §15: never let the agent read outside the
            // repository it was given — reject anything that escapes RootPath, including
            // "../" traversal and absolute-path overrides.
            return ToolResult.Error("'filePath' resolves outside the repository root and was rejected.");
        }

        if (!File.Exists(fullPath))
        {
            return ToolResult.Error($"File '{relativePath}' does not exist in this repository.");
        }

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken).ConfigureAwait(false);

        var startLine = Math.Max(1, GetOptionalInt(input, "startLine") ?? 1);
        var endLine = Math.Min(lines.Length, GetOptionalInt(input, "endLine") ?? lines.Length);

        if (startLine > endLine)
        {
            return ToolResult.Error($"'startLine' ({startLine}) must not be after 'endLine' ({endLine}).");
        }

        var selected = string.Join('\n', lines[(startLine - 1)..endLine]);
        var content = selected.Length > MaxContentChars
            ? selected[..MaxContentChars] + "\n... (truncated)"
            : selected;

        return ToolResult.Ok(content, [new SourceCitation(relativePath, startLine, endLine)]);
    }

    /// <summary>Resolves relativePath under root and rejects any result that escapes it (path traversal guard).</summary>
    private static string? ResolveWithinRoot(string root, string relativePath)
    {
        var rootFull = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(rootFull, relativePath));

        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? candidate : null;
    }

    private static int? GetOptionalInt(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var element) && element.TryGetInt32(out var value) ? value : null;

    private static JsonElement Schema(string type, string description) =>
        JsonSerializer.SerializeToElement(new { type, description });
}
