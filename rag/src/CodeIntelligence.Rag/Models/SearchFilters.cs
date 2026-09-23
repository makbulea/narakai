namespace CodeIntelligence.Rag.Models;

/// <summary>
/// Metadata filters applied alongside vector/lexical similarity. Every property is
/// optional; a null value means "don't filter on this field". String filters are
/// exact matches (case-insensitive) — see docs/vector-search.md for the SQL shape.
/// </summary>
public sealed class SearchFilters
{
    public string? Language { get; init; }
    public string? Service { get; init; }
    public string? FilePath { get; init; }
    public string? Namespace { get; init; }
    public string? ClassName { get; init; }
    public string? MethodName { get; init; }
    public SymbolKind? SymbolKind { get; init; }
}
