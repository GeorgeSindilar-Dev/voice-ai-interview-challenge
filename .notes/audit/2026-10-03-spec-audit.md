# Audit: solution versus the specification (2026-10-03)

Scope: `solution/` at branch `ccr-cdb850f7-bdrj41` (main + the page design commits), compared with
`README.md`, `docs/mock-contract.md` and `contracts/submission.schema.json`. Read-only review of the
code; nothing was built or run (no .NET SDK in the review environment), and no live call was made.
Status words: **Met**, **Partly met**, **Not met**. Severity of a gap: Critical (blocks a valid
submission), High (visible to the assessors' tests), Medium, Low.

## 1. Summary

The core of the challenge is in place and mostly matches the contract: the backend state machine
decides, the tools are narrow, the mock issuer implements codes, attempts, expiry, throttling,
decoys, links, receipts and tickets, and success is only spoken from a receipt. The largest gaps
are outside the core logic:

| # | Gap | Severity |
|---|---|---|
| G1 | No phone endpoint, and the planned Twilio number does not satisfy "ACS or Teams telephony" | Critical |
| G2 | No setup document (`SETUP.md`) and no `architecture.md` | Critical |
| G3 | The live journey is not verified end to end; Voice Live rejects part of `session.update` | High |
| G4 | A reset finished after the call ended is only reconciled at the next process start | High |
| G5 | `GET /v1/reset-operations/{operation_id}` (contract route) is missing | Medium |
| G6 | An ambiguous code verification can leave the session stuck | Medium |
| G7 | No silence/no-input handling on the call | Medium |
| G8 | Transcript retention is not limited (no lifecycle rule) | Low |

## Owner review (2026-10-04)

The owner's comments on each gap (spelling tidied, meaning unchanged), with the decision and the
answers to the owner's questions.

| # | Owner's comment | Decision |
|---|---|---|
| G1 | "Will use Twilio; I've talked with the interviewer and it is OK." | **Accepted deviation**, agreed with the interviewer. State it in the submission `notes` and the setup doc. |
| G2 | "Will be done after we finish the implementation." | **Later**, after the implementation. |
| G3 | "Have you tested the voice, or why did you say this?" | **Answer:** no, the audit didn't test the voice (there's no .NET SDK in the review environment and no network access to the app). The finding comes from the project notes: `.notes/context.md` ("Open issue: Voice Live answers our `session.update` with `invalid_session_update_message`…") and `solution/docs/remaining-work.md` section 2 ("Still to verify: … the full journey …"). If the error has been fixed since, this gap is only "run the journey once and record it". The silent-call part is fixed under adversarial H3. |
| G4 | "This we have to fix." | **To fix.** Same as adversarial H1. |
| G5 | "We have to fix this, but what does the reset operations do?" | **To fix.** See the explanation below. Same as adversarial M7. |
| G6 | (owner: "to fix", given on adversarial M1) | **To fix.** Same as adversarial M1. |
| G7 | No comment yet. | Open. |
| G8 | Related comment on adversarial M5: "the purpose is to not store sensitive data in the transcripts". | Open. The masking gaps in adversarial M6 are the part that matters for that goal. |

### Resolution (2026-10-04, commit bdfc66a)

| # | Result |
|---|---|
| G1 | Twilio, agreed with the interviewer: say so in `notes` and `SETUP.md`. Buy a US toll-free number (the schema only accepts 800/833/844/855/866/877/888). |
| G2 | Later, after the implementation. |
| G3 | **Fixed:** see adversarial H3 (audio was sent before the settings). Still to do: one full live journey. |
| G4 | **Fixed:** see adversarial H1. |
| G5 | **Fixed:** route added (service credential or `ResetToken`), used by the form. |
| G6 | **Fixed:** see adversarial M1. |
| G7 | **Fixed:** 30 s silence → "Are you still there?", 30 s more → goodbye (`no_input`). |
| G8 | **Fixed:** transcripts deleted after 7 days. |
| `.gitignore` | `.claude/` added. |
| Section 6 "Not documented" rows, single instance, simplified `/resets` | Go into `SETUP.md` known limitations. |

### What `GET /v1/reset-operations/{operation_id}` does (G5)

Each reset submission from the form is one *operation*, identified by its `operation_id`. This route
answers "what happened to that operation?": `{operation_id, recovery_id, status, reset_receipt,
unlock_status, reason_code}`, where `status` is `pending`, `succeeded` or `failed`.

It exists for one situation: the form sends `POST /v1/resets`, and the answer is lost (timeout,
network error, the tab closed). The password may or may not have been changed. Instead of guessing,
the client asks the issuer by operation ID:

- **The browser** asks with `Authorization: ResetToken <token>`. The token only lets it read its
  own operation, even after the token is used or expired; it can't start a new reset.
- **The backend** asks with the service credential, without ever seeing the token or the password.

What this means here: the voice agent already settles the same question through
`GET /v1/recoveries/{id}` (`reset_receipt`), so the route mainly helps the reset form. After a
network error, the form can show the real result instead of "it may already be changed — tell the
assistant". The form should also keep the same `operation_id` when it retries the same submission
(adversarial M7). It is also a listed contract route, so an assessor reading the contract may call
it.

## 2. Submission package (README "Deliver privately", schema)

| Requirement | Status | Evidence / gap |
|---|---|---|
| `phone_e164`: an assigned US toll-free number (schema pattern `+1(800|833|844|855|866|877|888)…`) | **Not met** (G1) | No phone channel exists. Without a toll-free number, a schema-valid submission can't be made. |
| `reset_base_url`: HTTPS, no query/fragment | Met | `https://<app>/reset/`; the issuer appends `#token=` (`Mock/MockIssuer.cs`, `SendLink`). |
| `repository_url`, `commit_sha` | Met (later) | Public GitHub repo; `/health` returns the commit (`Health/HealthEndpoints.cs`), and `deploy.ps1` waits for it. |
| `azure_architecture` ≤ 500 chars | Not yet written | To do at submission time. |
| `setup_document`: a tracked file | **Not met** (G2) | `solution/docs/` contains only `remaining-work.md`. |
| `submission.json` never committed | Met | `/submission.json` is in the root `.gitignore`. |
| Health/version evidence of the deployed SHA | Met | `/health` → `{status, commit}`. |
| `video_url` at walkthrough stage | Deviation (owner decision) | The notes say the result is discussed in a meeting, not a video. The README gives 40 of 100 points to video reasoning; make sure the interviewer confirmed the meeting replaces it. |

## 3. Stack (README "Your task")

| Requirement | Status | Evidence / gap |
|---|---|---|
| Azure speech/LLM | Met | Azure Voice Live in model mode (`Voice/VoiceLiveConnection.cs`, `VoiceLiveSettings.cs`). |
| ACS or Teams telephony, inbound US toll-free endpoint the interviewer can call | **Not met** (G1) | Only the browser channel exists (`Voice/BrowserAudioChannel.cs`). The plan is a Twilio number. The README says "an arbitrary non-Azure managed voice platform does not satisfy the stack requirement" and "the interviewer supplies or allocates usable sandbox telephony". **Recommendation:** ask the interviewer for the sandbox number/ACS resource the README promises before building on Twilio; if Twilio is used anyway, state the deviation plainly in `notes`. |
| Synthetic accounts and mock systems only | Met | Three synthetic users; no Entra writes. |
| Template licence/attribution | Met (not reused) | No code from the gallery template was found. |

## 4. The journey (README "End-to-end journey")

| Step | Status | Evidence / gap |
|---|---|---|
| 1. Identifier, no enumeration | Met | Decoy recoveries for unknown users (`StartRecovery`), same phrase for all (`Phrases.CodeSent`: "If that account is enrolled…"). |
| 2. Code to the registered inbox, separate inbox credential; caller speaks the code | Met | Inbox is a Razor page with its own cookie (`Pages/Mock/Inbox`, `MockInboxAuth`). |
| 3. Link only after issuer verification, to the same inbox, never disclosed; service credential can't read inbox/codes/tokens | Met at the API boundary | No issuer route returns inbox data, codes or tokens. Note: the inbox (codes and token links) is stored in plain text in `state/mock/state.json`, which the app's managed identity can read. The boundary is the HTTP API, not the storage account; say so in the docs. |
| 4. Password typed privately, sent over HTTPS; backend enforces policy; only safe violations reach the agent | Met | `wwwroot/reset/reset.js` → `/mock/v1/password/validate` and `/mock/v1/resets`; policy in `Mock/PasswordPolicy.cs`. Policy violations never reach the voice agent at all (the form shows them). |
| 5. Success only from a receipt (plus unlock); reconcile ambiguous completion | Partly met (G4, G5) | `CheckResetStatusAsync` requires `completed` + `reset_receipt`. But reconciliation after the call ended only runs in `StartupCheck` (once per process start), and the browser has no operation-status route. |
| 6. Truthful ticket, honest escalation (browser unavailable, exhausted, human), no fake transfer | Partly met | Tickets are created after the recovery starts and updated on every outcome. Gaps: a caller who "can't use a browser" is recorded as `human_requested`, never `browser_unavailable`; a call that ends with a live link is recorded as `cancelled`/`call_dropped` even though the reset can still complete (see G4); a time-limit end is recorded as `call_dropped`. |

## 5. Security rules (README + contract)

| Requirement | Status | Evidence / gap |
|---|---|---|
| Backend enforces state transitions and binds to account/recovery | Met | `RecoveryWorkflow` checks state per tool; the session ID comes from the WebSocket, not the model (`ToolDispatcher`). |
| Caller ID, employee IDs, session IDs not authorization | Met | Only the inbox code verifies; prompt says so too. |
| Codes expire after 120 s; two completed wrong submissions exhaust; fragments are not attempts | Met | `s_codeLifetime`, `MaxAttempts = 2`, `SpokenInput.Code` rejects anything but 6 digits before any issuer call. Tests: `Verify_After120Seconds_ReturnsRecoveryExpired`, `Verify_TwoWrongCodes_ExhaustsRecovery`, `SubmitCode_Fragment_IsNotSubmitted`. |
| Links: ≥128-bit tokens, 10 min, single-use, bound, replay-safe, idempotent | Met | 32 random bytes, `s_linkLifetime`, `token_used`, same-operation replay. Tests cover expiry and double use. |
| Never solicit or repeat passwords | Met (prompt) | Prompt rule plus fixed phrases; not enforceable in code in model mode. |
| Browser passwords never in model/tools/transcripts/logs | Met | Passwords only go from the form to the mock reset routes; no request-body logging is enabled. |
| Codes not retained in general logs | Met | Logs carry IDs, tool names and statuses only (`VoiceLog`). Transcripts mask digit runs. |
| Form removes `#token` from the address bar; no referrer leakage | Met | `history.replaceState`, `Referrer-Policy: no-referrer`, `<meta name="referrer">`. |
| Reject wrong types and unknown fields | Met | `MockJson.Options` = `JsonSerializerOptions.Strict`. |
| Browser routes protected against cross-site requests | Met | JSON content type required and no CORS, so a cross-site form can't post; cookies are `SameSite=Strict`; the voice socket checks `Origin`. |

## 6. Mock contract routes (contract "Candidate-facing API")

| Route | Status | Notes |
|---|---|---|
| `POST /v1/recoveries` | Met | 202 envelope, decoys, `request_id` replay, throttle with `Retry-After`. |
| `POST /v1/recoveries/{id}/verify` | Met | `Idempotency-Key` required; 422/409/410 as specified; malformed requests are 400 and don't count. |
| `POST /v1/recoveries/{id}/reset-link` | Met | One link per recovery; other operation → 409 `invalid_state`. |
| `GET /v1/policy` | Met | |
| `POST /v1/password/validate` | Met | Doesn't consume the token. |
| `POST /v1/resets` | Met, simplified | Always completes synchronously (200 `succeeded`); `202 pending`, `failed`, `reset_pending` and `reset_failed` are never produced, so those paths are untested against a real response. Idempotent retry compares the password hash with the account's *current* password, so a legitimate retry after a later reset of the same account returns `idempotency_conflict`. |
| `GET /v1/reset-operations/{operation_id}` (S or matching `ResetToken`) | **Not met** (G5) | Not mapped. The browser therefore can't check an ambiguous reset itself, and the service can't look up an operation by ID. |
| `GET /v1/recoveries/{id}` | Met | |
| `POST /v1/tickets`, `POST /v1/tickets/{id}/outcome` | Met | One ticket per recovery, receipt check for `resolved`, success never overwritten, human request preserved. A preserved human-requested escalation silently ignores the later update without adding it to the history. |
| Namespace rate limits "documented by the adapter"; dedup retention "documented by the adapter" | Not documented | Records are kept forever (no retention) and there's no namespace-wide rate limit; both should be stated in the setup doc. |

## 7. Reliability (README "What we exercise")

| Area | Status | Evidence / gap |
|---|---|---|
| Ambiguous/invalid speech | Met | `SpokenInput` returns null instead of guessing; read-back confirmation is in the prompt. |
| Timeouts | Partly met (G6, G7) | Issuer timeouts are treated as "unknown" and keys are kept for replay (good). Gaps: no silence/no-input reprompt or idle end (G7); after an ambiguous verify the workflow never asks the issuer for the recovery status, so it can get stuck (G6, details in the adversarial audit). |
| Cancellation / dropped calls | Partly met (G4) | Ticket updated on cancel and drop; no revoke claimed (`Phrases.CancelledAfterLink`). But an accepted reset after a drop is only reconciled at the next restart. |
| Duplicate events | Met | `request_id`, `Idempotency-Key`, `operation_id` used per logical action; replays recorded. |
| Concurrent sessions | Met (single instance) | Per-session state and lock; one active recovery per account (`StartRecovery_SecondSessionSameAccount_DoesNotShareRecovery`). Locks are in-process, so this holds only on one instance. |
| Process restarts | Met for the issuer, partly for calls | Issuer state is in Blob Storage; `StartupCheck` reconciles open sessions. A live call is lost on restart (documented in `remaining-work.md`). |
| Instructions for an isolated deployment | **Not met** (G2) | No setup doc. Also `setup-azure.ps1`/`deploy.ps1` hard-code the resource group `rg-voicereset`, so a second isolated copy in the same subscription is awkward; and a local run uses the in-memory store, so restart testing needs Azure Storage. |

## 8. Conversation quality

| Area | Status | Notes |
|---|---|---|
| Clear guidance, honest outcomes | Met | Fixed backend sentences (`Recovery/Phrases.cs`), automated-assistant disclosure, no transfer claims. |
| Barge-in | Configured, not verified live | Semantic VAD with `InterruptResponse` and `AutoTruncate`; the page drops unplayed audio on `clear`. |
| Filtered/failed responses | Met | Fixed safe line, said once (`RunAsync_FilteredResponse_SaysSafeLineOnce`). |
| Max call duration | Met | `Limits:MaxCallSeconds` (600 s) with a goodbye line. |
| Latency | Not measured | Tool calls run inside the event loop, so a slow issuer call also delays audio forwarding; there's no "one moment" line. |
| Live behaviour | **Not verified** (G3) | `remaining-work.md` lists the open `invalid_session_update_message` error (the first call failed with `max_config_attempts_exceeded`) and a long list of journeys still to test. If the session update fails, `SessionUpdated` never arrives and the agent never greets: the caller hears silence. |

## 9. Engineering quality

| Area | Status | Notes |
|---|---|---|
| Understandable architecture | Met | Small feature folders, options validated at startup, narrow interfaces only at I/O boundaries. |
| Tests | Met | About 57 test methods (81 cases with theories) on the core rules, fake clock, in-memory store. Missing: reconciliation after a call ends without a restart, the ambiguous-verify path, the reset form's ambiguous path. |
| Build reproducibility | Partly met | `Directory.Build.props` treats warnings as errors in Release. No CI; build and deploy rely on Windows PowerShell scripts. |
| Operational notes | **Not met** (G2) | Only `remaining-work.md` and `.notes/`. |
| Repo hygiene | Low | The root `.gitignore` doesn't list `.claude/`, which `CLAUDE.md` says must never be committed. |

## 10. Suggested order of work before the deadline

1. G1: settle the phone path with the interviewer (their sandbox number/ACS), then build the second
   `IAudioChannel`.
2. G3: fix the `session.update` rejection and run the full journey and the failure paths live.
3. G2: write `SETUP.md` (with the limitations below) and `architecture.md`.
4. G4: reconcile on call end (ask the issuer for the recovery status before writing the ticket
   outcome; record `pending`/`completion_unknown` while a link is live), or run the reconciliation
   periodically, not just at startup.
5. G5, G6, G7, G8 as time allows; otherwise list them as known limitations.
