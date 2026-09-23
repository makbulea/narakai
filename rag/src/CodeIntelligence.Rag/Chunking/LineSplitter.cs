namespace CodeIntelligence.Rag.Chunking;

/// <summary>
/// Splits an array of lines into size-bounded slices with a bounded line overlap between
/// consecutive slices. Shared by the generic chunker and by the C# chunker's fallback for
/// oversized members. Always makes forward progress even if a single line exceeds the
/// configured max size.
/// </summary>
internal static class LineSplitter
{
    public readonly record struct Slice(IReadOnlyList<string> Lines, int FirstLine, int LastLine);

    public static List<Slice> Split(string[] lines, int maxChunkSizeChars, int overlapLines)
    {
        var result = new List<Slice>();
        var i = 0;

        while (i < lines.Length)
        {
            var start = i;
            var current = new List<string>();
            var size = 0;

            while (i < lines.Length && (current.Count == 0 || size + lines[i].Length + 1 <= maxChunkSizeChars))
            {
                current.Add(lines[i]);
                size += lines[i].Length + 1;
                i++;
            }

            result.Add(new Slice(current, start, i - 1));

            if (i >= lines.Length)
            {
                break;
            }

            // Step back for overlap, but always advance past `start` to guarantee termination.
            i = Math.Max(start + 1, i - overlapLines);
        }

        return result;
    }
}
