# Working context (for any session, local or cloud)

Facts and owner preferences that aren't obvious from the code. Read with `CLAUDE.md`.

## Owner preferences
- **No AI attribution anywhere on GitHub:** commits, commit messages, branch names, PRs, PR/issue
  comments and code comments never mention Claude, Claude Code or AI. No `Co-Authored-By`, no
  "Generated with". The only author is George Sindilar. Using AI is fine and `CLAUDE.md` is committed.
- **Keep it simple.** Smallest design that works; the owner must be able to explain every line.
- **Desktop only.** Pages target desktop Chrome/Edge. No mobile/responsive work, checks or review findings.
- **Never name the interviewer** in files, code or commits; write "the interviewer".
- **Structure (agreed for the cleanup pass):** features stay as top-level folders; everything that is
  not a feature (`Http/`, `Observability/`, `Storage/`, possibly `Health/`) moves into one `Shared/`
  folder. Also remove unused code. Done after the app works end to end.
- No local-run deliverable: everything is deployed to Azure.

## Status (2026-10-03)
- Implementation tasks T1–T15 are done and on `main` (80 tests, 0 warnings). T16 (docs) was stopped
  by the owner; an unreviewed draft is in `drafts/SETUP-draft.md`.
- Remaining work: `solution/docs/remaining-work.md`.
- Implementation process used: superpowers subagent-driven development: implementer → spec review →
  code-quality review → fixes, per task. Plans: `plans/` (`00-contracts.md`, `00-reconciliation.md`
  wins over the task plans and records the real names after each task).

## Azure account situation
- The first free trial ($200) was created in a company directory the owner had been invited to as a
  guest; after leaving that directory the owner lost access to it. Recovery would need billing
  support or that directory's admin (elevate access, re-invite, then Change directory).
- A second Microsoft account has its own clean directory but $0 credit (pay-as-you-go). Plan: use it;
  expected cost about $5–15 for a week; add a budget alert; delete the resource group when done.
- From a cloud session, sign in with `az login --use-device-code` (the Azure CLI may need installing).

## Useful references
- Verified Voice Live 1.2.0 / ACS facts: `archive/overnight/research/sdk-reference.md`.
- Guardrails research (attack scenarios, prompt draft): `archive/overnight/research/guardrails-research.md`.
- Challenge-related planning (roadmap, walkthrough, requirements checklist): `planning/`, `submission/`.
