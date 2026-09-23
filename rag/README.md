# CodeIntelligence.Rag

A retrieval-augmented generation (RAG) pipeline for indexing and searching source code
repositories. It scans a repository, splits it into code-aware chunks, embeds those chunks
with a code-tuned embedding model, and stores them in PostgreSQL/pgvector so they can be
retrieved later by semantic similarity, lexical (full-text) matching, or a blend of both.

This project is the retrieval layer only. It has no notion of an agent, a tool-calling loop,
or autonomous reasoning — see [Agent integration](#agent-integration) below.

## Architecture

```mermaid
flowchart LR
    subgraph Indexing
        Scanner[RepositoryScanner] --> Chunker[IChunker\nCSharpChunker / GenericTextChunker]
        Chunker --> Hasher[ContentHasher]
        Hasher --> Diff{Unchanged?}
        Diff -- yes: reuse embedding --> Store[(Postgres / pgvector)]
        Diff -- no: new or changed --> Embedder[IEmbeddingProvider\nVoyageEmbeddingProvider]
        Embedder --> Store
    end

    subgraph Query
        Query[Search query] --> QEmbed[IEmbeddingProvider]
        QEmbed --> Semantic[ISemanticSearch]
        Query --> Hybrid[IHybridSearch]
        Semantic --> Store
        Hybrid --> Store
        Semantic --> Reranker[IReranker]
        Hybrid --> Reranker
        Reranker --> Results[SearchResult list]
    end
```

`IndexingPipeline` drives the top half: scan the repository tree, chunk each file, hash each
chunk's content, diff against what's already stored for that file, embed only new/changed
chunks, and persist. `SemanticSearchService` and `HybridSearchService` drive the bottom half:
embed the query, search pgvector (and, for hybrid, PostgreSQL full-text) and optionally hand
the candidates to a reranker before returning results.

See [docs/architecture.md](docs/architecture.md) for the full component breakdown and how
`ServiceCollectionExtensions` wires it together.

## Quickstart

1. Start PostgreSQL with the pgvector extension:

   ```bash
   docker compose up -d
   ```

   This starts `pgvector/pgvector:pg16` on `localhost:5433` (database `code_intelligence_rag`,
   user/password `rag`/`rag` — see `docker-compose.yml`).

2. Copy `.env.example` to `.env` and set your Voyage AI API key:

   ```bash
   cp .env.example .env
   # edit .env: set VOYAGE_API_KEY=<your key from https://dashboard.voyageai.com/>
   ```

   Export the values in `.env` into your shell (or your IDE's run configuration) before
   running the CLI or API — `RAG_POSTGRES_CONNECTIONSTRING` and `VOYAGE_API_KEY` are read
   directly from the environment as a fallback when the corresponding `Rag:*` configuration
   section is empty (see `ServiceCollectionExtensions`).

3. Index a repository with the CLI:

   ```bash
   dotnet run --project src/CodeIntelligence.Rag.Cli -- index /path/to/repo --name my-repo
   ```

   The schema (`Persistence/Sql/schema.sql`) is applied automatically on startup — no
   separate migration step. Re-running `index` on an unchanged tree embeds nothing new;
   `reindex` forces every chunk to be re-embedded.

4. Search it:

   ```bash
   dotnet run --project src/CodeIntelligence.Rag.Cli -- search my-repo "parse retry backoff" --topk 5 --hybrid --rerank
   ```

   CLI usage in full (from `Program.cs`):

   ```
   rag-cli index <repository-path> [--name <name>]
   rag-cli reindex <repository-path> [--name <name>]     (forces re-embedding of every chunk)
   rag-cli search <repository-id-or-name> <query> [--topk N] [--hybrid] [--rerank]
   rag-cli clear <repository-id-or-name>
   ```

Alternatively, run the API host (`dotnet run --project src/CodeIntelligence.Rag.Api`) and
drive it over HTTP instead of the CLI.

## API

`CodeIntelligence.Rag.Api` (`Program.cs`) exposes:

| Method | Route | Purpose |
|---|---|---|
| GET | `/health` | Liveness check |
| POST | `/repositories/index` | Index (or re-index) a repository by local path |
| GET | `/repositories` | List all known repositories |
| GET | `/repositories/{id}` | Get one repository by id |
| POST | `/search` | Pure semantic (vector) search, optional rerank |
| POST | `/search/hybrid` | Semantic + lexical hybrid search, optional rerank |

Request/response shapes are plain records in `src/CodeIntelligence.Rag.Api/Contracts/Contracts.cs`
(`IndexRepositoryRequest`, `SearchRequest`, `RepositoryResponse`, `SearchResultResponse`). Both
search endpoints accept `{ repositoryId, query, topK, filters, rerank }` and, when `rerank` is
true, run the retrieved candidates through `IReranker` before returning them.

## Known limitations

- **Only C# has a language-aware chunker.** `CSharpChunker` uses Roslyn to split by symbol
  (method, constructor, property, indexer, event, plus one combined chunk for fields).
  Every other included extension (`.json`, `.yaml`, `.yml`, `.md`, `.sql`, `.xml`, `.csproj`)
  goes through `GenericTextChunker`, which is whole-file-or-line-split with no structural
  awareness. See [docs/chunking.md](docs/chunking.md).
- **`SimpleLexicalReranker` is not a real ML reranker.** It's a deterministic blend of the
  retrieval score and lexical token overlap — zero latency/cost and fully testable, but not
  a cross-encoder. `IReranker` is the seam for plugging one in later. See
  [docs/reranking.md](docs/reranking.md).
- **No authentication on the API.** `/repositories/index`, `/search`, etc. are open; anyone
  who can reach the host can index arbitrary local paths or query any repository's index.
  Suitable for local/internal use, not as-is for a multi-tenant or internet-facing deployment.
- **Single-node Postgres.** `docker-compose.yml` runs one pgvector container with a local
  volume — no replication, backup, or HA story is included.
- **Service inference is convention-based.** `Chunk.Service` is only populated when a file
  path contains a `Services/<Name>/...` segment (see `IndexingPipeline.InferService`); it's
  `null`, and the `service` filter simply never matches, for repositories that don't follow
  that layout.
- **`git rev-parse HEAD` is best-effort.** If `git` isn't installed or the path isn't a git
  repository, `Repository.GitCommit` is left `null` rather than failing the index run.

## Agent integration

This project does not and should not implement agent logic — no tool-calling loop, no
planning, no autonomous multi-step reasoning, no MCP server. It provides one capability:
turn a natural-language (or identifier) query into ranked, cited code snippets.

A future "Code Intelligence Agent" project is expected to consume this one as a tool, either:

- **Over HTTP**, calling `POST /search` or `POST /search/hybrid` on the API host, or
- **In-process**, if the agent host and this library run in the same process, by depending
  on `ISemanticSearch` / `IHybridSearch` / `IReranker` directly via DI
  (`AddCodeIntelligenceRag`).

Either way the agent gets back `SearchResult` records — `filePath`, `startLine`/`endLine`,
`content`, `score`, and `metadata` (language, service, namespace, class, method, symbol kind)
— everything needed to cite and reason about the code, and nothing that presumes what the
agent will do with it. See [docs/agent-integration.md](docs/agent-integration.md) for the
full boundary statement.

## Documentation

- [docs/architecture.md](docs/architecture.md) — component map and DI wiring
- [docs/indexing.md](docs/indexing.md) — the end-to-end indexing pipeline
- [docs/chunking.md](docs/chunking.md) — chunking strategy and rationale
- [docs/embeddings.md](docs/embeddings.md) — embedding provider abstraction, Voyage AI, retries
- [docs/vector-search.md](docs/vector-search.md) — pgvector schema, indexes, distance metrics
- [docs/hybrid-search.md](docs/hybrid-search.md) — semantic + lexical combination
- [docs/reranking.md](docs/reranking.md) — the retrieve-then-rerank pipeline
- [docs/incremental-indexing.md](docs/incremental-indexing.md) — content hashing, idempotency
- [docs/agent-integration.md](docs/agent-integration.md) — the integration boundary with the future agent project
