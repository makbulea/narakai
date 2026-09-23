using Npgsql;

namespace CodeIntelligence.Rag.Persistence;

/// <summary>
/// Builds the shared <see cref="NpgsqlDataSource"/> (Npgsql's pooled connection factory) with
/// the pgvector type mapping enabled, so <c>Pgvector.Vector</c> can be used directly as a
/// parameter/column type without manual (de)serialization.
/// </summary>
public static class NpgsqlDataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        return builder.Build();
    }
}
