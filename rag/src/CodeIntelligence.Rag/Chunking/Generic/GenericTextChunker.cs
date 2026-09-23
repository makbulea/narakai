using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Scanning;

namespace CodeIntelligence.Rag.Chunking.Generic;

/// <summary>
/// Fallback chunker for non-C# indexable files (json, yaml, md, sql, xml, csproj).
/// These files don't have a language-aware symbol tree available here, so the whole
/// file is kept as one chunk whenever it fits the size budget, and only split by size
/// (with line overlap) when it doesn't. See docs/chunking.md for the rationale.
/// </summary>
public sealed class GenericTextChunker : IChunker
{
    private static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "json",
        [".yaml"] = "yaml",
        [".yml"] = "yaml",
        [".md"] = "markdown",
        [".sql"] = "sql",
        [".xml"] = "xml",
        [".csproj"] = "xml"
    };

    public bool CanHandle(string extension) => LanguageByExtension.ContainsKey(extension);

    public IReadOnlyList<ParsedChunk> Chunk(ScannedFile file, string sourceText, ChunkingOptions options)
    {
        var language = LanguageByExtension.GetValueOrDefault(file.Extension, "text");

        if (sourceText.Length <= options.MaxChunkSizeChars)
        {
            return
            [
                new ParsedChunk(
                    file.RelativePath,
                    StartLine: 1,
                    EndLine: Math.Max(1, sourceText.Count(c => c == '\n') + 1),
                    sourceText,
                    language,
                    Namespace: null,
                    ClassName: null,
                    MethodName: null,
                    SymbolKind.File)
            ];
        }

        var lines = sourceText.Split('\n');
        var slices = LineSplitter.Split(lines, options.MaxChunkSizeChars, options.OverlapLines);
        var chunks = new List<ParsedChunk>(slices.Count);

        foreach (var slice in slices)
        {
            var content = string.Join('\n', slice.Lines);
            chunks.Add(new ParsedChunk(
                file.RelativePath,
                StartLine: slice.FirstLine + 1,
                EndLine: slice.LastLine + 1,
                content,
                language,
                Namespace: null,
                ClassName: null,
                MethodName: null,
                SymbolKind.File));
        }

        return chunks;
    }
}
