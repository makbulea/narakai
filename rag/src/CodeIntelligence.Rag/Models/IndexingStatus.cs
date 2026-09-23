namespace CodeIntelligence.Rag.Models;

/// <summary>
/// Lifecycle state of a repository indexing run. Persisted so a crashed or
/// interrupted run can be observed and resumed rather than leaving the index
/// in a silently half-written state.
/// </summary>
public enum IndexingStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
