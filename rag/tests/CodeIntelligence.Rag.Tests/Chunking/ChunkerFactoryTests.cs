using CodeIntelligence.Rag.Chunking;
using CodeIntelligence.Rag.Chunking.CSharp;
using CodeIntelligence.Rag.Chunking.Generic;

namespace CodeIntelligence.Rag.Tests.Chunking;

public sealed class ChunkerFactoryTests
{
    [Fact]
    public void GetChunker_CsExtension_ReturnsCSharpChunker()
    {
        var factory = new ChunkerFactory();
        Assert.IsType<CSharpChunker>(factory.GetChunker(".cs"));
    }

    [Fact]
    public void GetChunker_MdExtension_ReturnsGenericTextChunker()
    {
        var factory = new ChunkerFactory();
        Assert.IsType<GenericTextChunker>(factory.GetChunker(".md"));
    }

    [Fact]
    public void GetChunker_UnknownExtension_FallsBackToGenericTextChunker()
    {
        var factory = new ChunkerFactory();
        Assert.IsType<GenericTextChunker>(factory.GetChunker(".xyz"));
    }

    [Fact]
    public void GetChunker_WithOnlyCSharpChunkerRegistered_UnknownExtensionStillFallsBackToGenericTextChunker()
    {
        var factory = new ChunkerFactory([new CSharpChunker()]);
        Assert.IsType<GenericTextChunker>(factory.GetChunker(".json"));
    }

    [Fact]
    public void GetChunker_CustomChunkerList_PrefersFirstMatchingChunker()
    {
        var factory = new ChunkerFactory([new CSharpChunker(), new GenericTextChunker()]);
        Assert.IsType<CSharpChunker>(factory.GetChunker(".cs"));
        Assert.IsType<GenericTextChunker>(factory.GetChunker(".json"));
    }
}
