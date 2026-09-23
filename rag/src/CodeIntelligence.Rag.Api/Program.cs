using CodeIntelligence.Rag.Api.Contracts;
using CodeIntelligence.Rag.DependencyInjection;
using CodeIntelligence.Rag.HybridSearch;
using CodeIntelligence.Rag.Indexing;
using CodeIntelligence.Rag.Persistence;
using CodeIntelligence.Rag.Reranking;
using CodeIntelligence.Rag.Retrieval;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddCodeIntelligenceRag(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Schema is applied on startup so a fresh `docker compose up` + `dotnet run` needs no manual
// migration step. Every statement in schema.sql is idempotent, so this is safe on every boot.
using (var scope = app.Services.CreateScope())
{
    var schemaInitializer = scope.ServiceProvider.GetRequiredService<SchemaInitializer>();
    await schemaInitializer.ApplyAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");

app.MapPost("/repositories/index", async (
        IndexRepositoryRequest request, IIndexingPipeline pipeline, CancellationToken cancellationToken) =>
    {
        var result = await pipeline.IndexAsync(request.RepositoryPath, request.RepositoryName, cancellationToken);
        return Results.Ok(result);
    })
    .WithName("IndexRepository");

app.MapGet("/repositories", async (IRepositoryStore store, CancellationToken cancellationToken) =>
    {
        var repositories = await store.ListAsync(cancellationToken);
        return Results.Ok(repositories.Select(RepositoryResponse.FromModel));
    })
    .WithName("ListRepositories");

app.MapGet("/repositories/{id:guid}", async (Guid id, IRepositoryStore store, CancellationToken cancellationToken) =>
    {
        var repository = await store.GetByIdAsync(id, cancellationToken);
        return repository is null ? Results.NotFound() : Results.Ok(RepositoryResponse.FromModel(repository));
    })
    .WithName("GetRepository");

app.MapPost("/search", async (
        SearchRequest request, ISemanticSearch search, IReranker reranker, CancellationToken cancellationToken) =>
    {
        var query = ToSearchQuery(request);
        var results = await search.SearchAsync(query, cancellationToken);
        if (request.Rerank)
        {
            results = await reranker.RerankAsync(request.Query, results, request.TopK, cancellationToken);
        }

        return Results.Ok(results.Select(SearchResultResponse.FromModel));
    })
    .WithName("Search");

app.MapPost("/search/hybrid", async (
        SearchRequest request, IHybridSearch search, IReranker reranker, CancellationToken cancellationToken) =>
    {
        var query = ToSearchQuery(request);
        var results = await search.SearchAsync(query, cancellationToken);
        if (request.Rerank)
        {
            results = await reranker.RerankAsync(request.Query, results, request.TopK, cancellationToken);
        }

        return Results.Ok(results.Select(SearchResultResponse.FromModel));
    })
    .WithName("HybridSearch");

app.Run();

static CodeIntelligence.Rag.Models.SearchQuery ToSearchQuery(SearchRequest request) => new()
{
    RepositoryId = request.RepositoryId,
    Query = request.Query,
    TopK = request.TopK,
    Filters = request.Filters
};

// Exposed for WebApplicationFactory-based integration testing.
public partial class Program;
