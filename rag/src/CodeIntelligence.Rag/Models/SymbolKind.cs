namespace CodeIntelligence.Rag.Models;

/// <summary>
/// The kind of source-code symbol a chunk represents. Populated by language-aware
/// chunkers (e.g. the Roslyn-based C# chunker); generic text chunkers use <see cref="File"/>.
/// </summary>
public enum SymbolKind
{
    File,
    Namespace,
    Class,
    Interface,
    Record,
    Struct,
    Enum,
    Method,
    Constructor,
    Property,
    Field,
    Event,
    Other
}
