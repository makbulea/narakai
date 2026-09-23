using CodeIntelligence.Rag.DependencyInjection;
using CodeIntelligence.Rag.HybridSearch;
using CodeIntelligence.Rag.Indexing;
using CodeIntelligence.Rag.Models;
using CodeIntelligence.Rag.Persistence;
using CodeIntelligence.Rag.Reranking;
using CodeIntelligence.Rag.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

// Command/positional args are parsed by hand below — don't hand them to the host builder's
// own command-line config provider, which would otherwise misread flags like --topk as config overrides.
var hostBuilder = Host.CreateApplicationBuilder([]);
hostBuilder.Services.AddCodeIntelligenceRag(hostBuilder.Configuration);
using var host = hostBuilder.Build();

var schemaInitializer = host.Services.GetRequiredService<SchemaInitializer>();
await schemaInitializer.ApplyAsync();

var command = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

try
{
    return command switch
    {
        "index" => await RunIndexAsync(host.Services, rest, forceReindex: false),
        "reindex" => await RunIndexAsync(host.Services, rest, forceReindex: true),
        "search" => await RunSearchAsync(host.Services, rest),
        "clear" => await RunClearAsync(host.Services, rest),
        _ => PrintUsageAndFail(command)
    };
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static int PrintUsageAndFail(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'.");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          rag-cli index <repository-path> [--name <name>]
          rag-cli reindex <repository-path> [--name <name>]     (forces re-embedding of every chunk)
          rag-cli search <repository-id-or-name> <query> [--topk N] [--hybrid] [--rerank]
          rag-cli clear <repository-id-or-name>
        """);
}

static async Task<int> RunIndexAsync(IServiceProvider services, string[] args, bool forceReindex)
{
    if (args.Length == 0)
    {
        throw new ArgumentException("Missing <repository-path>.");
    }

    var repositoryPath = args[0];
    var name = TryGetOption(args, "--name");

    if (forceReindex)
    {
        var store = services.GetRequiredService<IRepositoryStore>();
        var existing = await store.GetByNameAsync(name ?? Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath))));
        if (existing is not null)
        {
            var chunkRepository = services.GetRequiredService<IChunkRepository>();
            var deleted = await chunkRepository.DeleteAllForRepositoryAsync(existing.Id);
            Console.WriteLine($"Cleared {deleted} existing chunks before reindexing.");
        }
    }

    var pipeline = services.GetRequiredService<IIndexingPipeline>();
    var result = await pipeline.IndexAsync(repositoryPath, name);

    Console.WriteLine($"Repository:          {result.RepositoryId}");
    Console.WriteLine($"Files discovered:    {result.FilesDiscovered}");
    Console.WriteLine($"Chunks created:       {result.ChunksCreated}");
    Console.WriteLine($"  unchanged:          {result.ChunksUnchanged}");
    Console.WriteLine($"  new/changed:        {result.ChunksNew}");
    Console.WriteLine($"  deleted (stale):    {result.ChunksDeleted}");
    Console.WriteLine($"Embeddings generated: {result.EmbeddingsGenerated}");
    Console.WriteLine($"Duration:             {result.Duration.TotalSeconds:F1}s");
    Console.WriteLine($"Status:               {result.Status}");

    return result.Status == IndexingStatus.Completed ? 0 : 1;
}

static async Task<int> RunSearchAsync(IServiceProvider services, string[] args)
{
    if (args.Length < 2)
    {
        throw new ArgumentException("Usage: search <repository-id-or-name> <query> [--topk N] [--hybrid] [--rerank]");
    }

    var repositoryStore = services.GetRequiredService<IRepositoryStore>();
    var repository = await ResolveRepositoryAsync(repositoryStore, args[0]);

    var topKOption = TryGetOption(args, "--topk");
    var topK = int.TryParse(topKOption, out var parsed) ? parsed : 5;
    var useHybrid = args.Contains("--hybrid");
    var useRerank = args.Contains("--rerank");
    var queryText = string.Join(' ', args.Skip(1).Where(a => !a.StartsWith("--") && a != topKOption));

    var query = new SearchQuery { RepositoryId = repository.Id, Query = queryText, TopK = useRerank ? Math.Max(topK, 20) : topK };

    var results = useHybrid
        ? await services.GetRequiredService<IHybridSearch>().SearchAsync(query)
        : await services.GetRequiredService<ISemanticSearch>().SearchAsync(query);

    if (useRerank)
    {
        var reranker = services.GetRequiredService<IReranker>();
        results = await reranker.RerankAsync(queryText, results, topK);
    }

    if (results.Count == 0)
    {
        Console.WriteLine("No results.");
        return 0;
    }

    foreach (var result in results)
    {
        Console.WriteLine($"[{result.Score:F3}] {result.FilePath}:{result.StartLine}-{result.EndLine}  " +
                           $"({result.Metadata.SymbolKind} {result.Metadata.ClassName}.{result.Metadata.MethodName})");
        Console.WriteLine(Indent(Truncate(result.Content, 400)));
        Console.WriteLine();
    }

    return 0;
}

static async Task<int> RunClearAsync(IServiceProvider services, string[] args)
{
    if (args.Length == 0)
    {
        throw new ArgumentException("Usage: clear <repository-id-or-name>");
    }

    var repositoryStore = services.GetRequiredService<IRepositoryStore>();
    var repository = await ResolveRepositoryAsync(repositoryStore, args[0]);

    var chunkRepository = services.GetRequiredService<IChunkRepository>();
    var deleted = await chunkRepository.DeleteAllForRepositoryAsync(repository.Id);
    await repositoryStore.DeleteAsync(repository.Id);

    Console.WriteLine($"Deleted repository '{repository.Name}' and {deleted} chunks.");
    return 0;
}

static async Task<Repository> ResolveRepositoryAsync(IRepositoryStore store, string idOrName)
{
    var repository = Guid.TryParse(idOrName, out var id)
        ? await store.GetByIdAsync(id)
        : await store.GetByNameAsync(idOrName);

    return repository ?? throw new ArgumentException($"Repository '{idOrName}' not found.");
}

static string? TryGetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string Truncate(string text, int maxLength) =>
    text.Length <= maxLength ? text : text[..maxLength] + "\n... (truncated)";

static string Indent(string text) =>
    string.Join('\n', text.Split('\n').Select(line => "    " + line));
