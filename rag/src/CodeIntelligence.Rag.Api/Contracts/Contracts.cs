using CodeIntelligence.Rag.Models;

namespace CodeIntelligence.Rag.Api.Contracts;

public sealed record IndexRepositoryRequest(string RepositoryPath, string? RepositoryName);

public sealed record SearchRequest(
    Guid RepositoryId,
    string Query,
    int TopK = 5,
    SearchFilters? Filters = null,
    bool Rerank = false);

public sealed record RepositoryResponse(
    Guid Id, string Name, string RootPath, string? GitCommit, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static RepositoryResponse FromModel(Repository repository) => new(
        repository.Id, repository.Name, repository.RootPath, repository.GitCommit,
        repository.Status.ToString(), repository.CreatedAt, repository.UpdatedAt);
}

public sealed record SearchResultResponse(
    Guid ChunkId, string FilePath, int StartLine, int EndLine, string Content, double Score, ChunkMetadata Metadata)
{
    public static SearchResultResponse FromModel(SearchResult result) => new(
        result.ChunkId, result.FilePath, result.StartLine, result.EndLine, result.Content, result.Score, result.Metadata);
}
