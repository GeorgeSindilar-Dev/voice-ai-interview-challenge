# Plan: voice-assisted password reset (lean version)

**Goal:** hand over a working agent that does what the challenge asks. Simple, not
production-ready. The things we deliberately skip are listed at the end, so they can
be discussed in the meeting with the interviewer.

**Estimate:** about 6–7 hours, plus a time-boxed phone attempt.

---

## 1. What we build

**One ASP.NET Core app** (.NET 10) on Azure App Service, with **three pages**:

| Page | URL | What it does |
|---|---|---|
| **Agent page** | `/` | Asks for the access code, then Start/End buttons; talks to the agent through the microphone; shows the agent's captions |
| **Mock inbox** | `/mock/inbox` | Login (separate inbox password per test user), then a list of messages: the code and the reset link |
| **Reset form** | `/reset/` | Opened from the inbox link. New password + confirm, sent over HTTPS to the mock reset API |

```
Browser (agent page) ──WebSocket──► App: voice session ──► Azure Voice Live (gpt-4.1-mini)
                                       │ tool calls
                                       ▼
                                   RecoveryWorkflow (state machine)
                                       │ HTTP + service credential
                                       ▼
                                   /mock/v1/... (mock issuer + tickets, same app)
Browser (inbox)  ──────────────────────► /mock/inbox
Browser (reset form) ── token + password ──► /mock/v1/password/validate, /mock/v1/resets
```

The agent's backend talks to the mock **over HTTP with the service credential**, as if
it were an external system. So the boundary stays clear, even though both live in
one app.

### The agent (Voice Live, tools in our code)
- One Voice Live session per call. Prompt and tools are defined in code.
- **Tools** (no IDs, no free text; the backend knows the session from the connection):
  `start_recovery(username)`, `submit_code(code)`, `send_reset_link()`,
  `check_reset_status()`, `request_human()`, `cancel_reset()`, `end_call()`.
- **State machine** (`RecoveryWorkflow`): AwaitingUsername → AwaitingCode → Verified →
  LinkSent → Completed, plus Escalated/Cancelled. A tool called in the wrong state is
  refused by code, whatever the model says.
- **Truth:** the agent says "your password has been reset" only when the mock
  returns `completed` with a receipt. The tool result contains the exact sentence
  to say.
- **Voice settings:** semantic turn detection with barge-in (interrupt + truncate),
  noise suppression, echo cancellation, English, short answers. The browser clears
  its playback on barge-in.
- **Guardrails** (the cheap, high-value items from the guardrails research; see
  CLAUDE.md rule 3):
  - the state machine refuses out-of-state tools;
  - narrow tools (no IDs, free text or destinations);
  - outcome sentences come from the backend;
  - read-back before submitting a code;
  - a fixed safe line on filtered or failed responses;
  - a 10-minute call limit;
  - a clear prompt: reset only, English only, discloses that it's automated, never
    asks for or repeats passwords, caller claims are not proof.

  Skipped: strikes, output monitor, telemetry framework, adversarial test runs.
- **Access code** on the agent page (one secret in app settings, checked by the
  backend, exchanged for a cookie), so strangers can't spend Voice Live credit.

### The mock (small on purpose)
Only what the journey needs, following the contract's routes and JSON field names:

| Route | Rules we implement |
|---|---|
| `POST /mock/v1/recoveries` | Same answer for unknown users (no delivery). One active recovery per user (else `429`). 6-digit code, **valid 120 s**, written to the inbox. Same `request_id` → same answer. |
| `POST /mock/v1/recoveries/{id}/verify` | `Idempotency-Key` replay returns the stored answer. **2 wrong codes → exhausted.** Expired → `410`. |
| `POST /mock/v1/recoveries/{id}/reset-link` | Only after verification. Random token (32 bytes), **10 min**, single use, link written to the inbox. |
| `GET /mock/v1/recoveries/{id}` | Status, receipt, unlock status |
| `GET /mock/v1/policy`, `POST /mock/v1/password/validate`, `POST /mock/v1/resets` | Policy check on the server; a reset completes immediately with a receipt; token single use; safe violation messages only |
| `POST /mock/v1/tickets`, `POST /mock/v1/tickets/{id}/outcome` | One ticket per recovery; `resolved` only with the matching receipt; a resolved ticket is never overwritten |

Skipped in the mock: delayed or pending resets, `GET /reset-operations`, fault
injection, rate limits. Three synthetic users from configuration (one needs
"unlock").

### State: a JSON file in Blob Storage
- Each **call session** is saved as `state/sessions/<sessionId>.json` after each change.
- The **mock's state** is saved as `state/mock.json` after each change.
- **On startup:** load the mock state. For every session left open (link sent, no
  final ticket), ask the mock for its status and update the ticket truthfully
  (`resolved` with a receipt, or `cancelled`/`call_dropped`).
- This covers the README's "process restart" test simply. A live call is still lost
  on a restart; that's documented.

### Transcripts (for debugging)
- One JSON file per call in the `transcripts` container: the agent's and the caller's
  final sentences.
- **Simple masking** before saving: digit runs (codes) → `[CODE]`; the rest of a
  sentence after "password is" → `[REDACTED]`. That's enough for a debug aid, and it
  keeps the challenge's rule that passwords never reach transcripts.

### Logs
- Application Insights for the API (requests, errors, dependencies).
- **Never logged:** request bodies, codes, tokens, passwords, transcript text, tool
  arguments. Only IDs, states, status codes and durations.

### Phone (after the browser works)
- First a 5-minute check: can our subscription get an ACS number at all?
  (Microsoft announced the ACS retirement in September 2026; new resources may not
  get numbers.)
- If yes: ACS incoming call → answer with audio streaming → the **same** voice
  session as the browser. The voice session talks to an `IAudioChannel`, so the phone
  is a second implementation of it.
- If no: document why, and the browser stays the channel.

---

## 2. Azure (kept simple)

One `az` script (`solution/scripts/setup-azure.ps1`) creates:

| Resource | Why |
|---|---|
| Resource group `rg-voicereset` (Sweden Central) | Everything in one place, one delete |
| Foundry / AI Services resource | Voice Live |
| Storage account | `state` and `transcripts` containers |
| App Service plan B1 (Linux) + web app | The app (WebSockets on, Always On, HTTPS only) |
| Application Insights | API logs |

- The web app gets a **system managed identity** (2 role assignments: Voice Live and
  Blob). The code uses `DefaultAzureCredential`, so it works with `az login` locally
  and with the identity in Azure. No keys for Voice Live or storage.
- **Secrets** (access code, mock service credential, inbox passwords) go in **app
  settings**, which are encrypted at rest. No Key Vault, no Bicep.

---

## 3. Steps

| # | Step | Est. | Done when |
|---|---|---|---|
| 0 | **Setup:** Azure CLI installed, `az login` (you), `setup-azure.ps1` run | 45 min | Resources exist; app settings set |
| 1 | **Skeleton:** solution, app project, test project, `/health` returning the commit SHA | 20 min | Builds; `/health` works locally |
| 2 | **Mock:** issuer + tickets + inbox page (login, messages) + JSON state | 1 h | Tests for the 120 s / 2 attempts / single-use / receipt rules pass |
| 3 | **Workflow:** state machine + tools + HTTP client to the mock + session state file | 1 h | Tests for the state transitions pass |
| 4 | **Voice:** Voice Live session + browser agent page + access code + barge-in | 1.5 h | **First real conversation** in Azure |
| 5 | **Reset form** | 30 min | Full journey works end to end |
| 6 | **Transcripts + restart check on startup** | 45 min | A transcript appears in Blob; restart test passes |
| 7 | **Deploy script + deploy** (`dotnet publish` → `az webapp deploy`) | 20 min | `/health` in Azure shows the pushed commit |
| 8 | **Manual test** with the browser (Playwright tools, not test code) + fixes | 45 min | Happy path + key failure paths work; console clean |
| 9 | **Docs:** `SETUP.md` (setup, config names, trust boundaries, limitations) | 45 min | Someone else could deploy it |
| 10 | **Phone attempt** (time-boxed) | ≤ 1 h | Works, or documented why not |
| 11 | **Submission JSON** (validated against the schema) | 15 min | Ready for you to send |

**Tests (xUnit, about 10–12, no Playwright in code):**
- Mock: code expires after 120 s; 2 wrong attempts exhaust; an idempotent verify
  doesn't count twice; a reset link is single use; `resolved` needs the matching
  receipt; unknown users get the same answer.
- Workflow: `send_reset_link` before verification is refused; success only with a
  receipt; a code fragment ("4 7") isn't submitted; two sessions don't share a
  recovery; restart: a saved open session is fixed on startup.
- Masking: codes and "password is …" are masked.

---

## 4. What we deliberately skip (for the meeting)

| Skipped | Why it's fine for the challenge | What I'd do in production |
|---|---|---|
| Separate mock app and storage | The mocks stand in for external systems | Real external issuer; boundary enforced by network + RBAC |
| Key Vault, Bicep | Simpler setup | Key Vault references, infrastructure as code |
| Live-call survival across restarts | State and tickets survive; only the live call drops | External media relay / reconnect flow |
| Strikes, output monitor, telemetry events, state-aware silence timers | Prompt + state machine + call duration cover the core | Layered guardrails ([archived research](archive/overnight/research/guardrails-research.md)) |
| Advanced masking | Debug transcripts only | A PII service; stricter retention |
| Large automated test suites, red-team runs | ~12 unit tests + a manual pass | Adversarial conversation tests, PyRIT/promptfoo |
| Rate limits, concurrency caps | The access code protects cost | Rate limiting per IP/session |

---

## 5. Open points

- **Foundry Agent vs. tools in our code:** this plan uses tools in code (Voice Live
  model mode). If your friend's answers show a clean way to bind Foundry Agent tool
  calls to the call session, only the "voice" step changes.
- **Your email** for a budget alert (optional; set in the portal).
- **Inbox logins and the access code** are shared privately with the interviewer.
