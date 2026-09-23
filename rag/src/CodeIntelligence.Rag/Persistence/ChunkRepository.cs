using System.Text;
using CodeIntelligence.Rag.Models;
using Npgsql;
using Pgvector;

namespace CodeIntelligence.Rag.Persistence;

public sealed class ChunkRepository(NpgsqlDataSource dataSource) : IChunkRepository
{
    private const string SelectColumns = """
        id, repository_id, file_path, start_line, end_line, content, language, service,
        namespace, class_name, method_name, symbol_kind, content_hash, created_at, updated_at
        """;

    public async Task<IReadOnlyList<Chunk>> GetChunksByFileAsync(
        Guid repositoryId, string filePath, CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT {SelectColumns} FROM chunks WHERE repository_id = @repositoryId AND file_path = @filePath;";

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("repositoryId", repositoryId);
        command.Parameters.AddWithValue("filePath", filePath);

        var results = new List<Chunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(MapChunk(reader));
        }

        return results;
    }

    public async Task UpsertManyAsync(IReadOnlyList<ChunkWriteRecord> records, CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(connection, transaction);

        foreach (var record in records)
        {
            batch.BatchCommands.Add(BuildUpsertCommand(record));
        }

        await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static NpgsqlBatchCommand BuildUpsertCommand(ChunkWriteRecord record)
    {
        var chunk = record.Chunk;
        var batchCommand = new NpgsqlBatchCommand
        {
            CommandText = record.Embedding is not null
                ? """
                    INSERT INTO chunks (id, repository_id, file_path, start_line, end_line, content, language,
                                         service, namespace, class_name, method_name, symbol_kind, content_hash,
                                         embedding, created_at, updated_at)
                    VALUES (@id, @repositoryId, @filePath, @startLine, @endLine, @content, @language,
                            @service, @namespace, @className, @methodName, @symbolKind, @contentHash,
                            @embedding, @createdAt, @updatedAt)
                    ON CONFLICT (repository_id, file_path, start_line, end_line) DO UPDATE SET
                        content = EXCLUDED.content, language = EXCLUDED.language, service = EXCLUDED.service,
                        namespace = EXCLUDED.namespace, class_name = EXCLUDED.class_name,
                        method_name = EXCLUDED.method_name, symbol_kind = EXCLUDED.symbol_kind,
                        content_hash = EXCLUDED.content_hash, embedding = EXCLUDED.embedding,
                        updated_at = EXCLUDED.updated_at;
                    """
                : """
                    INSERT INTO chunks (id, repository_id, file_path, start_line, end_line, content, language,
                                         service, namespace, class_name, method_name, symbol_kind, content_hash,
                                         created_at, updated_at)
                    VALUES (@id, @repositoryId, @filePath, @startLine, @endLine, @content, @language,
                            @service, @namespace, @className, @methodName, @symbolKind, @contentHash,
                            @createdAt, @updatedAt)
                    ON CONFLICT (repository_id, file_path, start_line, end_line) DO UPDATE SET
                        content = EXCLUDED.content, language = EXCLUDED.language, service = EXCLUDED.service,
                        namespace = EXCLUDED.namespace, class_name = EXCLUDED.class_name,
                        method_name = EXCLUDED.method_name, symbol_kind = EXCLUDED.symbol_kind,
                        content_hash = EXCLUDED.content_hash, updated_at = EXCLUDED.updated_at;
                    """
        };

        batchCommand.Parameters.Add(new NpgsqlParameter("id", chunk.Id));
        batchCommand.Parameters.Add(new NpgsqlParameter("repositoryId", chunk.RepositoryId));
        batchCommand.Parameters.Add(new NpgsqlParameter("filePath", chunk.FilePath));
        batchCommand.Parameters.Add(new NpgsqlParameter("startLine", chunk.StartLine));
        batchCommand.Parameters.Add(new NpgsqlParameter("endLine", chunk.EndLine));
        batchCommand.Parameters.Add(new NpgsqlParameter("content", chunk.Content));
        batchCommand.Parameters.Add(new NpgsqlParameter("language", chunk.Language));
        batchCommand.Parameters.Add(new NpgsqlParameter("service", (object?)chunk.Service ?? DBNull.Value));
        batchCommand.Parameters.Add(new NpgsqlParameter("namespace", (object?)chunk.Namespace ?? DBNull.Value));
        batchCommand.Parameters.Add(new NpgsqlParameter("className", (object?)chunk.ClassName ?? DBNull.Value));
        batchCommand.Parameters.Add(new NpgsqlParameter("methodName", (object?)chunk.MethodName ?? DBNull.Value));
        batchCommand.Parameters.Add(new NpgsqlParameter("symbolKind", chunk.SymbolKind.ToString()));
        batchCommand.Parameters.Add(new NpgsqlParameter("contentHash", chunk.ContentHash));
        batchCommand.Parameters.Add(new NpgsqlParameter("createdAt", chunk.CreatedAt));
        batchCommand.Parameters.Add(new NpgsqlParameter("updatedAt", chunk.UpdatedAt));

        if (record.Embedding is not null)
        {
            batchCommand.Parameters.Add(new NpgsqlParameter("embedding", new Vector(record.Embedding)));
        }

        return batchCommand;
    }

    public async Task DeleteManyAsync(IReadOnlyList<Guid> chunkIds, CancellationToken cancellationToken = default)
    {
        if (chunkIds.Count == 0)
        {
            return;
        }

        const string sql = "DELETE FROM chunks WHERE id = ANY(@ids);";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", chunkIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteByFileAsync(Guid repositoryId, string filePath, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM chunks WHERE repository_id = @repositoryId AND file_path = @filePath;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("repositoryId", repositoryId);
        command.Parameters.AddWithValue("filePath", filePath);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> DeleteAllForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM chunks WHERE repository_id = @repositoryId;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("repositoryId", repositoryId);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountForRepositoryAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM chunks WHERE repository_id = @repositoryId;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("repositoryId", repositoryId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<string>> ListFilePathsAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT DISTINCT file_path FROM chunks WHERE repository_id = @repositoryId;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("repositoryId", repositoryId);

        var results = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async Task<IReadOnlyList<(Chunk Chunk, double Score)>> SearchByVectorAsync(
        Guid repositoryId, float[] queryEmbedding, int topK, SearchFilters? filters, DistanceMetric metric,
        CancellationToken cancellationToken = default)
    {
        var op = GetOperator(metric);
        var sql = new StringBuilder($"""
            SELECT {SelectColumns}, (embedding {op} @query) AS distance
            FROM chunks
            WHERE repository_id = @repositoryId AND embedding IS NOT NULL
            """);
        var parameters = new List<NpgsqlParameter> { new("repositoryId", repositoryId), new("query", new Vector(queryEmbedding)) };
        AppendFilters(sql, parameters, filters);
        sql.Append($" ORDER BY embedding {op} @query ASC LIMIT @topK;");
        parameters.Add(new NpgsqlParameter("topK", topK));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql.ToString(), connection);
        command.Parameters.AddRange(parameters.ToArray());

        var results = new List<(Chunk, double)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var chunk = MapChunk(reader);
            var distance = reader.GetDouble(reader.GetOrdinal("distance"));
            results.Add((chunk, ToScore(metric, distance)));
        }

        return results;
    }

    public async Task<IReadOnlyList<(Chunk Chunk, double Score)>> SearchByTextAsync(
        Guid repositoryId, string queryText, int topK, SearchFilters? filters,
        CancellationToken cancellationToken = default)
    {
        var sql = new StringBuilder($"""
            SELECT {SelectColumns}, ts_rank(content_tsv, plainto_tsquery('english', @queryText)) AS rank
            FROM chunks
            WHERE repository_id = @repositoryId AND content_tsv @@ plainto_tsquery('english', @queryText)
            """);
        var parameters = new List<NpgsqlParameter> { new("repositoryId", repositoryId), new("queryText", queryText) };
        AppendFilters(sql, parameters, filters);
        sql.Append(" ORDER BY rank DESC LIMIT @topK;");
        parameters.Add(new NpgsqlParameter("topK", topK));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql.ToString(), connection);
        command.Parameters.AddRange(parameters.ToArray());

        var results = new List<(Chunk, double)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var chunk = MapChunk(reader);
            var rank = reader.GetDouble(reader.GetOrdinal("rank"));
            results.Add((chunk, rank));
        }

        return results;
    }

    private static void AppendFilters(StringBuilder sql, List<NpgsqlParameter> parameters, SearchFilters? filters)
    {
        if (filters is null)
        {
            return;
        }

        if (filters.Language is not null)
        {
            sql.Append(" AND language = @fLanguage");
            parameters.Add(new NpgsqlParameter("fLanguage", filters.Language));
        }

        if (filters.Service is not null)
        {
            sql.Append(" AND service = @fService");
            parameters.Add(new NpgsqlParameter("fService", filters.Service));
        }

        if (filters.FilePath is not null)
        {
            sql.Append(" AND file_path = @fFilePath");
            parameters.Add(new NpgsqlParameter("fFilePath", filters.FilePath));
        }

        if (filters.Namespace is not null)
        {
            sql.Append(" AND namespace = @fNamespace");
            parameters.Add(new NpgsqlParameter("fNamespace", filters.Namespace));
        }

        if (filters.ClassName is not null)
        {
            sql.Append(" AND class_name = @fClassName");
            parameters.Add(new NpgsqlParameter("fClassName", filters.ClassName));
        }

        if (filters.MethodName is not null)
        {
            sql.Append(" AND method_name = @fMethodName");
            parameters.Add(new NpgsqlParameter("fMethodName", filters.MethodName));
        }

        if (filters.SymbolKind is not null)
        {
            sql.Append(" AND symbol_kind = @fSymbolKind");
            parameters.Add(new NpgsqlParameter("fSymbolKind", filters.SymbolKind.Value.ToString()));
        }
    }

    private static string GetOperator(DistanceMetric metric) => metric switch
    {
        DistanceMetric.Cosine => "<=>",
        DistanceMetric.InnerProduct => "<#>",
        DistanceMetric.Euclidean => "<->",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
    };

    /// <summary>
    /// Converts a raw pgvector distance into an "bigger is better" score. Cosine similarity =
    /// 1 - cosine distance. Inner product: pgvector's "&lt;#&gt;" already returns the *negative*
    /// inner product, so negating it back gives the true (raw, unnormalized) inner product.
    /// Euclidean has no natural [0,1] similarity, so the negated distance is returned (0 = identical,
    /// more negative = farther) — see docs/vector-search.md.
    /// </summary>
    private static double ToScore(DistanceMetric metric, double rawDistance) => metric switch
    {
        DistanceMetric.Cosine => 1.0 - rawDistance,
        DistanceMetric.InnerProduct => -rawDistance,
        DistanceMetric.Euclidean => -rawDistance,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
    };

    private static Chunk MapChunk(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        RepositoryId = reader.GetGuid(reader.GetOrdinal("repository_id")),
        FilePath = reader.GetString(reader.GetOrdinal("file_path")),
        StartLine = reader.GetInt32(reader.GetOrdinal("start_line")),
        EndLine = reader.GetInt32(reader.GetOrdinal("end_line")),
        Content = reader.GetString(reader.GetOrdinal("content")),
        Language = reader.GetString(reader.GetOrdinal("language")),
        Service = reader.IsDBNull(reader.GetOrdinal("service")) ? null : reader.GetString(reader.GetOrdinal("service")),
        Namespace = reader.IsDBNull(reader.GetOrdinal("namespace")) ? null : reader.GetString(reader.GetOrdinal("namespace")),
        ClassName = reader.IsDBNull(reader.GetOrdinal("class_name")) ? null : reader.GetString(reader.GetOrdinal("class_name")),
        MethodName = reader.IsDBNull(reader.GetOrdinal("method_name")) ? null : reader.GetString(reader.GetOrdinal("method_name")),
        SymbolKind = Enum.Parse<SymbolKind>(reader.GetString(reader.GetOrdinal("symbol_kind"))),
        ContentHash = reader.GetString(reader.GetOrdinal("content_hash")),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at"))
    };
}
