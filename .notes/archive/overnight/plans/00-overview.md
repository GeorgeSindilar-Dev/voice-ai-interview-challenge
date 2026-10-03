# Plans overview: architecture and shared decisions

This is the foundation every step plan builds on. It fixes the names, folders,
types and configuration keys, so all plans use the same ones. When a step plan
and this file disagree, **this file wins**. Fix the step plan.

Decisions taken overnight without the owner are marked **(Q-n)**. Each one links to
a question in [00-questions-for-you.md](00-questions-for-you.md), with a
recommended default. Implementation can start with the defaults.

---

## 1. The system in one picture

```
                        ┌─────────────────────────── Azure (one resource group) ─────────────────────────────┐
                        │                                                                                    │
 Browser ─ voice page ──┼─► app-agent (ASP.NET Core)  ──────── Voice Live (Foundry, model mode) ──────────────│
   (access code gate)   │     • voice sessions, state machine,                                               │
 Phone ─ ACS ───────────┼─►     tools, guardrails, reconciliation        ┌──────────────────────────────┐    │
   (optional)           │     • reset form (static page)  ── browser ──►│ app-mocks (ASP.NET Core)     │    │
                        │     • transcripts → Blob                      │  • mock issuer API  /v1/...   │    │
                        │     • session state → Table   ── service ────►│  • mock ticket API            │    │
                        │                                   credential  │  • mock inbox page (login)    │    │
 Browser ─ inbox tab ───┼──────────────────────────────────────────────►│  • state → its own Table      │    │
                        │                                               └──────────────────────────────┘    │
                        └────────────────────────────────────────────────────────────────────────────────────┘
```

Two web apps on **one App Service plan** (Linux, B1):

| App | Owns | Can NOT |
|---|---|---|
| **app-agent** | Voice sessions, the recovery state machine, tools, the Voice Live bridge, the browser voice page, the **reset form page** (static), transcripts, reconciliation | Read the inbox, codes, tokens or passwords (it has no API for them, and no access to the mocks' storage) |
| **app-mocks** | The mock issuer (`/v1/...` from the mock contract), the ticket API, the mock inbox page, synthetic users | — (it plays the "enterprise systems") |

**The password never touches app-agent.** The reset form is a static page served by
app-agent, but its JavaScript sends the password **directly to app-mocks**
(`/v1/password/validate`, `/v1/resets`) over HTTPS. CORS on app-mocks allows only
the app-agent origin. Our backend therefore can't log a password even by mistake,
which is a strong trust-boundary story. **(Q-1)**

---

## 2. Repository and solution layout

```
solution/
├── VoiceReset.slnx
├── Directory.Build.props            shared build settings (nullable, warnings as errors, SourceRevisionId)
├── Directory.Packages.props         central package versions
├── .editorconfig
├── .gitignore
├── src/
│   ├── VoiceReset.Agent/            web app: app-agent
│   └── VoiceReset.Mocks/            web app: app-mocks
├── tests/
│   ├── VoiceReset.Agent.Tests/
│   └── VoiceReset.Mocks.Tests/
├── infra/
│   └── main.bicep                   all Azure resources (Q-2)
├── scripts/
│   ├── deploy.ps1                   build + zip deploy both apps
│   └── seed-users.ps1               (if needed) prints how to set synthetic users
└── docs/
    ├── planning/  plans/  research/
    ├── architecture/  infrastructure/  process/  submission/
```

Only two production projects, deliberately. There are no "Core/Domain/
Infrastructure" project layers. Inside each project, **folders by feature**:

```
src/VoiceReset.Agent/
├── Program.cs
├── appsettings.json
├── Configuration/        options classes (AgentOptions, IssuerOptions, VoiceLiveOptions, ...)
├── Recovery/             state machine + session model + reconciliation
├── Issuer/               typed HttpClients for the mock issuer + ticket API, DTOs
├── Voice/                Voice Live session, tools, prompt, guardrail limits
├── Channels/             browser WebSocket channel, ACS media channel, IAudioChannel
├── Telephony/            ACS incoming call + callback endpoints (optional)
├── Transcripts/          masker + blob writer
├── Access/               access-code gate (cookie)
├── Health/               /health version endpoint
├── Prompts/              system-prompt.md, faq.md (embedded resources)
└── wwwroot/              index.html (voice page), reset/index.html (reset form), js/, css/
```

```
src/VoiceReset.Mocks/
├── Program.cs
├── Configuration/        MocksOptions (users, service credential, reset base URL, ...)
├── Issuer/               recoveries, verification, links, policy, resets endpoints + logic
├── Tickets/              ticket endpoints + logic
├── Inbox/                inbox store, login, inbox page (Razor Pages)
├── Storage/              table entities + repository (Table Storage / in-memory)
├── Shared/               error responses, idempotency store, clock, ids
└── Pages/                Razor Pages for the inbox (Login, Index)
```

---

## 3. Technology choices (fixed)

| Concern | Choice | Why |
|---|---|---|
| Runtime | .NET 10 (LTS), C# 14 | Installed, current LTS |
| Web | ASP.NET Core **Minimal APIs** with route groups | Least ceremony, easy to read |
| Inbox UI | **Razor Pages** (server-rendered, no JS framework) | Simple login + list page |
| Voice/reset pages | Static HTML + vanilla JS (`AudioWorklet` for audio) | No framework, clean console |
| Voice AI | `Azure.AI.VoiceLive`, **model mode** | See [01-decision](../planning/01-decision-where-the-agent-lives.md) |
| Telephony | `Azure.Communication.CallAutomation` (optional channel) | Required stack |
| State | **Azure Table Storage** (`Azure.Data.Tables`), one storage account per app | Cheap, simple, ETag concurrency for atomic updates |
| Transcripts | Blob Storage (agent's storage account), container `transcripts` | Requirement |
| Secrets | Key Vault + App Service **Key Vault references** in app settings | No secret in code or repo |
| Identity | System-assigned **managed identity** per app: `ManagedIdentityCredential` in Azure, `AzureCliCredential` locally (chosen by environment in one place) | No keys; Microsoft advises against `DefaultAzureCredential` in production |
| HTTP resilience | `Microsoft.Extensions.Http.Resilience` (standard handler) | Retries are allowed because every agent → issuer mutation carries an idempotency key (the contract guarantees no second side effect); otherwise `DisableForUnsafeHttpMethods()` |
| JSON | .NET 10 strict mode (unknown and duplicate members rejected), `snake_case` for contract DTOs | The contract says to reject unexpected fields |
| Tests | xUnit v3 (`xunit.v3` 4.0.1) on Microsoft Testing Platform (`global.json` test runner) | Current |
| Time | `TimeProvider` everywhere; `FakeTimeProvider` in tests | 120 s / 10 min expiry tested in milliseconds |
| Observability | Azure Monitor OpenTelemetry distro → Application Insights | Built in; no bodies logged |
| Tests | xUnit, `WebApplicationFactory`, hand-written fakes (no mocking library) | Readable, no magic |
| Infra | Bicep (`infra/main.bicep`) + `az` CLI **(Q-2)** | One re-runnable command; also gives "deploy your own copy" |

---

## 4. Shared names (use exactly these)

### Azure resources (prefix `vr` = voice reset, plus a short unique suffix)

| Resource | Name pattern |
|---|---|
| Resource group | `rg-voicereset` |
| App Service plan | `asp-voicereset` (Linux B1) |
| Web app (agent) | `app-vr-agent-<suffix>` |
| Web app (mocks) | `app-vr-mocks-<suffix>` |
| Web app (restart-test copy) | `app-vr-agent-test-<suffix>` + `app-vr-mocks-test-<suffix>` (created only for the manual restart test) |
| Storage (agent) | `stvragent<suffix>` |
| Storage (mocks) | `stvrmocks<suffix>` |
| Key Vault | `kv-vr-<suffix>` |
| Foundry / AI Services | `ais-vr-<suffix>` (kind `AIServices`, custom subdomain) |
| Log Analytics + App Insights | `log-vr-<suffix>`, `appi-vr-<suffix>` |
| ACS (optional) | `acs-vr-<suffix>` |

Region: **Sweden Central** **(Q-3)**. Per the [SDK research](../research/sdk-reference.md),
it supports Voice Live, realtime and cascaded models, and HD voices. West Europe has
no realtime models.

### Voice Live settings (from the SDK research, all configuration values)

| Setting | Value |
|---|---|
| Package | `Azure.AI.VoiceLive` **1.2.0** (GA, API `2026-07-15`) |
| Model | `gpt-4.1-mini` (Standard tier, about $0.03 per conversation-minute) **(Q-4)** |
| Input transcription | `azure-speech` (must match a cascaded model) |
| Voice | `en-US-Ava:DragonHDLatestNeural` |
| Turn detection | `AzureSemanticVadTurnDetection`, `InterruptResponse = true`, `AutoTruncate = true`, `RemoveFillerWords = true` |
| Audio | PCM16 24 kHz mono both ways; deep noise suppression; server echo cancellation |
| Tools | `AllowParallelToolCalls = false`; capped output tokens |
| Auth | `DefaultAzureCredential`, scope `https://ai.azure.com/.default`; the app identity gets **Cognitive Services User** + **Foundry User** on the Foundry resource |
| Limits | 60 min per session, 120K tokens per minute, about 100 new connections per minute per resource |

### Phone channel: almost certainly not available (ACS retirement)

Microsoft announced the ACS retirement in **September 2026**. Tenants whose **first**
ACS resource is created after the announcement **can't get phone numbers**, trial or
paid. Sign-ups close 2026-10-23. A brand-new subscription falls into this group.
So:
- Step 9 starts with a **5-minute eligibility check**. If no number can be obtained,
  the phone channel is **not built**. It's documented instead (design + reason +
  retirement guide link) as a known limitation.
- The **`IAudioChannel` abstraction stays**. It costs nothing, makes the voice
  session testable with a fake channel, and shows where ACS would plug in.
- The interviewer's automated tests then use the **browser voice page** (its
  WebSocket protocol is documented in step 7).

### Configuration keys (app settings; `__` = section separator)

**app-agent**

| Key | Meaning | Secret? |
|---|---|---|
| `Issuer__BaseUrl` | `https://app-vr-mocks-<suffix>.azurewebsites.net` | no |
| `Issuer__ServiceCredential` | Bearer credential for the mock issuer/ticket API | **yes** (Key Vault ref) |
| `VoiceLive__Endpoint` | `https://ais-vr-<suffix>.services.ai.azure.com/` | no |
| `VoiceLive__Model` | e.g. `gpt-realtime` (Q-4) | no |
| `VoiceLive__Voice` | e.g. `en-US-Ava:DragonHDLatestNeural` | no |
| `Access__Code` | voice page access code | **yes** (Key Vault ref) |
| `Storage__TableEndpoint` | `https://stvragent<suffix>.table.core.windows.net` | no |
| `Storage__BlobEndpoint` | `https://stvragent<suffix>.blob.core.windows.net` | no |
| `Limits__*` | see §8 (`MaxCallSecondsBrowser`, `MaxCallSecondsPhone`, `MaxTurns`, `MaxStrikes`, `MaxDistinctUsernames`, `SilencePromptSeconds`, `SilenceEndSeconds`, `LinkStepCheckInSeconds`, `MaxConcurrentSessions`) | no |
| `Telephony__Enabled` | `false` until the number exists | no |
| `Telephony__AcsEndpoint` | `https://acs-vr-<suffix>.communication.azure.com` | no |
| `Telephony__PublicBaseUrl` | `https://app-vr-agent-<suffix>.azurewebsites.net` | no |
| `Reconciliation__IntervalSeconds` | default 15 | no |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights | no (not a secret in practice, but kept out of repo) |

**app-mocks**

| Key | Meaning | Secret? |
|---|---|---|
| `Mocks__ServiceCredential` | the same credential the agent uses (one namespace) | **yes** |
| `Mocks__ResetBaseUrl` | `https://app-vr-agent-<suffix>.azurewebsites.net/reset/` — the registered reset form URL | no |
| `Mocks__AllowedCorsOrigin` | `https://app-vr-agent-<suffix>.azurewebsites.net` | no |
| `Mocks__Users__0__Username` … | synthetic users (username, display name, requiresUnlock) | no |
| `Mocks__Users__0__InboxPassword` … | inbox login per synthetic user | **yes** |
| `Mocks__Users__0__InitialPassword` … | current synthetic password (so "same as current" can be checked) | **yes** |
| `Storage__TableEndpoint` | `https://stvrmocks<suffix>.table.core.windows.net` | no |
| `Faults__ResetCompletionDelaySeconds` | 0 = complete immediately; >0 = return `202 pending` and complete later (to exercise reconciliation) | no |

---

## 5. The recovery state machine (app-agent)

One `CallSession` per conversation (browser or phone). The state lives in **Table
Storage** (`sessions` table), so a restart doesn't lose it.

```
          start
            │
            ▼
   ┌──────────────────┐  start_recovery(username)   ┌───────────────────┐
   │ AwaitingUsername │ ───────────────────────────►│ AwaitingCode      │
   └──────────────────┘   (issuer 202, or throttled │ (recovery exists) │
            ▲              → stays + "can't start") └───────────────────┘
            │                                         │ submit_code(code)
            │                     verified ┌──────────┤
            │                              ▼          │ 422 → stays (1 attempt left)
            │                     ┌──────────────┐    │ 409 exhausted / 410 expired
            │                     │ Verified     │    ▼
            │                     └──────────────┘  ┌────────────┐
            │       send_reset_link()  │            │ Escalated  │ (terminal for voice)
            │                          ▼            └────────────┘
            │                     ┌──────────────┐        ▲
            │                     │ LinkSent     │────────┤ request_human / browser unavailable
            │                     └──────────────┘        │ dependency_unavailable
            │      check_reset_status()│                  │
            │        (or reconciler)   ▼                  │
            │                     ┌──────────────┐        │
            │                     │ Completed    │ (receipt received; ticket resolved)
            │                     └──────────────┘
   any state ── cancel_reset() ──► Cancelled      any state ── call ends ──► Ended flag set
```

- **States (enum `RecoveryState`):** `AwaitingUsername`, `AwaitingCode`,
  `Verified`, `LinkSent`, `Completed`, `Escalated`, `Cancelled`.
- `Ended` is not a state. It's a flag (`EndedAt`, `EndReason`) because a call can end
  in any state, and reconciliation continues after it.
- Transitions are implemented as **one class with one method per tool**
  (`RecoveryWorkflow`), using a plain `switch`/guard clauses, with no state machine
  library.
- Every method returns a `ToolResult` (`Ok`, `Status`, `SayHint`), which is the
  *only* thing the model learns. `SayHint` is safe, fixed text.

### Tools exposed to the model (exact names)

| Tool | Arguments | Allowed in state | Calls |
|---|---|---|---|
| `start_recovery` | `username` (string, `^[a-z0-9._-]{3,64}$` after normalisation) | `AwaitingUsername` | `POST /v1/recoveries`, then `POST /v1/tickets` |
| `submit_code` | `code` (string, digits only, 4–8) | `AwaitingCode` | `POST /v1/recoveries/{id}/verify` |
| `send_reset_link` | — | `Verified` | `POST /v1/recoveries/{id}/reset-link` |
| `check_reset_status` | — | `LinkSent` | `GET /v1/recoveries/{id}` |
| `request_human` | `reason` enum: `caller_asked`, `browser_unavailable` | any non-terminal | ticket outcome `escalated` |
| `cancel_reset` | — | any non-terminal | ticket outcome `cancelled` (`caller_cancelled`) |
| `end_call` | — | any | ends the session gracefully |

**No tool takes an ID.** The session is known from the connection the tool call
arrived on.

---

## 6. Idempotency and keys (app-agent → issuer)

| Action | Key | Generated | Persisted before the call? |
|---|---|---|---|
| Start recovery | `request_id` | when `start_recovery` is accepted | **yes** (in the session) |
| Verify code | `Idempotency-Key` header | per *completed submission* (one per `submit_code` call) | **yes** |
| Send link | `operation_id` | when `send_reset_link` is accepted | **yes** |
| Create ticket | `operation_id` | once per session | **yes** |
| Ticket outcome update | `operation_id` | per update | **yes** |

Rule: **write the key to the session first, then call the issuer.** After a crash,
a retry reuses the same key, so the issuer returns the recorded result and no
second side effect happens.

---

## 7. Ticket outcome rules (app-agent)

| Situation | Outcome | `reason_code` |
|---|---|---|
| Receipt received (`completed`) | `resolved` | `reset_completed` (with receipt) |
| 2nd wrong code | `escalated` | `verification_exhausted` |
| Code expired | `escalated` | `verification_expired` |
| Caller asked for a human | `escalated` | `human_requested` (kept even if a reset later completes; note added) |
| Browser not available | `escalated` | `browser_unavailable` |
| Caller cancelled | `cancelled` | `caller_cancelled` |
| Call dropped before completion | `cancelled` | `call_dropped` → may later become `resolved` if a receipt appears |
| Issuer down / 503 | `escalated` | `dependency_unavailable` |
| Reset accepted but status unknown | `pending` | `completion_unknown` → reconciled later |

The reconciler (a `BackgroundService`, every 15 s, and once at startup) looks at
sessions whose outcome isn't final, checks `GET /v1/recoveries/{id}`, and updates
the ticket. **It never downgrades `resolved`.**

---

## 8. Guardrail limits (app-agent, code-enforced)

Updated after the [guardrails research](../research/guardrails-research.md).

| Limit (`LimitsOptions`) | Default | On hit |
|---|---|---|
| `MaxCallSecondsBrowser` / `MaxCallSecondsPhone` | 600 / 290 (trial number cap is 300 s) | one-minute warning, then a polite goodbye, end, outcome recorded |
| `MaxTurns` | 60 | same |
| `MaxStrikes` | 3. Strikes: off-topic, abuse, content-filter hit, tool call refused by the state machine | short goodbye, end, honest ticket outcome |
| `MaxDistinctUsernames` | 2 per call | "I can't start another reset on this call" + escalation offer, the same words for known and unknown accounts |
| `SilencePromptSeconds` / `SilenceEndSeconds` | 10 / 40, **state-aware**: in `LinkSent` silence is normal (the caller is typing in the browser) | re-prompt, then goodbye |
| `LinkStepCheckInSeconds` | 60 (in `LinkSent`: short check-in plus a status check) | — |
| `MaxConcurrentSessions` | 10, plus a rate limit on session start | friendly "busy, try again later" |

More code-enforced guardrails from the research (all in step 7 unless noted):
- **C1:** a filtered or incomplete model response → a fixed safe line, plus a strike. Never silence.
- **C5 (steps 6 and 7):** tools take no free text, IDs, receipts, destinations or contact details.
- **C6 (step 6):** `ToolResult.SayHint` holds the backend-written sentence for critical outcomes, and the prompt tells the model to use it.
- **C7:** an output monitor on the agent's transcript (URLs, "token", success claims while not `Completed`, "transferring") → cancel, correct, log.
- **C8:** Voice Live settings as guardrails (semantic VAD, filler-word removal, interrupt + auto-truncate, noise suppression, echo cancellation, English transcription, low max output tokens).
- **C10:** guardrail telemetry events without content, plus a hash of the prompt version per session.
- **C15:** any text shown in a page uses `textContent`, never `innerHTML`.

Details: [04-guardrails-plan.md](../planning/04-guardrails-plan.md) and
[research/guardrails-research.md](../research/guardrails-research.md).

---

## 8b. Interfaces between steps (fixed names)

**Step 7 (voice) ↔ step 10 (transcripts).** Step 10 builds these. Step 7 calls them
from its Voice Live event loop.

```csharp
namespace VoiceReset.Agent.Transcripts;

// Pure function; no I/O. Masks codes, spoken passwords, links/tokens, emails.
public sealed class TranscriptMasker
{
    public MaskResult Mask(string text, TurnRole role);          // MaskResult(string Text, int MasksApplied)
}

public enum TurnRole { Caller, Agent }

// One per voice session, in memory. Only masked text is ever stored in it.
public sealed class TranscriptRecorder(string sessionId, string channel, TimeProvider time, TranscriptMasker masker)
{
    public void OnCallerTurnCommitted(string itemId);              // input_audio_buffer.committed → reserves order
    public void OnCallerTranscript(string itemId, string text);    // conversation.item.input_audio_transcription.completed
    public void OnAgentTranscript(string text);                    // response.audio_transcript.done
    public TranscriptDocument Complete(string endReason, string? ticketOutcome);
}

// Writes one JSON blob per session, create-only (a duplicate write is a no-op).
public sealed class BlobTranscriptStore(BlobContainerClient container)
{
    public Task SaveAsync(TranscriptDocument document, CancellationToken ct);
    public Task SaveLostStubAsync(string sessionId, string channel, DateTimeOffset startedAt, string endReason, CancellationToken ct); // after a restart: turnsLost = true
}
```

Additions from the step 10 plan (they win over the sketch above):
- `MaskResult(string Text, int MasksApplied, bool MaskNextCallerTurn = false)`
- `TranscriptRecorder` also has `string? PromptVersion { get; init; }` and
  `void OnVoiceLiveEvent(SessionUpdate update)`. **Step 7 calls only
  `OnVoiceLiveEvent`** from its event loop; the recorder routes the three
  transcript events itself.
- `BlobTranscriptStore(BlobContainerClient container, ILogger<BlobTranscriptStore> logger)`
  plus `static string BlobName(string sessionId, DateTimeOffset startedAt)`.
- `TranscriptDocument.EndedAt` is nullable (the restart stub).
- DI: `services.AddTranscripts()`. The shared credential is registered as
  `TokenCredential` (by step 6).
- At session end, step 7 calls `Complete` + `SaveAsync` using the
  `ApplicationStopping` token (not the call's token, which is already cancelled).

**Step 7 (voice) ↔ step 6 (workflow).** Step 7 calls only the public surface that
the step 6 plan lists in its "Public surface for later steps" section.

**Step 7 channels.** `IAudioChannel` lives in `Channels/` and is owned by step 7:

```csharp
public interface IAudioChannel
{
    string Kind { get; }                                             // "browser" | "phone"
    IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAudioAsync(CancellationToken ct);   // PCM16 24 kHz mono
    ValueTask SendAudioAsync(ReadOnlyMemory<byte> pcm16, CancellationToken ct);
    ValueTask StopPlaybackAsync(CancellationToken ct);              // barge-in flush
    ValueTask SendControlAsync(ChannelControl message, CancellationToken ct); // captions, ended, error (browser); no-op for phone
    ValueTask CloseAsync(string reason, CancellationToken ct);
}
```

---

## 8c. Accepted additions from the step plans (reviewed 2026-10-03)

Each step plan ends with an "Additions to 00-overview" section. These were reviewed
and are **accepted** as part of this overview. The most important ones:

| Topic | Decision | Source |
|---|---|---|
| Session IDs | `s-` + 32 hex digits (random); used as the transcript blob name | step 6; step 10 updated |
| Mocks IDs | `rec_…`, `tkt_<recovery id>`, `rcpt_…`, `msg_…`; policy version `2026-10-v1` | step 5 |
| Mocks tables | `accounts`, `recoveries`, `idempotency`, `tokens`, `tickets`, `inbox` (created by the app at startup) | step 5 |
| Agent table | `sessions` (one row per session: JSON `Data` column + `IsOpen`); created by Bicep | step 6; step 4 updated |
| DI / mapping | `AddAgentOptions`, `AddIssuerClient`, `AddRecovery`, `AddTranscripts`, `AddAccessGate`, `AddVoice`, `MapHealth`, `MapAccess`, `MapVoice`, `UseVoicePageSecurityHeaders`, `UseVoiceWebSockets` | steps 6, 7, 10 |
| Dispatcher | `new ToolDispatcher(workflow)`; `DispatchAsync(sessionId, toolName, argumentsJson, ct)` → `ToolResult`; `ToolDispatcher.ToOutputJson(result)` = `{"ok","status","say"}` | step 7 |
| `call_id` de-dup | in `VoiceSession.OnFunctionCallAsync` (the dispatcher never sees the call ID) | step 11 Task 2 |
| Routes (agent) | `GET /health`, `POST /access` (JSON only), `GET /access/status`, `/voice/ws`, `/reset/` + `GET /reset/config.json` | steps 6, 7, 8 |
| Gate scope | the access cookie guards **only** `/voice/ws`; pages, `/health` and `/reset/*` are anonymous | step 7 |
| Cookies | `__Host-vr-access` (agent), `__Host-vr-inbox` (mocks inbox) | steps 5, 7 |
| New settings | `Access__AllowedOrigin` (required; in Bicep), `Access__CookieLifetimeMinutes` (120), `VoiceLive__TranscriptionModel` (`azure-speech`), `VoiceLive__MaxResponseOutputTokens` (300), `VoiceLive__Temperature` (0.6), `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (both apps) | steps 5, 7 |
| Middleware order | HSTS → reset headers → voice headers (skip `/reset`) → default files → static files → WebSockets → authentication → authorization → rate limiter → endpoints | step 7 |
| Log event IDs | 1000–1099 transcripts, 2000–2049 voice, 2050–2059 access | steps 7, 10 |
| Test commands | `dotnet test --project <test project> --filter-class "<class>"` / `--filter-namespace` / `--filter-trait "Category=Live|Browser|E2E"` (Microsoft Testing Platform) | steps 5–7; 8, 11, 12 updated |
| Test helpers | step 6 `FakeIssuerHandler` (scripted), `WorkflowHarness`, `AgentAppFactory`; step 7 `VoiceSessionHarness`, `FakeVoiceLiveConnection`, `FakeAudioChannel`; step 11 `StatefulIssuerHandler`, `AgentTestHost` | steps 6, 7, 11 |
| Clean console exception | expected 4xx answers on the reset form make Chrome log "Failed to load resource"; documented (Q-8.4). A wrong access code answers `200 {"ok":false}` to avoid it (Q-7.1) | steps 7, 8 |
| Possible 8th tool | `report_off_topic` (no arguments, adds a strike) **if Q-7.4 is answered "yes"** | step 7 |

---

## 9. Where each plan lives

| Roadmap step | Plan file |
|---|---|
| 1 Clarify and decide | this file + [00-questions-for-you.md](00-questions-for-you.md) |
| 2 Research | `solution/docs/research/*.md` (done overnight) |
| 3 Architecture | [step-03-architecture-docs.md](step-03-architecture-docs.md) |
| 4 Azure setup | [step-04-azure-setup.md](step-04-azure-setup.md) |
| 5 Mock services | [step-05-mock-services.md](step-05-mock-services.md) |
| 6 Backend core | [step-06-backend-core.md](step-06-backend-core.md) |
| 7 Voice agent + browser voice page | [step-07-voice-agent.md](step-07-voice-agent.md) |
| 8 Reset form | [step-08-reset-form.md](step-08-reset-form.md) |
| 9 Phone (optional) | [step-09-phone.md](step-09-phone.md) |
| 10 Transcripts | [step-10-transcripts.md](step-10-transcripts.md) |
| 11 Reliability | [step-11-reliability.md](step-11-reliability.md) |
| 12 Testing | [step-12-testing.md](step-12-testing.md) |
| 13 Deploy + version | [step-13-deploy.md](step-13-deploy.md) |
| 14 Documentation | [step-14-documentation.md](step-14-documentation.md) |
| 15 Submit | [step-15-submit.md](step-15-submit.md) |
| 16 Video | [step-16-video.md](step-16-video.md) |

---

## 10. Suggested schedule (deadline Monday 2026-10-05)

| When | Work |
|---|---|
| **Sat morning** | Answer the questions (30 min). You: create the subscription, run `az login`. Me: install Azure CLI, step 4 (Azure setup), and the solution skeleton. |
| **Sat** | Step 5 (mocks) and step 6 (backend core), with tests. Deploy the mocks early. |
| **Sat evening / Sun morning** | Step 7 (voice agent + browser page). **First real conversation.** Step 8 (reset form). |
| **Sun** | Step 10 (transcripts), step 11 (reliability), step 12 (tests + guardrail scenarios), step 13 (deploy). Try step 9 (phone) if the number is available. |
| **Mon** | Step 14 (docs), final checks against the requirements checklist, step 15 (submit). |
| After feedback | Step 16 (video). |

**Cut line if time runs short**, in this order: phone (9) → manual restart test on a
second copy → language/polish extras. Never cut: security rules, truthful outcomes,
the reset form, docs.

---

## 11. Who does what in Azure

- **The owner:** creates the subscription, runs `az login` in the terminal (an
  interactive Microsoft sign-in; no keys pasted into chat), and gives the account
  **Owner** on the subscription. That's needed to assign roles to the managed
  identities. If the phone number is attempted, the owner also activates the ACS
  trial number in the portal (it may need a verification SMS to the owner's
  phone).
- **Claude:** installs the Azure CLI (with OK), runs the Bicep deployment, assigns
  roles, sets app settings, puts secrets into Key Vault (generated randomly, never
  printed into chat or files), deploys code, verifies, and writes SETUP.md from the
  commands actually run.
- **Secrets:** generated in the terminal straight into Key Vault. The voice page
  access code and inbox passwords are then **read by the owner** from Key Vault
  (one command, shown in SETUP.md) to share privately with the interviewer.
