using CodeIntelligence.Rag.Models;

namespace CodeIntelligence.Rag.Indexing;

public sealed record IndexingResult(
    Guid RepositoryId,
    int FilesDiscovered,
    int ChunksCreated,
    int ChunksUnchanged,
    int ChunksNew,
    int ChunksDeleted,
    int EmbeddingsGenerated,
    TimeSpan Duration,
    IndexingStatus Status);
