namespace CodeIntelligence.Rag.Models;

/// <summary>
/// A source-code repository that has been (or is being) indexed. Tracking the
/// git commit lets search results be pinned to the exact code version they came from.
/// </summary>
public sealed class Repository
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public string? GitCommit { get; init; }
    public IndexingStatus Status { get; init; } = IndexingStatus.Pending;
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
