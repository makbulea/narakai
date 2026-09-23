# CodeIntelligence.Agent

Phase 1 of the Narakai Agent (see [`../docs/roadmap.md`](../docs/roadmap.md)): a bounded tool-calling
orchestration layer on top of [`CodeIntelligence.Rag`](../rag) and the
[`backend`](../backend) e-commerce services, so a developer can ask natural-language
questions about a repository and get an evidence-based, cited answer.

Full design, concepts, and local dev instructions: **[docs/AGENT.md](docs/AGENT.md)**.

## Layout

```
src/
  CodeIntelligence.Agent/       core library — LLM abstraction, tools, orchestration loop
  CodeIntelligence.Agent.Api/   ASP.NET Core host (POST /api/agent/chat)
tests/
  CodeIntelligence.Agent.Tests/ orchestration, tools, RAG client — unit tests, no live LLM calls
docs/
  AGENT.md                     what an Agent is, workflow, tools, security, local dev, config
```

## Quick start

```bash
cd agent
cp .env.example .env   # ANTHROPIC_API_KEY, JWT_SIGNING_KEY (same key as backend's)
dotnet build
dotnet test
dotnet run --project src/CodeIntelligence.Agent.Api
```

Requires a running `CodeIntelligence.Rag.Api` with the target repository already
indexed — see [`../rag/README.md`](../rag/README.md).

## Status

Phase 1: `search_code` + `read_file` tools, hand-written agent loop, in-memory
conversation context, Anthropic (Claude) as the LLM provider behind a swappable
`ILlmClient`. `FindReferences`/`FindCallers`/`FindImplementations`/`FindEvents` are
Phase 2 — see docs/AGENT.md's "Known Phase 1 limitations".
