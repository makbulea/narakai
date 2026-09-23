using CodeIntelligence.Rag.Chunking;
using CodeIntelligence.Rag.Chunking.Generic;
using CodeIntelligence.Rag.Scanning;
using SymbolKind = CodeIntelligence.Rag.Models.SymbolKind;

namespace CodeIntelligence.Rag.Tests.Chunking;

public sealed class GenericTextChunkerTests
{
    private readonly GenericTextChunker _chunker = new();

    private static ScannedFile MakeFile(string extension, string relativePath) =>
        new(AbsolutePath: $"/repo/{relativePath}", RelativePath: relativePath, Extension: extension, SizeBytes: 0, LastModifiedUtc: DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(".json", "config.json", "json")]
    [InlineData(".yaml", "config.yaml", "yaml")]
    [InlineData(".yml", "config.yml", "yaml")]
    [InlineData(".md", "readme.md", "markdown")]
    [InlineData(".sql", "schema.sql", "sql")]
    [InlineData(".xml", "config.xml", "xml")]
    [InlineData(".csproj", "App.csproj", "xml")]
    public void Chunk_FileUnderLimit_MapsExtensionToCorrectLanguage(string extension, string relativePath, string expectedLanguage)
    {
        const string source = "small content that fits in one chunk";

        var chunks = _chunker.Chunk(MakeFile(extension, relativePath), source, new ChunkingOptions());

        var chunk = Assert.Single(chunks);
        Assert.Equal(expectedLanguage, chunk.Language);
        Assert.Equal(source, chunk.Content);
        Assert.Equal(SymbolKind.File, chunk.SymbolKind);
        Assert.Equal(1, chunk.StartLine);
    }

    [Fact]
    public void Chunk_UnknownExtension_FallsBackToTextLanguage()
    {
        var chunks = _chunker.Chunk(MakeFile(".txt", "notes.txt"), "hello", new ChunkingOptions());

        var chunk = Assert.Single(chunks);
        Assert.Equal("text", chunk.Language);
    }

    [Fact]
    public void Chunk_FileUnderLimit_ComputesCorrectEndLineFromNewlines()
    {
        var source = "line1\nline2\nline3";

        var chunks = _chunker.Chunk(MakeFile(".md", "notes.md"), source, new ChunkingOptions());

        var chunk = Assert.Single(chunks);
        Assert.Equal(1, chunk.StartLine);
        Assert.Equal(3, chunk.EndLine);
    }

    [Fact]
    public void Chunk_FileOverLimit_IsSplitViaLineSplitter()
    {
        var lines = Enumerable.Range(0, 50).Select(i => $"This is line number {i} with some padding text.");
        var source = string.Join('\n', lines);

        var options = new ChunkingOptions { MaxChunkSizeChars = 200, OverlapLines = 2 };
        var chunks = _chunker.Chunk(MakeFile(".md", "big.md"), source, options);

        Assert.True(chunks.Count > 1, "Expected the oversized file to be split into multiple chunks.");
        Assert.All(chunks, c =>
        {
            Assert.Equal("markdown", c.Language);
            Assert.Equal(SymbolKind.File, c.SymbolKind);
            Assert.Null(c.ClassName);
            Assert.Null(c.MethodName);
        });

        // Cross-check against LineSplitter directly: the chunker must not diverge from the
        // shared splitting logic it's supposed to delegate to.
        var expectedSlices = LineSplitter.Split(source.Split('\n'), options.MaxChunkSizeChars, options.OverlapLines);
        Assert.Equal(expectedSlices.Count, chunks.Count);

        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(expectedSlices[i].FirstLine + 1, chunks[i].StartLine);
            Assert.Equal(expectedSlices[i].LastLine + 1, chunks[i].EndLine);
            Assert.Equal(string.Join('\n', expectedSlices[i].Lines), chunks[i].Content);
        }
    }

    [Fact]
    public void Chunk_ContentExactlyAtLimit_StaysAsOneChunk()
    {
        var source = new string('a', 100);
        var options = new ChunkingOptions { MaxChunkSizeChars = 100 };

        var chunks = _chunker.Chunk(MakeFile(".md", "exact.md"), source, options);

        Assert.Single(chunks);
    }
}
