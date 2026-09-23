# Narakai — Code Intelligence Agent

Narakai is an AI agent that answers questions about a source code repository —
*"How does customer creation work?"*, *"Which services publish `OrderCreated`?"*,
*"Where is the refresh token generated?"* — by **researching the code first** and
answering from what it actually found, with file and line citations.

It is built bottom-up in three layers, each of which is a standalone .NET solution:

| Layer | Directory | What it is |
|---|---|---|
| **Backend** | [`backend/`](backend/) | A realistic .NET 10 e-commerce microservice system. This is the *target* the agent analyzes — a codebase big enough that questions about it are non-trivial. |
| **RAG** | [`rag/`](rag/) | The retrieval layer: scans a repository, chunks it in a code-aware way, embeds the chunks, stores them in PostgreSQL/pgvector, and serves semantic + hybrid search. |
| **Agent** | [`agent/`](agent/) | The reasoning layer: a bounded tool-calling loop on top of RAG. The LLM decides *which* tool to call and *when it has seen enough*, instead of answering from a single search result. |

```
                 "Who calls CustomerService.CreateAsync?"
                                 │
                                 ▼
                    ┌────────────────────────┐
                    │  Agent  (agent/)       │   bounded loop:
                    │  LLM ⇄ tools           │   think → call tool → observe → repeat
                    └───────────┬────────────┘
                     search_code│read_file
                                ▼
                    ┌────────────────────────┐
                    │  RAG    (rag/)         │   semantic + hybrid search
                    │  pgvector + HNSW       │   over code-aware chunks
                    └───────────┬────────────┘
                                ▼
                    ┌────────────────────────┐
                    │  Indexed repository    │   e.g. backend/
                    └────────────────────────┘
```

---

## Why this project exists

Narakai is a **learning project**. The goal is not only a working system, but
understanding *Agentic AI* by implementing each concept in real code rather than
reading about it:

LLM abstraction · prompt engineering · embeddings · vector databases · RAG ·
semantic search · hybrid search · reranking · **tool calling** · **agent loop** ·
skills · memory · MCP · agentic workflows · evaluation · observability

Because of that, the code optimizes for being *read*: explicit architecture, small
incremental changes, and documentation that explains **why** a decision was made —
not just what the code does.

The central question the project is built around:

> **What makes an agent different from a normal LLM application?**
>
> A normal RAG application runs a fixed pipeline: *query → retrieve → answer.*
> An agent runs a **loop**: the model chooses a tool, sees the result, decides
> whether that was enough, and only then answers. Narakai implements that loop
> by hand (`AgentOrchestrator`) instead of hiding it behind an SDK, so the
> mechanism is visible.

---

## Current status

| Phase | Scope | Status |
|---|---|---|
| P0 | Sample codebase to analyze (`backend/`) | ✅ Done |
| P1 | RAG indexing + retrieval pipeline (`rag/`) | ✅ Done |
| P2 | Agent orchestration loop, 2 tools (`agent/`) | ✅ Done |
| P3 | CodeGraph + structural tools (`find_callers`, …) | ⏸️ Deferred |
| P4 | Skills layer | ⬅️ Next |
| P5–P10 | Persistent memory, reranking, evaluation, MCP, workflows, observability | ⬜ Planned |

Full plan and rationale: [`docs/roadmap.md`](docs/roadmap.md).

### What works today

- Incremental indexing of a repository: only new or changed chunks are re-embedded
  (content hashing), so re-indexing is cheap.
- Roslyn-based, symbol-aware chunking for C# — a chunk is a class or a method, not
  an arbitrary N-token window.
- Semantic search (pgvector, HNSW), lexical search (PostgreSQL full-text) and a
  hybrid blend of the two, with an optional rerank pass.
- An agent loop with `search_code` and `read_file`, bounded by max iterations, a
  total timeout and a per-tool timeout — never an unbounded loop.
- Grounded answers with citations built from real retrieval metadata (file path,
  line range, symbol), and an explicit *"the repository does not contain enough
  information"* when evidence is missing.

### What does not work yet

Structural questions (*"who calls this method?"*, *"which classes implement this
interface?"*) need a call/reference graph that does not exist yet — that is P3.
Memory is in-process only, so conversations do not survive a restart (P5).

---

## Technology

- **.NET 10 / C#**, ASP.NET Core
- **PostgreSQL + pgvector** (HNSW index) for vector storage
- **Roslyn** for syntax-aware C# analysis and chunking
- **Voyage AI** embeddings behind `IEmbeddingProvider`
- **Anthropic Claude** behind `ILlmClient`

AI providers are isolated behind interfaces on purpose: no provider name appears in
the orchestration, retrieval or tool code, so a provider can be swapped without
touching the agent.

---

## Getting started

Each layer has its own README with a full quickstart. The short path, from nothing
to a question answered:

```bash
# 1. Retrieval layer: start pgvector, configure keys, index a repository
cd rag
docker compose up -d
cp .env.example .env            # set VOYAGE_API_KEY
dotnet run --project src/CodeIntelligence.Rag.Cli -- index ../backend --name backend
dotnet run --project src/CodeIntelligence.Rag.Api        # http://localhost:5151

# 2. Agent layer: point it at the RAG API and ask
cd ../agent
cp .env.example .env            # set ANTHROPIC_API_KEY, JWT_SIGNING_KEY
dotnet run --project src/CodeIntelligence.Agent.Api      # POST /api/agent/chat
```

```bash
curl -X POST http://localhost:5107/api/agent/chat \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer <token>' \
  -d '{"repositoryId":"<repo-id>","message":"How does customer creation work?"}'
```

Per-layer instructions:
[`backend/README.md`](backend/README.md) ·
[`rag/README.md`](rag/README.md) ·
[`agent/README.md`](agent/README.md)

---

## Documentation

| | |
|---|---|
| [`docs/roadmap.md`](docs/roadmap.md) | Phases, what is done, what is next, and why in that order |
| [`agent/docs/AGENT.md`](agent/docs/AGENT.md) | What an agent is, the loop, tools, prompts, security |
| [`rag/docs/`](rag/docs/) | Chunking, embeddings, vector search, hybrid search, reranking, incremental indexing |
| [`backend/docs/`](backend/docs/) | Order flow, outbox, idempotency, events, resilience |

---

## Design rules the code follows

- **Read-only by default.** The agent can search, read files and inspect symbols.
  It cannot delete, deploy, or run shell commands.
- **Bounded execution.** Every loop has a maximum iteration count, a timeout and a
  context size limit.
- **No invented evidence.** No made-up files, line numbers, code or configuration
  values. Citations come from actual retrieval metadata.
- **No private reasoning in logs.** Observability records operational events —
  selected tool, arguments, result summary, iteration, latency, tokens, errors.
- **Secrets stay out of the repository.** Only `.env.example` templates are tracked.

---

## Repository layout

```
backend/   e-commerce microservices — the codebase under analysis
rag/       indexing + retrieval (chunking, embeddings, pgvector, search)
agent/     LLM abstraction, tools, orchestration loop, HTTP API
docs/      roadmap and cross-cutting documentation
```
