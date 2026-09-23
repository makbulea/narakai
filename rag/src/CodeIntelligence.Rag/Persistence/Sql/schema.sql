-- CodeIntelligence.Rag schema. Applied idempotently by SchemaInitializer on startup / `rag-cli index`.
-- See docs/vector-search.md for the indexing choices made here.

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS repositories (
    id UUID PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    root_path TEXT NOT NULL,
    git_commit TEXT NULL,
    status TEXT NOT NULL DEFAULT 'Pending',
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS chunks (
    id UUID PRIMARY KEY,
    repository_id UUID NOT NULL REFERENCES repositories (id) ON DELETE CASCADE,
    file_path TEXT NOT NULL,
    start_line INT NOT NULL,
    end_line INT NOT NULL,
    content TEXT NOT NULL,
    language TEXT NOT NULL,
    service TEXT NULL,
    namespace TEXT NULL,
    class_name TEXT NULL,
    method_name TEXT NULL,
    symbol_kind TEXT NOT NULL,
    content_hash CHAR(64) NOT NULL,
    embedding VECTOR({{EMBEDDING_DIMENSION}}) NULL,
    content_tsv TSVECTOR GENERATED ALWAYS AS (to_tsvector('english', content)) STORED,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    UNIQUE (repository_id, file_path, start_line, end_line)
);

-- Every search is scoped to one repository, so repository_id leads every composite index.
CREATE INDEX IF NOT EXISTS idx_chunks_repository ON chunks (repository_id);
CREATE INDEX IF NOT EXISTS idx_chunks_file_path ON chunks (repository_id, file_path);
CREATE INDEX IF NOT EXISTS idx_chunks_filters
    ON chunks (repository_id, language, service, namespace, class_name, symbol_kind);

-- Lexical side of hybrid search.
CREATE INDEX IF NOT EXISTS idx_chunks_content_tsv ON chunks USING GIN (content_tsv);

-- Vector side. HNSW over IVFFlat: HNSW needs no training step (IVFFlat's list count has to be
-- chosen from an existing row count, which is awkward on a table indexed incrementally) and
-- gives better recall/latency at query time at the cost of slower, more memory-hungry builds —
-- an acceptable trade for a code-search corpus that's written far less often than it's queried.
-- cosine ops mirror the default distance metric used by SemanticSearchService.
CREATE INDEX IF NOT EXISTS idx_chunks_embedding_hnsw
    ON chunks USING hnsw (embedding vector_cosine_ops);
