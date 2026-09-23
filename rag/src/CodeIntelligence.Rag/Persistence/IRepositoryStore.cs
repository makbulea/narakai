using CodeIntelligence.Rag.Models;

namespace CodeIntelligence.Rag.Persistence;

public interface IRepositoryStore
{
    /// <summary>Finds a repository by name, or creates it (status Pending) if none exists yet.</summary>
    Task<Repository> GetOrCreateAsync(string name, string rootPath, string? gitCommit, CancellationToken cancellationToken = default);

    Task<Repository?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Repository?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Repository>> ListAsync(CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid id, IndexingStatus status, string? gitCommit, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
