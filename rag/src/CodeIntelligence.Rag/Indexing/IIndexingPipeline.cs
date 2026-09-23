namespace CodeIntelligence.Rag.Indexing;

/// <summary>
/// Orchestrates the full indexing flow: scan → chunk → hash → diff against what's already
/// stored → embed only new/changed chunks → persist. Idempotent — running it twice on an
/// unchanged repository produces no duplicate chunks and no new embedding calls. See
/// docs/indexing.md and docs/incremental-indexing.md.
/// </summary>
public interface IIndexingPipeline
{
    Task<IndexingResult> IndexAsync(
        string repositoryPath, string? repositoryName = null, CancellationToken cancellationToken = default);
}
