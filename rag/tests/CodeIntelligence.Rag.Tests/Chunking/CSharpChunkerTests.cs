using CodeIntelligence.Rag.Chunking;
using CodeIntelligence.Rag.Chunking.CSharp;
using CodeIntelligence.Rag.Scanning;
using SymbolKind = CodeIntelligence.Rag.Models.SymbolKind;

namespace CodeIntelligence.Rag.Tests.Chunking;

public sealed class CSharpChunkerTests
{
    private readonly CSharpChunker _chunker = new();

    private static ScannedFile MakeFile(string relativePath = "Foo.cs") =>
        new(AbsolutePath: $"/repo/{relativePath}", RelativePath: relativePath, Extension: ".cs", SizeBytes: 0, LastModifiedUtc: DateTimeOffset.UtcNow);

    [Fact]
    public void Chunk_ClassWithTwoMethods_ProducesTwoMethodChunks()
    {
        const string source = """
            namespace MyApp.Services;

            public class OrderService
            {
                public void Create()
                {
                }

                public void Cancel()
                {
                }
            }
            """;

        var chunks = _chunker.Chunk(MakeFile(), source, new ChunkingOptions());

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c =>
        {
            Assert.Equal(SymbolKind.Method, c.SymbolKind);
            Assert.Equal("OrderService", c.ClassName);
            Assert.Equal("MyApp.Services", c.Namespace);
            Assert.Equal("csharp", c.Language);
        });
        Assert.Contains(chunks, c => c.MethodName == "Create");
        Assert.Contains(chunks, c => c.MethodName == "Cancel");
    }

    [Fact]
    public void Chunk_FieldsCollapseIntoOneFieldChunk()
    {
        const string source = """
            namespace MyApp;

            public class Config
            {
                private readonly int _a;
                private readonly string _b = "x";
                public const int Max = 10;
            }
            """;

        var chunks = _chunker.Chunk(MakeFile(), source, new ChunkingOptions());

        var fieldChunk = Assert.Single(chunks);
        Assert.Equal(SymbolKind.Field, fieldChunk.SymbolKind);
        Assert.Null(fieldChunk.MethodName);
        Assert.Equal("Config", fieldChunk.ClassName);
        Assert.Contains("_a", fieldChunk.Content);
        Assert.Contains("_b", fieldChunk.Content);
        Assert.Contains("Max", fieldChunk.Content);
    }

    [Fact]
    public void Chunk_MethodsAndFieldsTogether_ProducesSeparateChunksForEach()
    {
        const string source = """
            namespace MyApp;

            public class Widget
            {
                private readonly int _count;

                public void Increment()
                {
                    _count++;
                }
            }
            """;

        var chunks = _chunker.Chunk(MakeFile(), source, new ChunkingOptions());

        Assert.Equal(2, chunks.Count);
        Assert.Contains(chunks, c => c.SymbolKind == SymbolKind.Field);
        Assert.Contains(chunks, c => c.SymbolKind == SymbolKind.Method && c.MethodName == "Increment");
    }

    [Fact]
    public void Chunk_EmptyMarkerInterface_ProducesOneChunk()
    {
        const string source = """
            namespace MyApp;

            public interface IMarker
            {
            }
            """;

        var chunks = _chunker.Chunk(MakeFile(), source, new ChunkingOptions());

        var chunk = Assert.Single(chunks);
        Assert.Equal(SymbolKind.Interface, chunk.SymbolKind);
        Assert.Equal("IMarker", chunk.ClassName);
        Assert.Null(chunk.MethodName);
    }

    [Fact]
    public void Chunk_EmptyClass_ProducesOneChunk()
    {
        const string source = """
            namespace MyApp;

            public class Empty
            {
            }
            """;

        var chunks = _chunker.Chunk(MakeFile(), source, new ChunkingOptions());

        var chunk = Assert.Single(chunks);
        Assert.Equal(SymbolKind.Class, chunk.SymbolKind);
    }

    [Fact]
    public void Chunk_Enum_ProducesOneChunk()
    {
        const string source = """
            namespace MyApp;

            public enum Color
            {
                Red,
                Green,
                Blue
            }
            """;

        var chunks = _chunker.Chunk(MakeFile(), source, new ChunkingOptions());

        var chunk = Assert.Single(chunks);
        Assert.Equal(SymbolKind.Enum, chunk.SymbolKind);
        Assert.Equal("Color", chunk.ClassName);
    }

    [Fact]
    public void Chunk_TopLevelStatementsFile_FallsBackToOneWholeFileChunk()
    {
        const string source = """
            Console.WriteLine("Hello, world!");
            var x = 1 + 1;
            Console.WriteLine(x);
            """;

        var chunks = _chunker.Chunk(MakeFile("Program.cs"), source, new ChunkingOptions());

        var chunk = Assert.Single(chunks);
        Assert.Equal(SymbolKind.File, chunk.SymbolKind);
        Assert.Null(chunk.Namespace);
        Assert.Null(chunk.ClassName);
        Assert.Null(chunk.MethodName);
        Assert.Equal(source, chunk.Content);
        Assert.Equal(1, chunk.StartLine);
    }

    [Fact]
    public void Chunk_MemberExceedingMaxChunkSize_IsSplitWithOverlapAndPartHeader()
    {
        var bodyLines = Enumerable.Range(0, 40).Select(i => $"        Console.WriteLine(\"line {i} of a very long method body\");");
        var source = $$"""
            namespace MyApp;

            public class BigThing
            {
                public void HugeMethod()
                {
            {{string.Join('\n', bodyLines)}}
                }
            }
            """;

        var options = new ChunkingOptions { MaxChunkSizeChars = 300, OverlapLines = 3 };
        var chunks = _chunker.Chunk(MakeFile(), source, options);

        Assert.True(chunks.Count > 1, "Expected the oversized method to be split into multiple chunks.");
        Assert.All(chunks, c =>
        {
            Assert.Equal(SymbolKind.Method, c.SymbolKind);
            Assert.Equal("HugeMethod", c.MethodName);
            Assert.Equal("BigThing", c.ClassName);
        });

        for (var i = 0; i < chunks.Count; i++)
        {
            var expectedHeader = $"// MyApp.BigThing.HugeMethod — part {i + 1}/{chunks.Count} " +
                                  "(split: member exceeds the configured max chunk size)";
            Assert.StartsWith(expectedHeader, chunks[i].Content);
        }
    }

    [Fact]
    public void Chunk_NestedTypes_GetTheirOwnMemberChunksWithQualifiedClassName()
    {
        const string source = """
            namespace MyApp;

            public class Outer
            {
                public void OuterMethod()
                {
                }

                public class Inner
                {
                    public void InnerMethod()
                    {
                    }
                }
            }
            """;

        var chunks = _chunker.Chunk(MakeFile(), source, new ChunkingOptions());

        Assert.Equal(2, chunks.Count);

        var outerChunk = Assert.Single(chunks, c => c.MethodName == "OuterMethod");
        Assert.Equal("Outer", outerChunk.ClassName);

        var innerChunk = Assert.Single(chunks, c => c.MethodName == "InnerMethod");
        Assert.Equal("Outer.Inner", innerChunk.ClassName);
    }

    [Fact]
    public void Chunk_ConstructorAndPropertyAndIndexer_AreClassifiedCorrectly()
    {
        const string source = """
            namespace MyApp;

            public class Item
            {
                public Item(int id)
                {
                    Id = id;
                }

                public int Id { get; }

                public string this[int index] => index.ToString();
            }
            """;

        var chunks = _chunker.Chunk(MakeFile(), source, new ChunkingOptions());

        Assert.Equal(3, chunks.Count);
        Assert.Contains(chunks, c => c.SymbolKind == SymbolKind.Constructor && c.MethodName == "Item");
        Assert.Contains(chunks, c => c.SymbolKind == SymbolKind.Property && c.MethodName == "Id");
        Assert.Contains(chunks, c => c.SymbolKind == SymbolKind.Property && c.MethodName == "this[]");
    }
}
