namespace CodeIntelligence.Rag.Scanning;

/// <summary>A source file discovered by <see cref="IRepositoryScanner"/>, not yet parsed.</summary>
public sealed record ScannedFile(
    string AbsolutePath,
    string RelativePath,
    string Extension,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);
