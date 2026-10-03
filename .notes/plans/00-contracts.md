# Shared contracts for all task plans (names, routes, types)

Every task plan uses **exactly** these names. If a plan needs something not listed,
it adds it in its own plan and lists it under "Additions to contracts" at the end.

Lean rule: do what the challenge needs, simply. Tests only for the rules named in each task.

## Status
- **T1 done** (commit `d4198a3`): `solution/` skeleton (global.json with SDK 10.0.302 + MTP runner,
  Directory.Build.props, Directory.Packages.props with Azure.AI.VoiceLive 1.2.0, Azure.Storage.Blobs 12.30.0,
  Azure.Identity 1.21.0, Azure.Monitor.OpenTelemetry.AspNetCore 1.6.0, xunit.v3 4.0.1,
  Microsoft.AspNetCore.Mvc.Testing 10.0.12, Microsoft.Extensions.TimeProvider.Testing 10.10.0; .editorconfig;
  VoiceReset.slnx; `src/VoiceReset` web app; `tests/VoiceReset.Tests`), `GET /health` →
  `{"status":"ok","commit":"<sha>"}` in `Health/HealthEndpoints.cs` (`MapHealth`, `CommitFrom`). 5 tests pass.
  `solution/.gitignore` ignores bin/ obj/ submission.json.

## Task order
| # | Task | Plan file |
|---|---|---|
| T2 | JSON state store (in-memory + Blob) | `t02-json-store.md` |
| T3 | Mock issuer: recoveries, verify, reset link, recovery status | `t03-mock-issuer.md` |
| T4 | Mock: policy, validate, resets, tickets | `t04-mock-reset-tickets.md` |
| T5 | Mock inbox pages (login, messages) | `t05-mock-inbox.md` |
| T6 | Recovery workflow + issuer client + session store | `t06-recovery-workflow.md` |
| T7 | Startup check for open sessions (restart) | `t07-startup-check.md` |
| T8 | Transcripts (masker, recorder, writer) | `t08-transcripts.md` |
| T9 | Tools, system prompt, tool dispatcher | `t09-tools-prompt.md` |
| T10 | Access gate (access code → cookie) | `t10-access-gate.md` |
| T11 | Voice session + browser audio channel + `/voice/ws` | `t11-voice-session.md` |
| T12 | Agent page (HTML/JS/AudioWorklet) | `t12-agent-page.md` |
| T13 | Reset form page | `t13-reset-form.md` |
| T14 | Logging, Application Insights, security headers | `t14-logging-headers.md` |
| T15 | Azure setup + deploy scripts (written, not run) | `t15-azure-scripts.md` |
| T16 | Docs: SETUP.md + architecture.md | `t16-docs.md` |

## Configuration (options classes, validated at startup with `ValidateOnStart`)
| Section | Class | Keys |
|---|---|---|
| `Mock` | `VoiceReset.Mock.MockOptions` | `ServiceCredential`, `ResetBaseUrl`, `Users` (list of `MockUser`: `Username`, `DisplayName`, `InboxPassword`, `InitialPassword`, `RequiresUnlock`) |
| `Issuer` | `VoiceReset.Recovery.IssuerOptions` | `BaseUrl` (e.g. `https://host/mock/`), `ServiceCredential` |
| `Storage` | `VoiceReset.Storage.StorageOptions` | `BlobEndpoint` (empty → in-memory store) |
| `VoiceLive` | `VoiceReset.Voice.VoiceLiveOptions` | `Endpoint`, `Model` (gpt-4.1-mini), `Voice` (en-US-Ava:DragonHDLatestNeural) |
| `Access` | `VoiceReset.Access.AccessOptions` | `Code` |
| `Limits` | `VoiceReset.Voice.LimitsOptions` | `MaxCallSeconds` (600) |
`appsettings.Development.json` holds obviously fake local values (e.g. `dev-only-...`) so the app runs locally.
Shared Azure credential: one `TokenCredential` singleton = `new DefaultAzureCredential()` (registered by T2, reused by T11).

## Storage (T2) — namespace `VoiceReset.Storage`
```csharp
public interface IJsonStore
{
    Task<T?> ReadAsync<T>(string key, CancellationToken ct) where T : class;
    Task WriteAsync<T>(string key, T value, CancellationToken ct) where T : class;
    Task<IReadOnlyList<string>> ListKeysAsync(string prefix, CancellationToken ct);
}
public sealed class InMemoryJsonStore : IJsonStore;                  // tests + local dev
public sealed class BlobJsonStore(BlobContainerClient container) : IJsonStore;   // container "state"
public static class StorageServiceCollectionExtensions { public static IServiceCollection AddJsonStore(this IServiceCollection services, IConfiguration configuration); }
```
Keys: `mock/state.json`, `sessions/<sessionId>.json`. Stored JSON: System.Text.Json web defaults (camelCase).

## Mock (T3, T4, T5) — namespace `VoiceReset.Mock`, routes under `/mock`
- Contract routes from `docs/mock-contract.md`, prefixed with `/mock`:
  `POST /mock/v1/recoveries`, `POST /mock/v1/recoveries/{id}/verify`, `POST /mock/v1/recoveries/{id}/reset-link`,
  `GET /mock/v1/recoveries/{id}`, `GET /mock/v1/policy`, `POST /mock/v1/password/validate`, `POST /mock/v1/resets`,
  `POST /mock/v1/tickets`, `POST /mock/v1/tickets/{id}/outcome`.
- Service routes need `Authorization: Bearer <Mock:ServiceCredential>` (constant-time compare) → else `401`.
  Browser routes (`policy`, `password/validate`, `resets`) need no service auth (the token is in the body).
- JSON: `snake_case_lower`, unknown members rejected → `400 invalid_request`. Errors: `{"error":{"code":"…","message":"…"}}`
  plus top-level `attempts_remaining`/`status` for verify errors, `violations` for policy errors, `Retry-After` header on 429.
- Classes: `MockOptions`, `MockUser`, `MockState` (all mutable mock data; persisted as one document `mock/state.json`
  after every change), `MockIssuer` (rules; one `SemaphoreSlim` around every operation), `MockJson` (serializer options),
  `MockIssuerEndpoints.MapMockIssuer(...)` (T3) and `MockResetEndpoints.MapMockReset(...)` (T4).
- IDs: `rec_<32 hex>`, `tkt_<recoveryId>`, `rcpt_<32 hex>`, `msg_<32 hex>`. Code: 6 random digits (string, leading zeros kept),
  stored only as SHA-256 hex. Token: 32 random bytes base64url, stored only as SHA-256 hex.
- Inbox message: `InboxMessage(string Id, string Username, DateTimeOffset CreatedAt, string Subject, string Body, string? Link)`.
- Inbox pages (T5): Razor Pages `/mock/inbox/login` and `/mock/inbox`, cookie scheme `MockInbox`, cookie `__Host-mock-inbox`.

## Recovery (T6, T7) — namespace `VoiceReset.Recovery`
```csharp
public enum RecoveryState { AwaitingUsername, AwaitingCode, Verified, LinkSent, Completed, Escalated, Cancelled }
public sealed record ToolResult(bool Ok, string Status, string Say);          // Say = exact sentence for the agent
public static class Phrases { /* const string per sentence */ }
public sealed class CallSession { /* SessionId, StartedAt, State, Username, RecoveryId, RequestId, LinkOperationId,
    TicketId, TicketOutcome, TicketReason, EndedAt, EndReason … */ }
public sealed class SessionStore(IJsonStore store)      // GetAsync, SaveAsync, ListOpenAsync
public sealed class IssuerClient(HttpClient http)        // typed client → /mock/v1 with Bearer
public sealed class RecoveryWorkflow(SessionStore sessions, IssuerClient issuer, TimeProvider time)
{
    Task<string> StartSessionAsync(string channel, CancellationToken ct);           // returns sessionId (Guid "N")
    Task<ToolResult> StartRecoveryAsync(string sessionId, string? username, CancellationToken ct);
    Task<ToolResult> SubmitCodeAsync(string sessionId, string? code, CancellationToken ct);
    Task<ToolResult> SendResetLinkAsync(string sessionId, CancellationToken ct);
    Task<ToolResult> CheckResetStatusAsync(string sessionId, CancellationToken ct);
    Task<ToolResult> RequestHumanAsync(string sessionId, CancellationToken ct);
    Task<ToolResult> CancelAsync(string sessionId, CancellationToken ct);
    Task EndCallAsync(string sessionId, string reason, CancellationToken ct);       // idempotent
    Task<RecoveryState> GetStateAsync(string sessionId, CancellationToken ct);
}
public sealed class StartupCheck : IHostedService        // T7
```
Ticket outcomes per contract: `resolved` (with receipt, reason `reset_completed`), `escalated`
(`verification_exhausted`, `verification_expired`, `human_requested`, `dependency_unavailable`),
`cancelled` (`caller_cancelled`, `call_dropped`), `pending` (`completion_unknown`).

## Transcripts (T8) — namespace `VoiceReset.Transcripts`
```csharp
public static class TranscriptMasker { public static string Mask(string text); }   // digit runs (incl. spoken digits) → [CODE]; after "password is" → [REDACTED]; URLs → [LINK]
public sealed class TranscriptRecorder(string sessionId, TimeProvider time)        // AddCaller(text), AddAgent(text), ToDocument(endReason)
public sealed record TranscriptDocument(...);
public interface ITranscriptWriter { Task SaveAsync(TranscriptDocument doc, CancellationToken ct); }
public sealed class BlobTranscriptWriter : ITranscriptWriter;   // container "transcripts", name yyyy/MM/dd/<sessionId>.json
public sealed class NullTranscriptWriter : ITranscriptWriter;   // when Storage:BlobEndpoint is empty
```

## Voice (T9, T10, T11) — namespaces `VoiceReset.Voice`, `VoiceReset.Access`
- Tools (exact names): `start_recovery(username)`, `submit_code(code)`, `send_reset_link()`, `check_reset_status()`,
  `request_human()`, `cancel_reset()`, `end_call()`. No IDs or free text besides username/code.
- `SystemPrompt.Text` (embedded resource `Voice/system-prompt.md`), `ToolDefinitions.All`,
  `ToolDispatcher(RecoveryWorkflow)`: `Task<ToolResult> DispatchAsync(string sessionId, string toolName, string argumentsJson, CancellationToken ct)`,
  `static string ToOutputJson(ToolResult r)` → `{"ok":…,"status":"…","say":"…"}`.
- Access: `POST /access` JSON `{"code":"…"}` → `200 {"ok":true}` + cookie `__Host-vr-access` (scheme `Access`), wrong → `200 {"ok":false}`;
  `GET /access/status` → `{"signedIn":bool}`. Only `/voice/ws` requires the cookie.
- `IAudioChannel` (browser now, phone later): `ReadAudioAsync` (PCM16 24 kHz mono frames), `SendAudioAsync`, `StopPlaybackAsync`,
  `SendCaptionAsync(string text)`, `SendEndedAsync(string reason)`, `CloseAsync`.
  Browser wire format: binary frames = PCM16 24 kHz mono; text frames JSON `{"type":"clear"}`, `{"type":"caption","text":"…"}`, `{"type":"ended","reason":"…"}`.
- `IVoiceLiveConnection` wraps the Voice Live SDK session (so `VoiceSession` is testable with a fake). `VoiceSession` = one call.
- `/voice/ws` endpoint (WebSocket, cookie required, `AllowedOrigins` = own origin).
- Verified SDK code to reuse: `.notes/archive/overnight/research/sdk-reference.md` (sections A, C) and
  `.notes/archive/overnight/plans/step-07-voice-agent.md` (compiled against Azure.AI.VoiceLive 1.2.0).

## Pages (T5, T12, T13)
`/` agent page (`wwwroot/index.html`, `js/agent.js`, `js/audio-worklets.js`, `css/site.css`, `favicon.svg`),
`/reset/` (`wwwroot/reset/index.html`, `reset/reset.js`) calling the same-origin `/mock/v1/...` browser routes,
`/mock/inbox` (Razor Pages). No inline scripts/styles; `textContent` only; no console output.
