using CodeIntelligence.Rag.Chunking;
using CodeIntelligence.Rag.Chunking.CSharp;
using CodeIntelligence.Rag.Chunking.Generic;
using CodeIntelligence.Rag.Embeddings;
using CodeIntelligence.Rag.Embeddings.Ollama;
using CodeIntelligence.Rag.Embeddings.Voyage;
using CodeIntelligence.Rag.HybridSearch;
using CodeIntelligence.Rag.Indexing;
using CodeIntelligence.Rag.Persistence;
using CodeIntelligence.Rag.Reranking;
using CodeIntelligence.Rag.Retrieval;
using CodeIntelligence.Rag.Scanning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CodeIntelligence.Rag.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires up the full RAG pipeline: scanning, chunking, embeddings, persistence, retrieval,
    /// hybrid search and reranking. The caller (API host or CLI) still owns logging setup and
    /// applying the schema (see <see cref="SchemaInitializer"/>) — this only registers services.
    /// </summary>
    public static IServiceCollection AddCodeIntelligenceRag(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RepositoryScannerOptions>(configuration.GetSection(RepositoryScannerOptions.SectionName));
        services.Configure<ChunkingOptions>(configuration.GetSection(ChunkingOptions.SectionName));
        services.Configure<HybridSearchOptions>(configuration.GetSection(HybridSearchOptions.SectionName));
        services.Configure<RerankerOptions>(configuration.GetSection(RerankerOptions.SectionName));

        services.AddOptions<PostgresOptions>()
            .Bind(configuration.GetSection(PostgresOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    options.ConnectionString = Environment.GetEnvironmentVariable("RAG_POSTGRES_CONNECTIONSTRING") ?? string.Empty;
                }
            })
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString),
                "PostgreSQL connection string is missing. Set Rag:Postgres:ConnectionString or the RAG_POSTGRES_CONNECTIONSTRING environment variable.");

        var embeddingProviderOptions = configuration.GetSection(EmbeddingProviderOptions.SectionName)
            .Get<EmbeddingProviderOptions>() ?? new EmbeddingProviderOptions();
        var useOllama = string.Equals(embeddingProviderOptions.Provider, "Ollama", StringComparison.OrdinalIgnoreCase);

        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));

        services.AddOptions<VoyageOptions>()
            .Bind(configuration.GetSection(VoyageOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = Environment.GetEnvironmentVariable("VOYAGE_API_KEY") ?? string.Empty;
                }
            })
            // Voyage's key is only required when it's the active provider — Ollama needs none.
            .Validate(o => useOllama || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Voyage API key is missing. Set Rag:Embeddings:Voyage:ApiKey or the VOYAGE_API_KEY environment variable.");

        services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PostgresOptions>>().Value;
            return NpgsqlDataSourceFactory.Create(options.ConnectionString);
        });
        services.AddSingleton<SchemaInitializer>();

        services.AddSingleton<IRepositoryScanner, RepositoryScanner>();

        services.AddSingleton<IChunker, CSharpChunker>();
        services.AddSingleton<IChunker, GenericTextChunker>();
        services.AddSingleton<IChunkerFactory, ChunkerFactory>();

        // Exactly one IEmbeddingProvider is wired, chosen by Rag:Embeddings:Provider ("Voyage",
        // the default, or "Ollama") — never both, so there is no ambiguity about which one
        // IIndexingPipeline/ISemanticSearch resolve. See docs/embeddings.md for the trade-offs.
        if (useOllama)
        {
            services.AddHttpClient<IEmbeddingProvider, OllamaEmbeddingProvider>((sp, client) =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OllamaOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
            });
        }
        else
        {
            services.AddHttpClient<IEmbeddingProvider, VoyageEmbeddingProvider>((sp, client) =>
            {
                var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<VoyageOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
            });
        }

        services.AddSingleton<IRepositoryStore, RepositoryStore>();
        services.AddSingleton<IChunkRepository, ChunkRepository>();

        services.AddSingleton<ISemanticSearch, SemanticSearchService>();
        services.AddSingleton<IHybridSearch, HybridSearchService>();
        services.AddSingleton<IReranker, SimpleLexicalReranker>();

        services.AddSingleton<IIndexingPipeline, IndexingPipeline>();

        return services;
    }
}
