using CodeIntelligence.Rag.Scanning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SymbolKind = CodeIntelligence.Rag.Models.SymbolKind;

namespace CodeIntelligence.Rag.Chunking.CSharp;

/// <summary>
/// Roslyn-based, symbol-aware chunker for C# source files.
///
/// Chunking strategy (see docs/chunking.md for the full rationale): a class/interface/
/// record/struct is split into its members — one chunk per method, constructor, property,
/// indexer or event, plus one combined chunk for all field declarations in the type. A type
/// with no such members (a marker interface, an empty class, an enum) becomes a single
/// chunk. Nested types are visited independently and produce their own member chunks. This
/// keeps each chunk aligned to a logical unit a developer would reason about, instead of an
/// arbitrary token window that can split a method body in half.
/// </summary>
public sealed class CSharpChunker : IChunker
{
    public bool CanHandle(string extension) => extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ParsedChunk> Chunk(ScannedFile file, string sourceText, ChunkingOptions options)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: file.AbsolutePath);
        var root = tree.GetCompilationUnitRoot();

        var typeDeclarations = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToList();
        if (typeDeclarations.Count == 0)
        {
            // No types at all (e.g. a top-level-statements program, or a partial fragment):
            // keep the whole file as one chunk rather than discarding it.
            return ChunkWholeFile(file, sourceText);
        }

        var chunks = new List<ParsedChunk>();

        foreach (var typeDecl in typeDeclarations)
        {
            var ns = GetNamespace(typeDecl);
            var className = GetQualifiedTypeName(typeDecl);

            if (typeDecl is EnumDeclarationSyntax enumDecl)
            {
                chunks.Add(BuildChunk(tree, file, ns, className, methodName: null, SymbolKind.Enum, enumDecl));
                continue;
            }

            if (typeDecl is not TypeDeclarationSyntax typeDeclaration)
            {
                continue;
            }

            var members = typeDeclaration.Members;
            var memberChunksAdded = false;

            foreach (var member in members)
            {
                var classified = Classify(member);
                if (classified is null)
                {
                    continue;
                }

                var (methodName, kind) = classified.Value;
                chunks.AddRange(BuildMemberChunks(tree, file, ns, className, methodName, kind, member, options));
                memberChunksAdded = true;
            }

            var fields = members.OfType<FieldDeclarationSyntax>().ToList();
            if (fields.Count > 0)
            {
                chunks.Add(BuildFieldsChunk(tree, file, ns, className, fields));
                memberChunksAdded = true;
            }

            var hasNestedTypes = members.OfType<BaseTypeDeclarationSyntax>().Any();
            if (!memberChunksAdded && !hasNestedTypes)
            {
                // Truly empty type (e.g. a marker interface): keep it as one chunk so it's
                // still discoverable, since it produced no member chunks of its own.
                chunks.Add(BuildChunk(
                    tree, file, ns, className, methodName: null, GetTypeSymbolKind(typeDeclaration), typeDeclaration));
            }
        }

        return chunks;
    }

    private static (string Name, SymbolKind Kind)? Classify(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => (m.Identifier.Text, SymbolKind.Method),
        ConstructorDeclarationSyntax c => (c.Identifier.Text, SymbolKind.Constructor),
        PropertyDeclarationSyntax p => (p.Identifier.Text, SymbolKind.Property),
        IndexerDeclarationSyntax => ("this[]", SymbolKind.Property),
        EventDeclarationSyntax e => (e.Identifier.Text, SymbolKind.Event),
        EventFieldDeclarationSyntax ef => (string.Join(", ", ef.Declaration.Variables.Select(v => v.Identifier.Text)), SymbolKind.Event),
        _ => null
    };

    private static IEnumerable<ParsedChunk> BuildMemberChunks(
        SyntaxTree tree,
        ScannedFile file,
        string? ns,
        string className,
        string methodName,
        SymbolKind kind,
        MemberDeclarationSyntax member,
        ChunkingOptions options)
    {
        var content = member.ToFullString().Trim('\n', '\r');
        var lineSpan = tree.GetLineSpan(member.FullSpan);
        var startLine = lineSpan.StartLinePosition.Line + 1;

        if (content.Length <= options.MaxChunkSizeChars)
        {
            var endLine = lineSpan.EndLinePosition.Line + 1;
            yield return new ParsedChunk(file.RelativePath, startLine, endLine, content, "csharp", ns, className, methodName, kind);
            yield break;
        }

        // The member is larger than the configured budget (e.g. a very large generated-code
        // method): fall back to a bounded, overlapping line split so we still index it,
        // rather than emitting one unbounded chunk or silently dropping it.
        var lines = content.Split('\n');
        var slices = LineSplitter.Split(lines, options.MaxChunkSizeChars, options.OverlapLines);

        for (var i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            var header = $"// {ns}.{className}.{methodName} — part {i + 1}/{slices.Count} " +
                         "(split: member exceeds the configured max chunk size)\n";
            var partContent = header + string.Join('\n', slice.Lines);
            yield return new ParsedChunk(
                file.RelativePath,
                startLine + slice.FirstLine,
                startLine + slice.LastLine,
                partContent,
                "csharp",
                ns,
                className,
                methodName,
                kind);
        }
    }

    private static ParsedChunk BuildFieldsChunk(
        SyntaxTree tree, ScannedFile file, string? ns, string className, List<FieldDeclarationSyntax> fields)
    {
        var content = string.Join("\n\n", fields.Select(f => f.ToFullString().Trim('\n', '\r')));
        var startLine = tree.GetLineSpan(fields[0].FullSpan).StartLinePosition.Line + 1;
        var endLine = tree.GetLineSpan(fields[^1].FullSpan).EndLinePosition.Line + 1;
        return new ParsedChunk(file.RelativePath, startLine, endLine, content, "csharp", ns, className, MethodName: null, SymbolKind.Field);
    }

    private static ParsedChunk BuildChunk(
        SyntaxTree tree, ScannedFile file, string? ns, string className, string? methodName, SymbolKind kind, SyntaxNode node)
    {
        var content = node.ToFullString().Trim('\n', '\r');
        var lineSpan = tree.GetLineSpan(node.FullSpan);
        return new ParsedChunk(
            file.RelativePath,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.EndLinePosition.Line + 1,
            content,
            "csharp",
            ns,
            className,
            methodName,
            kind);
    }

    private static IReadOnlyList<ParsedChunk> ChunkWholeFile(ScannedFile file, string sourceText) =>
    [
        new ParsedChunk(
            file.RelativePath,
            StartLine: 1,
            EndLine: Math.Max(1, sourceText.Count(c => c == '\n') + 1),
            sourceText,
            "csharp",
            Namespace: null,
            ClassName: null,
            MethodName: null,
            SymbolKind.File)
    ];

    private static string? GetNamespace(SyntaxNode node) =>
        node.FirstAncestorOrSelf<BaseNamespaceDeclarationSyntax>()?.Name.ToString();

    private static string GetQualifiedTypeName(BaseTypeDeclarationSyntax typeDecl)
    {
        var names = new List<string> { typeDecl.Identifier.Text };
        var current = typeDecl.Parent;
        while (current is BaseTypeDeclarationSyntax parentType)
        {
            names.Insert(0, parentType.Identifier.Text);
            current = current.Parent;
        }

        return string.Join('.', names);
    }

    private static SymbolKind GetTypeSymbolKind(TypeDeclarationSyntax typeDecl) => typeDecl switch
    {
        InterfaceDeclarationSyntax => SymbolKind.Interface,
        RecordDeclarationSyntax => SymbolKind.Record,
        StructDeclarationSyntax => SymbolKind.Struct,
        ClassDeclarationSyntax => SymbolKind.Class,
        _ => SymbolKind.Other
    };
}
