using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.Scanning;

public sealed class RepositoryScanner(
    IOptions<RepositoryScannerOptions> options,
    ILogger<RepositoryScanner> logger) : IRepositoryScanner
{
    private readonly RepositoryScannerOptions _options = options.Value;

    public async IAsyncEnumerable<ScannedFile> ScanAsync(
        string repositoryRootPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(repositoryRootPath))
        {
            throw new DirectoryNotFoundException($"Repository root not found: {repositoryRootPath}");
        }

        var root = Path.GetFullPath(repositoryRootPath);
        var discovered = 0;
        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = directories.Pop();

            IEnumerable<string> subDirectories;
            IEnumerable<string> files;
            try
            {
                subDirectories = Directory.EnumerateDirectories(currentDirectory);
                files = Directory.EnumerateFiles(currentDirectory);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Skipping directory without read access: {Directory}", currentDirectory);
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                var name = Path.GetFileName(subDirectory);
                if (!_options.IgnoredDirectoryNames.Contains(name))
                {
                    directories.Push(subDirectory);
                }
            }

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsIncluded(filePath))
                {
                    continue;
                }

                FileInfo fileInfo;
                try
                {
                    fileInfo = new FileInfo(filePath);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Skipping unreadable file: {File}", filePath);
                    continue;
                }

                discovered++;
                yield return new ScannedFile(
                    AbsolutePath: fileInfo.FullName,
                    RelativePath: Path.GetRelativePath(root, fileInfo.FullName),
                    Extension: fileInfo.Extension.ToLowerInvariant(),
                    SizeBytes: fileInfo.Length,
                    LastModifiedUtc: fileInfo.LastWriteTimeUtc);
            }

            // Yield control periodically so a very large tree doesn't block the caller's
            // synchronization context for the whole scan.
            if (discovered % 256 == 0)
            {
                await Task.Yield();
            }
        }

        logger.LogInformation("Repository scan of {Root} discovered {Count} indexable files", root, discovered);
    }

    private bool IsIncluded(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (!_options.IncludedExtensions.Contains(extension))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        foreach (var suffix in _options.GeneratedFileSuffixes)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
