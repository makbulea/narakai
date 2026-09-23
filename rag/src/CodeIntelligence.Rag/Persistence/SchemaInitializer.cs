using System.Reflection;
using CodeIntelligence.Rag.Embeddings;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CodeIntelligence.Rag.Persistence;

/// <summary>
/// Applies the embedded schema.sql. Safe to call on every startup — every statement is
/// idempotent (IF NOT EXISTS). The chunks.embedding column's vector width is filled in
/// from the active <see cref="IEmbeddingProvider"/> at startup, so it always matches
/// whichever provider is configured (Voyage: 1024, Ollama/nomic-embed-text: 768, etc.).
///
/// Note this only affects a genuinely new database: CREATE TABLE IF NOT EXISTS does not
/// widen/narrow an existing column. Switching providers to one with a different
/// dimension after data already exists requires resetting the database (e.g. `docker
/// compose down -v`) — a single column can only hold vectors of one fixed size, so old
/// and new embeddings from different-dimension providers cannot coexist in one table.
/// </summary>
public sealed class SchemaInitializer(NpgsqlDataSource dataSource, IEmbeddingProvider embeddingProvider, ILogger<SchemaInitializer> logger)
{
    private static readonly string SchemaTemplate = ReadEmbeddedSchema();

    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var schemaSql = SchemaTemplate.Replace("{{EMBEDDING_DIMENSION}}", embeddingProvider.Dimension.ToString());

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(schemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // On a genuinely fresh database, `CREATE EXTENSION IF NOT EXISTS vector` above is what
        // makes the "vector" type exist for the first time. NpgsqlDataSource resolves and caches
        // its type catalog (including extension types like pgvector's) from the database the
        // first time it's used, and that cache otherwise never refreshes on its own — so without
        // this reload, every later query that binds a Pgvector.Vector parameter throws
        // "Cannot resolve 'vector' to a fully qualified datatype name" for the lifetime of this
        // NpgsqlDataSource, even though the type now exists.
        await dataSource.ReloadTypesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Database schema applied/verified");
    }

    private static string ReadEmbeddedSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "CodeIntelligence.Rag.Persistence.Sql.schema.sql";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
