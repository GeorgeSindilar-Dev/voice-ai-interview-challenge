# Reconciliation: decisions that override the task plans

The task plans were written in parallel. Where a plan disagrees with this file, **this file wins**.
Implementers: read this file and `00-contracts.md` before your task plan.

## Before T2
- **Review T1** (already committed as `d4198a3`): run the spec and code-quality review on it first.

## Owner decision (2026-10-03)
- **Desktop only.** No mobile/responsive work, no phone-width checks, no mobile review findings. Desktop Chrome/Edge is the target.

## Names and settings
1. **Demo users** everywhere (dev settings, tests, T15 script): `alex.morgan` (no unlock), `jamie.lee` (no unlock),
   `sam.taylor` (`RequiresUnlock = true`). Replace `alice`/`bob` (T3–T5) and `alex.demo`/`blake.demo`/`casey.demo` (T15).
2. **`LimitsOptions`** is defined once, by **T6**, in `src/VoiceReset/Voice/LimitsOptions.cs` (namespace `VoiceReset.Voice`,
   section `Limits`, `MaxCallSeconds = 600`) and registered by `AddRecovery()`. T11's `Voice/VoiceOptions.cs` holds only
   `VoiceLiveOptions` and must not register `LimitsOptions` again.
3. **Local URLs:** T2 adds `src/VoiceReset/Properties/launchSettings.json` with one profile: `https://localhost:7180;http://localhost:5180`,
   `ASPNETCORE_ENVIRONMENT=Development`. Dev settings: `Issuer:BaseUrl = https://localhost:7180/mock/`,
   `Mock:ResetBaseUrl = https://localhost:7180/reset/`, `Access:AllowedOrigin = https://localhost:7180`. All dev secrets are `dev-only-…`.
4. **Azure settings (T15)** add `Access__AllowedOrigin = https://<app>.azurewebsites.net`,
   `Issuer__BaseUrl = https://<app>.azurewebsites.net/mock/`, `Mock__ResetBaseUrl = https://<app>.azurewebsites.net/reset/`.
   No `ASPNETCORE_FORWARDEDHEADERS_ENABLED` (T14 calls `UseForwardedHeaders` outside Development).
   Verify the runtime string (`az webapp list-runtimes --os linux`) and the role names at run time.

## Behaviour
5. **End-of-call reasons** (`EndCallAsync(sessionId, reason)`): `agent_ended` (end_call tool / goodbye), `call_dropped`
   (socket closed, Voice Live failure), `time_limit`. Ticket mapping when the ticket isn't final yet:
   `agent_ended` → `cancelled`/`caller_cancelled`; `call_dropped` and `time_limit` → `cancelled`/`call_dropped`.
6. **Human requested:** a ticket escalated with `human_requested` is **never** changed to `resolved` (contract). T4's mock keeps
   it (200, unchanged) and T6/T7 don't try.
7. **Resets** always complete immediately with `200 succeeded` (T4). No pending/failed, no `GET /reset-operations`.
8. **Containers:** the app creates `state` (T2) and `transcripts` (T8) if missing; the T15 script also creates them.
   No transcript retention rule (debug aid; document it).
9. **Access rate limit (T10):** too many attempts → `200 {"ok":false,"tooManyAttempts":true}` (no 429, so the console stays clean).
10. **T6 tests read the inbox** via T3's `MockIssuer.GetInboxAsync(username)` from DI, not by parsing `mock/state.json`.
12. **T6 (from T4 review):** after a ticket outcome update, store the outcome the mock **returned** (`result.Value.Outcome`),
    not the one requested (a human-requested escalation stays escalated). A 409 on an outcome update is not "couldn't create
    a ticket": the ticket exists, so keep the escalation wording honest.
13. **T14 (from T5 review):** also set the antiforgery cookie to Secure:
    `builder.Services.AddAntiforgery(o => o.Cookie.SecurePolicy = CookieSecurePolicy.Always);`.
11. **T9:** add the one-line test that `SystemPrompt.Text` loads. `request_human()` stays without parameters.

## Actual code after T6 (use these real names in T7+)
- Options classes use `const string SectionName` + static `IsValid` (not `Section`/DataAnnotations): `IssuerOptions.SectionName = "Issuer"`,
  `VoiceReset.Voice.LimitsOptions.SectionName = "Limits"` (MaxCallSeconds 600, range 60–3600), `MockOptions`, `StorageOptions`.
- `RecoveryWorkflow(SessionStore, IssuerClient, TimeProvider, ILogger<RecoveryWorkflow>)`: StartSessionAsync(channel), GetStateAsync,
  StartRecoveryAsync(sessionId, spokenUsername), SubmitCodeAsync(sessionId, spokenCode), SendResetLinkAsync, CheckResetStatusAsync,
  RequestHumanAsync, CancelAsync, EndCallAsync(sessionId, reason). Private helpers RunAsync / CompleteAsync / RecordOutcomeAsync (returns Task).
- `ToolResult(bool Ok, string Status, string Say)`; statuses: not_allowed, invalid_username, not_started, code_sent, invalid_code,
  code_incorrect, verified, exhausted, expired, link_sent, not_confirmed, completed, reset_failed, escalated, cancelled, unavailable.
- `Phrases` (Recovery/Phrases.cs): CodeSent, UsernameUnclear, CantStartNow, CodeUnclear, Verified, CodeIncorrect, Exhausted, Expired, LinkSent,
  Completed, CompletedWithUnlock, CantConfirmYet, ResetFailed, LinkExpired, HumanRequested, TicketCreated, TicketNotCreated, Cancelled,
  CancelledAfterLink, NotAvailableNow, NotAllowed. (T9 adds InvalidArgument, Goodbye.)
- (after review fix cdb6c74) extra statuses: `no_ticket` (RequestHuman before a recovery), `unavailable` also from StartRecovery.
- `IssuerClient` methods return `IssuerResult<T>(IssuerOutcome Outcome, T? Value, string? ErrorCode)`;
  DTO `VoiceReset.Recovery.RecoveryStatus` clashes by name with static class `VoiceReset.Mock.RecoveryStatus` → alias when both are imported.
- `CallSession` fields: SessionId, Channel, StartedAt, State, EndedAt, Username, RequestId, RecoveryId, VerifyKey, LinkOperationId,
  TicketOperationId, TicketId, OutcomeOperationId, TicketOutcome, TicketReason, ResetReceipt, UnlockStatus, EndReason; `[JsonIgnore] IsOpen`.
  `SessionStore`: GetAsync, SaveAsync, ListOpenAsync. Session IDs: Guid "N" (32 hex).
- Test helper `tests/VoiceReset.Tests/Recovery/RecoveryAppFactory.cs`: `RecoveryAppFactory(InMemoryJsonStore? store = null)` with Username,
  Store, Time, Workflow, StartRecoveryAsync, SessionAsync, InboxCodeAsync, InboxLinkAsync, CompleteResetAsync. `AddRecovery()` registers
  Issuer + Limits options, client (10 s timeout), SessionStore, RecoveryWorkflow, TimeProvider.

## Actual code after T7/T8
- T7: `RecoveryWorkflow.ReconcileAsync(sessionId, maxCallAge, ct)` → Task<bool>; `StartupCheck` BackgroundService registered by AddRecovery.
- T8 (namespace VoiceReset.Transcripts): `TranscriptMasker.Mask(string)`; `TranscriptTurn(Role, OffsetMs, Text)`;
  `TranscriptDocument(SessionId, Channel, StartedAt, EndedAt, EndReason, Turns)`; `TranscriptRecorder(string sessionId, string channel,
  TimeProvider time)` with AddCaller/AddAgent (masked on add, thread-safe, blanks ignored) and ToDocument(endReason);
  `ITranscriptWriter.SaveAsync(doc, ct)`; `NullTranscriptWriter`; `BlobTranscriptWriter` (create-only; 409 RequestFailedException if it exists
  → T11 catches and logs by type only); `services.AddTranscripts()` (no IConfiguration; call after AddJsonStore). T11 creates
  `new TranscriptRecorder(sessionId, "browser", timeProvider)` and injects `ITranscriptWriter`.

## Actual code after T9
- `SystemPrompt.Text` (embedded `VoiceReset.Voice.system-prompt.md`); `ToolDefinitions` consts (StartRecovery…EndCall) + `All`
  (IReadOnlyList<VoiceLiveFunctionDefinition>); `ToolDispatcher(RecoveryWorkflow, ILogger<ToolDispatcher>)` registered transient:
  `Task<ToolResult> DispatchAsync(sessionId, toolName, argumentsJson, ct)` (never throws except cancellation), `static ToOutputJson(ToolResult)`,
  consts `InvalidArgumentStatus`, `CallEndingStatus` ("call_ending" → T11 says goodbye and ends the call), `AgentEndedReason`.
  `Phrases.InvalidArgument`, `Phrases.Goodbye` exist. Azure.AI.VoiceLive is referenced by the app.

## Actual code after T10
- `VoiceReset.Access.AccessOptions` (SectionName "Access", Code, AllowedOrigin); `AccessGate` consts Scheme="Access", Policy="Access",
  CookieName="__Host-vr-access", RateLimitPolicy="access-code"; `AddAccessGate(this IServiceCollection, IConfiguration)`;
  `MapAccess()`; `UseRateLimiter()` after UseAuthorization (its OnRejected writes the access JSON body — don't reuse that limiter policy).
  T11: `/voice/ws` uses `.RequireAuthorization(AccessGate.Policy)`; `WebSocketOptions.AllowedOrigins` from `AccessOptions.AllowedOrigin`.
  `RecoveryWorkflow.UnavailableStatus = "unavailable"`.

## Notes from the T10 review
- **T14 forwarded headers (Important):** call `UseForwardedHeaders` FIRST (before UseRateLimiter), outside Development, with
  `ForwardedHeaders.XForwardedFor | XForwardedProto`, clear `KnownIPNetworks`/`KnownNetworks` and `KnownProxies` (App Service front end
  isn't loopback), keep `ForwardLimit = 1`. Otherwise the /access rate limit is shared by all callers.
- **T11:** normalise `AccessOptions.AllowedOrigin` with `new Uri(value).GetLeftPart(UriPartial.Authority)` before putting it into
  `WebSocketOptions.AllowedOrigins` (a trailing slash would otherwise refuse every socket).

## Notes for T16 (SETUP.md) from the T14/T15 reviews
- Scripts: `solution/scripts/setup-azure.ps1` (params SubscriptionId, Suffix, Location=swedencentral; names rg-voicereset,
  stvoicereset<Suffix>, ai-voicereset-<Suffix>, app-voicereset-<Suffix>; prints how to read Access__Code / inbox passwords with
  `az webapp config appsettings list`), `solution/scripts/deploy.ps1` (clean-tree check, tests, publish, zip, az webapp deploy, poll /health).
- The signed-in account needs Owner on the subscription (or Contributor + User Access Administrator).
- First start may fail/restart for a few minutes until role assignments propagate (storage access fail-fast).
- Re-running with a suffix whose AI account was soft-deleted needs a purge first (cleanup section).
- Antiforgery Secure cookie only outside Development; in Production a request without X-Forwarded-Proto: https returns 500 on the inbox
  login (App Service always sends it).
- Dev error pages show CSP console errors (Development only, cosmetic).

## Notes from the T6 review for later tasks
- **T9 dispatcher:** catch exceptions from the workflow (unknown session `InvalidOperationException`, store failures) and turn them
  into a safe ToolResult (`unavailable` + `Phrases.NotAvailableNow`); never let them escape to Voice Live. The T9 prompt must enforce
  read-back-and-confirm for both the username and the code (Cancelled is terminal within a call).
- **T11:** call `EndCallAsync` with a token that is not already cancelled (cleanup token), otherwise nothing is recorded.
- **T16 limitations (transcripts):** masking misses some phrasings ("the password I chose is X", "pass word is X", a password said in
  the turn after "my password is"), codes read as pairs ("forty seven"), URLs without a scheme, standalone mixed tokens like "Summer2026";
  it over-masks phone-like numbers. Raw audio/text still passes through Voice Live.
- **T16 limitations:** per-session lock dictionary never pruned; RequestHuman after an escalation keeps the earlier ticket reason;
  `invalid_state` on verify after a lost success response reads as "not responding"; Cancelled is terminal within a call.

## Accepted as-is (document as known limitations in T16)
- Single instance; last-writer-wins on JSON files; a blob write failure leaves memory ahead of storage.
- Startup check runs once per start (no periodic re-check). Cancel after the link was sent, or a dropped call older than
  `MaxCallSeconds`, means a later reset isn't recorded on the ticket.
- Lost reset response → the next attempt gets `token_used` (form says so honestly).
- Transcript masking is pattern-based (over-masks numbers; "password is …" to end of turn).
- Agent-only captions; Chrome/Edge supported; no `ws:` fallback; no rate limit/concurrency cap on `/voice/ws`;
  no inbox login rate limit; inbox refreshes every 5 s.
- Fixed lines use the pre-generated assistant message; fallback "say exactly this sentence" if the live test fails.
- Mock stores code/token hashes (fine for a mock).
