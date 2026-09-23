using CodeIntelligence.Rag.Embeddings;
using CodeIntelligence.Rag.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CodeIntelligence.Rag.IntegrationTests;

/// <summary>
/// Stands in for whichever real IEmbeddingProvider is configured — SchemaInitializer only
/// reads .Dimension to size the chunks.embedding column. 1024 matches
/// ChunkRepositoryIntegrationTests.VectorDimension.
/// </summary>
internal sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    public string ModelName => "fake";
    public int Dimension => 1024;

    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, EmbeddingInputType inputType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Integration tests exercise ChunkRepository directly and never call EmbedAsync.");
}

/// <summary>
/// Spins up a real pgvector/pgvector:pg16 container (the same image docker-compose.yml uses)
/// and applies the embedded schema, so integration tests exercise the actual SQL — vector
/// search operators, full-text ranking, unique-constraint upserts — instead of a mocked
/// IChunkRepository. Shared across a test class's methods via IClassFixture so the container
/// starts once, not once per test.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("pgvector/pgvector:pg16")
            .WithDatabase("code_intelligence_rag")
            .WithUsername("rag")
            .WithPassword("rag")
            .Build();

        await _container.StartAsync();

        DataSource = NpgsqlDataSourceFactory.Create(_container.GetConnectionString());

        var schemaInitializer = new SchemaInitializer(DataSource, new FakeEmbeddingProvider(), NullLogger<SchemaInitializer>.Instance);
        await schemaInitializer.ApplyAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
