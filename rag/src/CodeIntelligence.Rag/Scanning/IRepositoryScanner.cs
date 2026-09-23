namespace CodeIntelligence.Rag.Scanning;

/// <summary>Discovers indexable source files under a repository root.</summary>
public interface IRepositoryScanner
{
    /// <summary>
    /// Recursively enumerates files under <paramref name="repositoryRootPath"/> that match
    /// the configured include/ignore rules. Streams results — does not materialize the
    /// whole file list in memory before the caller can start consuming it.
    /// </summary>
    IAsyncEnumerable<ScannedFile> ScanAsync(string repositoryRootPath, CancellationToken cancellationToken = default);
}
