namespace CodeIntelligence.Rag.Scanning;

/// <summary>Configuration for <see cref="RepositoryScanner"/>. Bind from "Rag:Scanner".</summary>
public sealed class RepositoryScannerOptions
{
    public const string SectionName = "Rag:Scanner";

    /// <summary>File extensions to index, including the leading dot.</summary>
    public HashSet<string> IncludedExtensions { get; init; } =
        [".cs", ".csproj", ".json", ".yaml", ".yml", ".md", ".sql", ".xml"];

    /// <summary>Directory names to skip anywhere in the tree, matched case-insensitively.</summary>
    public HashSet<string> IgnoredDirectoryNames { get; init; } =
        [".git", "bin", "obj", "node_modules", "packages", ".vs", "build", "dist"];

    /// <summary>
    /// Filename suffixes/globs that mark generated files, skipped even inside included
    /// directories (e.g. "*.generated.cs", "*.Designer.cs", "*.g.cs", "*.g.i.cs").
    /// </summary>
    public HashSet<string> GeneratedFileSuffixes { get; init; } =
        [".generated.cs", ".designer.cs", ".g.cs", ".g.i.cs", ".AssemblyInfo.cs"];
}
