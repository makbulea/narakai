namespace CodeIntelligence.Rag.Persistence;

/// <summary>Configuration for the PostgreSQL/pgvector connection. Bind from "Rag:Postgres".</summary>
public sealed class PostgresOptions
{
    public const string SectionName = "Rag:Postgres";

    /// <summary>
    /// Npgsql connection string. Prefer setting this via the RAG_POSTGRES_CONNECTIONSTRING
    /// environment variable or user secrets in development — never commit real credentials.
    /// Not "required init": DI wiring falls back to that environment variable via IOptions
    /// PostConfigure, which needs a settable property. Missing/empty is validated at service
    /// registration time — see ServiceCollectionExtensions.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
