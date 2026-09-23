using CodeIntelligence.Rag.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeIntelligence.Rag.Tests.Scanning;

public sealed class RepositoryScannerTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("rag-scanner-tests-");

    public void Dispose() => _root.Delete(recursive: true);

    private static RepositoryScanner CreateScanner(RepositoryScannerOptions? options = null) =>
        new(Options.Create(options ?? new RepositoryScannerOptions()), NullLogger<RepositoryScanner>.Instance);

    private static void WriteFile(string path, string content = "// content")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<List<ScannedFile>> ScanAllAsync(RepositoryScanner scanner, string? root = null)
    {
        var results = new List<ScannedFile>();
        await foreach (var file in scanner.ScanAsync(root ?? _root.FullName))
        {
            results.Add(file);
        }

        return results;
    }

    [Fact]
    public async Task ScanAsync_IgnoresConfiguredDirectoryNames()
    {
        WriteFile(Path.Combine(_root.FullName, "src", "Foo.cs"));
        WriteFile(Path.Combine(_root.FullName, "bin", "Ignored.cs"));
        WriteFile(Path.Combine(_root.FullName, "obj", "Ignored.cs"));
        WriteFile(Path.Combine(_root.FullName, "node_modules", "pkg", "Ignored.cs"));
        WriteFile(Path.Combine(_root.FullName, "packages", "Ignored.cs"));
        WriteFile(Path.Combine(_root.FullName, ".git", "Ignored.cs"));
        WriteFile(Path.Combine(_root.FullName, ".vs", "Ignored.cs"));
        WriteFile(Path.Combine(_root.FullName, "build", "Ignored.cs"));
        WriteFile(Path.Combine(_root.FullName, "dist", "Ignored.cs"));

        var results = await ScanAllAsync(CreateScanner());

        var relativePaths = results.Select(f => f.RelativePath).ToList();
        Assert.Single(relativePaths);
        Assert.Equal(Path.Combine("src", "Foo.cs"), relativePaths[0]);
    }

    [Fact]
    public async Task ScanAsync_RespectsIncludedExtensions()
    {
        WriteFile(Path.Combine(_root.FullName, "code.cs"));
        WriteFile(Path.Combine(_root.FullName, "notes.md"));
        WriteFile(Path.Combine(_root.FullName, "image.png"));
        WriteFile(Path.Combine(_root.FullName, "data.bin"));

        var options = new RepositoryScannerOptions { IncludedExtensions = [".cs", ".md"] };
        var results = await ScanAllAsync(CreateScanner(options));

        Assert.Equal(2, results.Count);
        Assert.Contains(results, f => f.RelativePath == "code.cs");
        Assert.Contains(results, f => f.RelativePath == "notes.md");
    }

    [Fact]
    public async Task ScanAsync_ExcludesGeneratedFileSuffixes()
    {
        WriteFile(Path.Combine(_root.FullName, "Foo.cs"));
        WriteFile(Path.Combine(_root.FullName, "Foo.generated.cs"));
        WriteFile(Path.Combine(_root.FullName, "Foo.Designer.cs"));
        WriteFile(Path.Combine(_root.FullName, "Foo.g.cs"));
        WriteFile(Path.Combine(_root.FullName, "Foo.g.i.cs"));

        var results = await ScanAllAsync(CreateScanner());

        Assert.Single(results);
        Assert.Equal("Foo.cs", results[0].RelativePath);
    }

    [Fact]
    public async Task ScanAsync_ReturnsCorrectRelativePathsForNestedFiles()
    {
        WriteFile(Path.Combine(_root.FullName, "src", "Services", "Order", "OrderService.cs"));

        var results = await ScanAllAsync(CreateScanner());

        Assert.Single(results);
        Assert.Equal(Path.Combine("src", "Services", "Order", "OrderService.cs"), results[0].RelativePath);
        Assert.Equal(Path.Combine(_root.FullName, "src", "Services", "Order", "OrderService.cs"), results[0].AbsolutePath);
    }

    [Fact]
    public async Task ScanAsync_PopulatesExtensionSizeAndTimestamp()
    {
        var path = Path.Combine(_root.FullName, "Foo.cs");
        WriteFile(path, "hello world");

        var results = await ScanAllAsync(CreateScanner());

        var file = Assert.Single(results);
        Assert.Equal(".cs", file.Extension);
        Assert.Equal(new FileInfo(path).Length, file.SizeBytes);
        Assert.True(file.LastModifiedUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task ScanAsync_MissingRootDirectory_ThrowsDirectoryNotFoundException()
    {
        var scanner = CreateScanner();
        var missingPath = Path.Combine(_root.FullName, "does-not-exist");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await foreach (var _ in scanner.ScanAsync(missingPath))
            {
            }
        });
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_ReturnsNoFiles()
    {
        var results = await ScanAllAsync(CreateScanner());
        Assert.Empty(results);
    }
}
