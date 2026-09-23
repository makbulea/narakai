using CodeIntelligence.Rag.Chunking;

namespace CodeIntelligence.Rag.Tests.Chunking;

public sealed class LineSplitterTests
{
    [Fact]
    public void Split_SlicesRespectMaxChunkSizeChars()
    {
        // 20 lines of 10 chars each (11 with the '\n' the algorithm accounts for) — well over
        // a 50-char budget, so this must produce several slices.
        var lines = Enumerable.Range(0, 20).Select(i => new string('a', 10)).ToArray();

        var slices = LineSplitter.Split(lines, maxChunkSizeChars: 50, overlapLines: 0);

        Assert.True(slices.Count > 1);
        foreach (var slice in slices)
        {
            var size = slice.Lines.Sum(l => l.Length + 1);
            Assert.True(size <= 50, $"Slice of size {size} exceeded the 50-char budget.");
        }
    }

    [Fact]
    public void Split_AppliesOverlapBetweenConsecutiveSlices()
    {
        var lines = Enumerable.Range(0, 10).Select(i => $"line{i}").ToArray();

        // Each line is 5-6 chars (+1 for the newline accounting) so a budget of 15 fits ~2 lines/slice.
        var slices = LineSplitter.Split(lines, maxChunkSizeChars: 15, overlapLines: 1);

        Assert.True(slices.Count > 1);
        for (var i = 0; i < slices.Count - 1; i++)
        {
            var current = slices[i];
            var next = slices[i + 1];
            // The next slice must start at or before the end of the current one (overlap),
            // and never restart at/behind its own start (forward progress).
            Assert.True(next.FirstLine <= current.LastLine);
            Assert.True(next.FirstLine > current.FirstLine);
        }
    }

    [Fact]
    public void Split_NoOverlapConfigured_SlicesAreContiguousNotOverlapping()
    {
        var lines = Enumerable.Range(0, 6).Select(i => $"line{i}").ToArray();

        var slices = LineSplitter.Split(lines, maxChunkSizeChars: 12, overlapLines: 0);

        for (var i = 0; i < slices.Count - 1; i++)
        {
            Assert.Equal(slices[i].LastLine + 1, slices[i + 1].FirstLine);
        }
    }

    [Fact]
    public void Split_SingleLineExceedingMaxSize_StillProducesThatLineAsItsOwnSlice()
    {
        var lines = new[] { new string('x', 500), "short" };

        var slices = LineSplitter.Split(lines, maxChunkSizeChars: 50, overlapLines: 5);

        Assert.Equal(2, slices.Count);
        Assert.Single(slices[0].Lines);
        Assert.Equal(500, slices[0].Lines[0].Length);
        Assert.Equal("short", Assert.Single(slices[1].Lines));
    }

    [Fact]
    public async Task Split_OversizedLineFollowedByManyLines_AlwaysMakesForwardProgressAndTerminates()
    {
        // A regression test for the forward-progress guard: without `Math.Max(start + 1, ...)`,
        // stepping back by overlapLines after a slice that contains just one oversized line
        // could re-select the same starting index forever. This must terminate quickly.
        var lines = new List<string> { new string('x', 1000) };
        lines.AddRange(Enumerable.Range(0, 50).Select(i => $"line{i}"));

        var task = Task.Run(() => LineSplitter.Split(lines.ToArray(), maxChunkSizeChars: 20, overlapLines: 10));
        var winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.True(winner == task, "LineSplitter.Split did not terminate within 3 seconds — forward-progress guard may be broken.");
    }

    [Fact]
    public void Split_EmptyInput_ReturnsNoSlices()
    {
        var slices = LineSplitter.Split([], maxChunkSizeChars: 100, overlapLines: 2);
        Assert.Empty(slices);
    }

    [Fact]
    public void Split_AllLinesFitInOneSlice_ReturnsSingleSlice()
    {
        var lines = new[] { "one", "two", "three" };
        var slices = LineSplitter.Split(lines, maxChunkSizeChars: 1000, overlapLines: 5);

        var slice = Assert.Single(slices);
        Assert.Equal(lines, slice.Lines);
        Assert.Equal(0, slice.FirstLine);
        Assert.Equal(2, slice.LastLine);
    }
}
