using System.Runtime.CompilerServices;

// Exposes `internal` types (e.g. Chunking.LineSplitter) to the test project so its
// line-splitting/overlap/no-infinite-loop behavior can be unit tested directly instead
// of only indirectly through the chunkers that call it.
[assembly: InternalsVisibleTo("CodeIntelligence.Rag.Tests")]
