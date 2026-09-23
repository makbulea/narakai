using CodeIntelligence.Rag.Models;
using Npgsql;

namespace CodeIntelligence.Rag.Persistence;

public sealed class RepositoryStore(NpgsqlDataSource dataSource) : IRepositoryStore
{
    public async Task<Repository> GetOrCreateAsync(
        string name, string rootPath, string? gitCommit, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO repositories (id, name, root_path, git_commit, status, created_at, updated_at)
            VALUES (@id, @name, @rootPath, @gitCommit, 'Pending', @now, @now)
            ON CONFLICT (name) DO UPDATE SET root_path = EXCLUDED.root_path, updated_at = repositories.updated_at
            RETURNING id, name, root_path, git_commit, status, created_at, updated_at;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        var now = DateTimeOffset.UtcNow;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("rootPath", rootPath);
        command.Parameters.AddWithValue("gitCommit", (object?)gitCommit ?? DBNull.Value);
        command.Parameters.AddWithValue("now", now);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return Map(reader);
    }

    public async Task<Repository?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, root_path, git_commit, status, created_at, updated_at
            FROM repositories WHERE id = @id;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<Repository?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, root_path, git_commit, status, created_at, updated_at
            FROM repositories WHERE name = @name;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("name", name);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Repository>> ListAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT id, name, root_path, git_commit, status, created_at, updated_at
            FROM repositories ORDER BY created_at DESC;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);

        var results = new List<Repository>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task UpdateStatusAsync(
        Guid id, IndexingStatus status, string? gitCommit, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE repositories
            SET status = @status, git_commit = COALESCE(@gitCommit, git_commit), updated_at = @now
            WHERE id = @id;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("status", status.ToString());
        command.Parameters.AddWithValue("gitCommit", (object?)gitCommit ?? DBNull.Value);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM repositories WHERE id = @id;";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Repository Map(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        RootPath = reader.GetString(2),
        GitCommit = reader.IsDBNull(3) ? null : reader.GetString(3),
        Status = Enum.Parse<IndexingStatus>(reader.GetString(4)),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(6)
    };
}
