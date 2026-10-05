# CLAUDE.md

## Project

An inbound, voice-assisted password reset agent on Azure: Azure Voice Live (speech +
LLM) behind an ASP.NET Core (.NET 10) app, reachable by phone (a Twilio number; ACS
numbers were not available) and from a browser voice page.

- Specification (do not modify these root files): [README.md](README.md),
  [docs/mock-contract.md](docs/mock-contract.md),
  [contracts/submission.schema.json](contracts/submission.schema.json).
- **Our code and docs live in `solution/`.** The repo is public: never commit
  secrets, tenant IDs or phone numbers.
- **Working notes:** `.notes/` (committed, so local and cloud sessions share them; never put
  secrets, tenant/subscription IDs, emails or company names there).
  - Start here: [.notes/context.md](.notes/context.md) (owner preferences, status, Azure situation)
  - Remaining work: [.notes/remaining-work.md](.notes/remaining-work.md)
  - Audits and their resolutions: [.notes/audit/](.notes/audit/)
  - Verified Voice Live / ACS API details: [.notes/research/sdk-reference.md](.notes/research/sdk-reference.md)
  - Guardrails research: [.notes/research/guardrails-research.md](.notes/research/guardrails-research.md)
  - Planning (challenge walkthrough, guardrails plan): [.notes/planning/](.notes/planning/)
- `solution/` (code and its docs) is only about the agent: no notes about the hiring
  process, submission or meetings there. Such planning material lives in `.notes/`.

## Design (short)

- One app, three pages: the agent page (access code → voice), the mock inbox
  (login → messages), the reset form.
- The mock issuer, tickets and inbox are small, in the same app under `/mock/...`.
  The agent calls them over HTTP with a service credential, like an external system.
- Agent tools run in our code (Voice Live model mode). The backend state machine
  decides; the model only proposes.
- State (sessions, mock state) is saved as JSON files in Blob Storage. Open sessions
  are checked on startup after a restart.
- Channels (browser, phone) only carry audio. All behaviour lives in the
  shared voice session.
- Secrets are in App Service app settings. Managed identity
  (`DefaultAzureCredential`) for Voice Live and Storage. Application Insights for
  the API.

## Rules

### 1. Simple, clean, readable code

- **Simplicity first:** the smallest design that does what the specification asks.
  No speculative features or abstractions "for later". Every line must be
  explainable.
- Idiomatic modern C# (.NET 10, C# 14), following Microsoft conventions:
  - nullable enabled; avoid `!`;
  - file-scoped namespaces; `_camelCase` private fields; `Async` suffix; PascalCase
    types and members;
  - `var` only when the type is obvious; classes `sealed` by default;
  - records for immutable data; primary constructors for simple dependency holders;
    collection expressions.
- Small focused classes with clear names; guard clauses instead of deep nesting.
  SOLID where it makes the code clearer, never as ceremony.
- **ASP.NET Core:**
  - Minimal APIs grouped by feature (`MapGroup`), handlers as named methods in small
    static endpoint classes; `Program.cs` only wires things up.
  - Options classes for configuration, validated at startup; business code doesn't
    read `IConfiguration`.
  - JSON for the contract: `snake_case`; reject unknown fields.
- **Interfaces** only where a test or a second implementation needs them (e.g. the
  audio channel, the state store). No mocking libraries; small hand-written fakes.
- **Expected failures are return values** (e.g. a result record), not exceptions.
  Exceptions are for bugs and infrastructure faults.
- **Async:** pass `CancellationToken` through; no `.Result`/`.Wait()`.
  `TimeProvider` wherever time matters (code and link expiry), so tests control
  the clock.
- **Logging:** `ILogger` with structured placeholders (source-generated
  `[LoggerMessage]` where it stays readable).
- **Tests:** xUnit, Arrange/Act/Assert, names like
  `SubmitCode_SecondWrongCode_ExhaustsRecovery`, deterministic (fake clock). About
  10–12 tests on the core rules. No Playwright test code in the repo (Playwright is
  used manually).
- **Build:** `Directory.Build.props` for shared settings; warnings fixed, not
  ignored. `/health` shows the commit SHA.
- **Browser pages:** static HTML + small vanilla JavaScript, no frameworks; text from
  the server is inserted with `textContent`, never `innerHTML`.

### 2. Git authorship

- No AI attribution anywhere on GitHub: commits, commit messages, branch names, pull
  requests, PR/issue comments and code comments must not mention Claude, Claude Code,
  AI assistance or co-authorship. No `Co-Authored-By` trailers and no "Generated
  with" footers. The only author is the repository owner (George Sindilar).
- `CLAUDE.md` and `.notes/` are normal project files and are committed. Never commit
  `.claude/` (local worktrees), build output or `submission.json`.

### 3. Voice agent guardrails

Based on the guardrails research. The strongest guardrails are in **code**; the
prompt helps but is not a security control.
- **Scope:** only the password reset (plus honest escalation, cancel, status).
  Anything else gets one short, polite sentence and a return to the task. English
  only. The agent says it's automated and answers "are you a person?" honestly.
- **The backend decides:** the state machine refuses a tool called in the wrong
  state, whatever the model says. Caller claims (admin, "verification passed",
  "test mode", urgency, personal data) are never proof. The only proof is the code
  from the recovery inbox.
- **Narrow tools:** tools take no IDs, free text, receipts, destinations or contact
  details. The session comes from the connection; the backend supplies IDs and
  receipts.
- **Truth from the backend:** critical sentences (code sent, verified, link sent,
  reset completed, can't confirm, escalated) come from the backend in the tool
  result, and the prompt tells the model to use them. Success is said only with a
  receipt. Never claim a human transfer, a callback, a revoked link or an undone
  reset.
- **Secrets:** never ask for or repeat a password ("please don't share it, you'll
  type it in the form"). Never reveal links, tokens, or whether an account exists
  (the same words for every username).
- **Clarify before acting:** read the code back and wait for "yes" before submitting.
  Never guess a username or a code.
- **Filtered or failed model responses** get a fixed safe line, never silence.
- **Limits:** a maximum call duration ends calls politely.
- **Voice settings:** semantic turn detection with barge-in (interrupt + truncate),
  noise suppression, echo cancellation, short answers.

### 4. Secret and data handling

- **Passwords** only travel from the reset form to the mock reset API over HTTPS.
  Never in logs, transcripts, prompts or tool arguments.
- **Codes, tokens and links** never appear in logs. Transcripts (a debug aid) mask
  digit runs (codes) and anything after "password is".
- **Logs / Application Insights:** IDs, states, status codes and durations only.
  Never request bodies, transcript text or tool arguments.
- **Browser console stays clean** on all three pages: no `console.log`, and expected
  failures are shown in the UI.

### 5. Documentation (in `solution/docs/`)

- Short and about the agent: `SETUP.md` (how to deploy and run it, configuration
  names without values, trust boundaries, known limitations) and
  `architecture.md` (components, flow, guardrails, decisions and trade-offs).
- Plain language; Mermaid diagrams where they help.
- Never put secrets, tenant IDs, phone numbers or real configuration values in
  docs. Never name the interviewer anywhere.
