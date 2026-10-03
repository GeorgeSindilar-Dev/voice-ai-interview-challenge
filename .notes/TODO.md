# TODO

Research items to start on command. Findings feed back into [CLAUDE.md](../CLAUDE.md).

## Research

- [x] **Coding best practices for .NET (C#, ASP.NET Core).** Done 2026-10-03:
  [docs/archive/overnight/research/dotnet-best-practices.md](docs/archive/overnight/research/dotnet-best-practices.md);
  rules added to CLAUDE.md rule 1. Current language standards,
  clean code, SOLID/OOP, and the design patterns that fit this project (state
  machine for the recovery flow, adapters for the issuer/ticket APIs, ports for
  telephony and the voice model). Output: a concise rule set added to CLAUDE.md.
- [x] **Voice agent guardrails (HIGH PRIORITY).** Done 2026-10-03:
  [docs/archive/overnight/research/guardrails-research.md](docs/archive/overnight/research/guardrails-research.md);
  rules added to CLAUDE.md rule 3. Refines
  [docs/planning/04-guardrails-plan.md](docs/planning/04-guardrails-plan.md). How other agents doing similar work (IT help desk,
  account recovery, banking/telecom IVR assistants) keep the model on task: scope
  restriction, prompt-injection and jailbreak resistance, tool allow-listing and
  backend authorization, refusal/redirect phrasing, secret non-repetition, Azure AI
  Content Safety / Prompt Shields, and how to test guardrails. Output: a guardrail
  design and test list for this agent.
- [x] **Transcript storage and redaction.** Done 2026-10-03:
  [docs/archive/overnight/research/transcripts-research.md](docs/archive/overnight/research/transcripts-research.md);
  rules added to CLAUDE.md rule 4. How to save conversation transcripts to
  Azure Blob Storage safely: masking verification codes and spoken passwords,
  Azure AI Language PII redaction versus our own masking, private container access
  with managed identity, and lifecycle/retention rules. Output: a transcript
  design that fits the challenge's "no secrets in transcripts or logs" rules.
