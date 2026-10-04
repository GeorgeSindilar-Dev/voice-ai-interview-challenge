# Adversarial audit 2 (2026-10-04)

Second pass after the fixes in commit 41867c7 (deployed; `/health` shows that commit). Read-only:
every finding comes from tracing the code. Nothing was built, run or changed, and no request was
sent to the deployed app except `GET /health`. **Confirmed (code)** means the path is clear from
the code; **Needs live test** means it depends on Voice Live, the browser or Azure behaviour.

Focus: the new code (silence watcher, audio gate, error-before-greeting rule, call-end ticket and the
minute check, lost-verify recovery, `GET /mock/v1/reset-operations/{id}`, reset form changes,
prompt changes), then the whole journey again.

Attackers considered (same as audit 1):
- **A1, phone caller** (later), **A2, access-code holder** on the browser voice page.
- **A3, anonymous internet client:** every public route, no cookie or credential.
- **A4, insider** with storage or log access.
- **A5, faults:** timeouts, restarts, duplicate events, races between the call loops and the
  minute check, a caller who goes quiet or drops.
- **A6, the model itself:** prompt injection, or the model skipping or inventing steps.

Settled owner decisions (only noted, not raised again): mock not hardened (H2), single instance
(M2), no call concurrency cap, the issuer throttle can block an account (M4), codes and links in the
storage blob (M5), phone channel later, desktop only.

## Resolution (2026-10-04)

The owner approved the suggested fixes:
- **N1 fixed:** a cancel after the link was sent moves to `CancelledLinkOut`; the open-session check
  keeps watching it and records `resolved` if the link is used, `Cancelled` once it expired.
  (The owner asked whether the form could update the ticket instead: the form only talks to the
  issuer, and the contract has the agent ask the issuer, so the minute check is the place.)
- **N2 fixed:** "Are you still there? If you need more time, just say so."; goodbye after 60 s more.
- **N4 fixed:** no silence check while the caller is speaking.
- **N5 fixed:** the check also waits on `reset_pending`.
- **N9 fixed:** a reset answer without an error code (server error) asks the operation route.
- **N11 fixed:** the ticket is offered only once a reset was started.
- First letter: prefix padding 1000 ms, browser auto gain control off (see the tester feedback probe).
- Watch in the next live test: N3, N10, N12. The other Low items go into the limitations or stay as they are.

## 1. Findings

### High

#### N1. Cancel after the link was sent: a reset finished later is never recorded (A5)
- **Path:** `cancel_reset` in `LinkSent` → `RecoveryWorkflow.CancelAsync` sets `State = Cancelled`
  and records `cancelled/caller_cancelled` (`Recovery/RecoveryWorkflow.cs:155-165`). `Cancelled`
  is not "open" (`Recovery/CallSession.cs:32-33`), so `SessionStore.ListOpenAsync`
  (`Recovery/SessionStore.cs:14-20`) never returns it, and neither `EndCallAsync` nor the minute
  check (`ReconcileAsync`) looks at it again. In the call, `check_reset_status` is refused for a
  cancelled session (`RecoveryWorkflow.cs:130-133`).
- **Result:** the agent rightly says the link stays valid ("can't be withdrawn"). If the caller (or
  whoever holds the inbox) then uses it, the password changes but the ticket stays
  `cancelled/caller_cancelled` for good, and the agent can't confirm the reset if asked. The
  contract names this case: "Cancellation … stops new voice-driven work, not issuer history …
  reconcile operations already accepted", and allows replacing a cancelled outcome with a completed
  one on a matching receipt. Same class as audit 1's H1, which only covered hang-ups.
  **Confirmed (code).**
- **Fix idea:** keep a cancelled-with-link session reconcilable. For example a state
  `CancelledLinkOut` that counts as open for the minute check, is refused by every tool except
  `check_reset_status`, and that `EndCallAsync`/`ReconcileAsync` treat like `LinkSent` but keep the
  `cancelled` outcome until a receipt arrives (then `resolved`), or close it when the link expires.
  Add one test: cancel after link → reset in the browser → minute check → ticket resolved.

### Medium

#### N2. "Take your time" is followed by a hang-up 30 seconds later (A5, caller experience)
- **Path:** after 30 s of quiet the agent says "Are you still there? Take your time, I'm here when
  you're ready.", and after another 30 s of quiet it says goodbye and ends the call as `no_input`
  (`Voice/VoiceSession.cs:26-28`, `131-161`). The journey needs two quiet browser steps: signing in
  to the inbox to read the code, and typing a new password in the form.
- **Result:** a caller who heard "take your time" and keeps typing without answering is cut off.
  With a link out the ticket goes to `pending` and is settled later, so nothing is lost, but the
  caller gets no spoken confirmation, and the form tells them to "return to the call" that is gone.
  Automated assessment callers may also stay silent while their script reads the inbox.
  **Confirmed (code); how often it bites Needs live test.**
- **Fix idea:** make the line honest and the second wait longer, e.g. "Are you still there? Say
  anything if you need more time." with 60 s before the goodbye. Optional: a longer wait while a
  code or link is out (`workflow.GetStateAsync` already exists).

### Low

#### N3. A rejected `response.create` can switch off the silence check (A5)
- **Path:** `_responseActive` is set before every request (`VoiceSession.cs:266-279`) and cleared
  only by `response.done` (`226-229`). An error after the greeting is only logged (`198-204`).
  `CheckSilenceAsync` skips while `_responseActive` is true (`145-148`).
- **Result:** if Voice Live rejects one of our requests (for example a fixed line or a tool
  follow-up that races the service's own answer) and no other response follows, `_responseActive`
  stays true: no silence prompt, no `no_input` goodbye, and the next tool answer waits as "pending".
  A silent or absent caller then holds the session until the 10-minute limit. Usually another
  response clears it, so the trigger is rare. **Needs live test** (check logs for `VoiceLiveError`
  with `conversation_already_has_active_response`).
- **Fix idea:** clear `_responseActive` on an error when no `response.created` arrived since our
  last request, or base the silence check on the time of the last agent audio instead of the flag.

#### N4. Continuous speech or steady noise for 30 s triggers "Are you still there?" (A5)
- **Path:** `_lastHeard` moves on `speech_started` and `speech_stopped` (`VoiceSession.cs:171-178`).
  Between them, nothing moves it.
- **Result:** one long turn (or background noise the VAD keeps as one turn) gets the prompt over the
  caller's voice, and 30 s later the `no_input` goodbye while they are still talking.
  **Confirmed (code); Needs live test** for how the semantic VAD treats noise.
- **Fix idea:** a `_callerSpeaking` flag set on `speech_started`, cleared on `speech_stopped`; skip
  the check while it is set.

#### N5. The minute check gives up on `reset_pending` and on errors (A5)
- **Path:** `ReconcileAsync` waits only on `Unavailable` and `link_issued`
  (`RecoveryWorkflow.cs:210-229`). `reset_pending`, `reset_failed` and an error such as
  `not_found` all fall through to "close as cancelled".
- **Result:** with our mock this never happens (resets complete at once, records are never
  deleted). With another issuer, a pending reset that completes later is never recorded, and a
  temporary `not_found` (which the contract says is not proof) closes the session. Deleting the
  whole `state/mock/state.json` while sessions are open also closes them. **Confirmed (code).**
- **Fix idea:** treat `reset_pending` like `link_issued`, and `IssuerOutcome.Error` like
  `Unavailable` (try again next minute). Two lines.

#### N6. A failed ticket update after a final state is never retried (A5)
- **Path:** `CompleteAsync`, `EscalateAsync` and `CancelAsync` set the final state first, then call
  `RecordOutcomeAsync`, which only logs a failure (`RecoveryWorkflow.cs:235-255`, `280-297`). Final
  sessions are not open, so the minute check skips them.
- **Result:** if the issuer times out at that moment, the session says `Completed` (and the caller
  may hear "your password has been reset", which is true) but the ticket stays `open` or `pending`
  for good. The issuer runs in the same app, so this needs the app itself to be struggling.
  **Confirmed (code).**
- **Fix idea:** document. If wanted: keep the wanted outcome on the session when the update fails,
  and let the minute check retry it.

#### N7. Asking for a person after the link was sent: the agent can't confirm a later reset (A2)
- **Path:** `request_human` in `LinkSent` → `Escalated`; `check_reset_status` refuses `Escalated`
  (`RecoveryWorkflow.cs:130-133`, `146-153`).
- **Result:** the ticket correctly stays `escalated/human_requested` (contract), but a caller who
  then finishes the form and asks "did it work?" hears "I can't do that at this step".
  **Confirmed (code).**
- **Fix idea:** let `check_reset_status` read the issuer in `Escalated` when a link was sent, and
  only speak the result (no ticket change). Or document.

#### N8. Reason codes for "link expired unused" differ by path (A5)
- **Path:** during a call, an expired link gives `escalated/browser_unavailable`
  (`RecoveryWorkflow.cs:140-141`). After a polite goodbye (`agent_ended`), the minute check closes
  the same fact as `cancelled/caller_cancelled` (`RecoveryWorkflow.cs:223-229`; test
  `Check_CallEndedByAgentAndLinkExpired_ClosesAsCallerCancelled`).
- **Result:** the caller didn't cancel anything; the ticket reason is misleading for reporting. The
  outcome is still honest (no success claimed). **Confirmed (code).**
- **Fix idea:** when the minute check closes a session whose link expired unused, use
  `browser_unavailable`. Or document.

#### N9. Reset form: a 5xx from `/resets` is not checked (A5)
- **Path:** `reset.js:158-167`: a non-OK answer goes to `handleFailure`; with no known error code it
  shows "Something went wrong. Please try again". Only a thrown network error calls
  `checkOperation` (`reset.js:169-172`).
- **Result:** an App Service 502/503 (worker restart, front-end timeout) after the reset committed is
  shown as a plain failure. A retry then gets `token_used` ("may already be changed — tell the
  assistant"). The contract says a 503 means completion may be unknown. **Confirmed (code).**
- **Fix idea:** when `!reset.ok` and there is no known error code (or status ≥ 500), call
  `checkOperation(operationId)` as for a network error. Also: a `pending` answer drops the token
  (`finish`), so the form can't check it later; fine while the mock never returns `pending`.

#### N10. Reset page console: contract status codes show as errors (A3)
- **Path:** the new check on load (`reset.js:183-197`) posts to `/password/validate`; a used,
  expired or invalid link answers 409/410/401 as the contract requires. `checkOperation` can get a
  404.
- **Result:** Chrome and Edge log "Failed to load resource … 409" in the console for every such
  link, which breaks the project rule "browser console stays clean". The page itself handles it.
  **Needs live test** (browser behaviour).
- **Fix idea:** document it (the contract fixes the status codes); no code change.

#### N11. The prompt offers a ticket the backend can't create (A6)
- **Path:** the prompt says to "offer to create a help-desk ticket" for a non-English caller and for a
  caller in danger (`Voice/system-prompt.md:9`, `67`). Before a username there is no recovery, so
  `request_human` returns "…I couldn't create a help-desk ticket…" (`RecoveryWorkflow.cs:150`).
- **Result:** the agent offers something, the caller accepts, and the backend says it failed. Honest,
  but confusing in exactly the sensitive cases. **Confirmed (code).**
- **Fix idea:** prompt wording: before a username, offer "please contact your help desk directly";
  offer a ticket only once a reset has started.

#### N12. "One moment." may end the turn without the tool call (A6)
- **Path:** prompt rule "say 'One moment.' first" (`system-prompt.md:40`).
- **Result:** realtime models sometimes announce an action and stop. The caller then waits in silence
  until they speak or the 30 s prompt. **Needs live test.**
- **Fix idea:** watch for it in the next live calls; if seen, drop the rule (the delay is the tool
  itself) or say "in the same response as the tool call".

#### N13. No time limit on a tool call; a storage stall freezes the whole call (A5)
- **Path:** a tool runs inside the turn lock (`VoiceSession.cs:208-217`); the dispatcher has no
  timeout (`Voice/ToolDispatcher.cs:25-53`). The issuer has a 10 s timeout, but blob reads and writes
  use the SDK defaults (several retries, long network timeout).
- **Result:** during a storage stall the call is silent: no answers, no silence prompt, and the time
  limit waits for the lock. **Confirmed (code); trigger Needs live test.**
- **Fix idea:** a linked `CancellationTokenSource` (about 20 s) per tool that maps to the
  "unavailable" line, or shorter `BlobClientOptions` retry/network timeouts.

#### N14. The AI disclosure in the greeting depends on the model (A6)
- **Path:** the greeting is model-written from `GreetingInstruction` (`VoiceSession.cs:22-23`,
  `167-170`). A filtered or failed greeting gives the safe line ("Sorry, I can't help with that…")
  as the first sentence.
- **Result:** the "say you are automated" rule is very likely but not guaranteed. Fixed lines are
  proven live (the goodbye is spoken). **Confirmed (code).**
- **Fix idea:** speak a fixed greeting with `SayAsync` ("Hi, I'm an automated AI assistant for
  password resets. What's your username?"). It also saves one model round trip.

#### N15. The minute check reads every session blob, forever (A5, cost)
- **Path:** `ListOpenAsync` lists and downloads every `sessions/*.json` to find the open ones
  (`SessionStore.cs:14-20`); sessions are never deleted; the check runs every minute
  (`Recovery/OpenSessionCheck.cs:15`, `30-50`). Audit 1's L4 was at startup only.
- **Result:** reads per minute grow with every call ever made, sequentially. Small for the assessment
  window, but it never stops growing. **Confirmed (code).**
- **Fix idea:** document, or skip blobs not modified for a day (the listing has `LastModified`; no
  link lives that long), or move closed sessions under another prefix.

#### N16. Editing the mock state blob while the app runs is lost (operational)
- **Path:** `MockIssuer` loads `state/mock/state.json` once at startup and overwrites the whole blob
  on each change (`Mock/MockIssuer.cs:33-34`, `59-76`).
- **Result:** the hand-in step "remove the user's entry from `PasswordHashes`" (tester feedback note)
  does nothing until a restart, and is overwritten if any call or reset writes first.
  **Confirmed (code).**
- **Fix idea:** stop the app, edit the blob, start it again (or restart right after the edit with no
  traffic). Add this to the note.

#### N17. CSP allows WebSockets to any host (A3, defence in depth)
- **Path:** `connect-src 'self' wss:` (`Http/SecurityHeaders.cs:8-10`).
- **Result:** only matters if a script injection existed (none found). `'self'` already covers our
  own `wss://` in current Chrome/Edge. **Confirmed (code).**
- **Fix idea:** drop `wss:` (live check that the call still connects) or document.

#### N18. A caller who calls back can't learn the result of the previous call (A2)
- **Path:** a new call can't attach the old recovery; the issuer throttles a new start while the
  link is out (contract).
- **Result:** after a dropped call during the form step, the second call hears "I can't start a
  reset for that username right now". Honest, and the form already showed the result; the ticket is
  settled by the minute check. **Confirmed (code).**
- **Fix idea:** document only (contract behaviour; "starting again is not permission to reuse a
  previous call's verified context").

### Notes on settled decisions
- **H2:** the new `GET /mock/v1/reset-operations/{id}` is also public and takes the issuer's single
  lock (no write). Same decision; mention it in the limitation.
- **M2:** with two processes at once, the new process's minute check also treats the old process's
  live calls as dead (`StartedAt` before its start) and closes them. Same decision.
- A call can run a little past `MaxCallSeconds` (goodbye up to 15 s, plus a tool holding the lock).
  If the minute check runs in that window it may close the session first; the only effect is that
  the session's `EndReason` says `restart` instead of `time_limit`. No fix needed.

## 2. Attacks that failed (defences that held)

| Attack | Why it fails | Where |
|---|---|---|
| Read another reset operation with your own link's token | The token must hash to the token of the operation's own recovery | `MockIssuer.GetResetOperationAsync` |
| Probe operation IDs without a token | Unknown and unauthorized both give the same 404; IDs are random UUIDs from the form | same |
| Header tricks (`resettoken x`, `Bearer` + token, extra spaces) | Exact, case-sensitive scheme; service credential compared in constant time | same |
| Use the status route to reset again | It is read-only; a new reset still needs an unused, unexpired token | `ResetPassword` / `TokenRefusal` |
| Leak the token through the new header | Same origin over HTTPS, `no-referrer`, headers not logged, token never in the URL | `reset.js`, `SecurityHeaders` |
| Open a used link to reach the form | Checked on load; used/expired/invalid links end at once | `reset.js` `start()` |
| Make the minute check settle a live call | Only sessions that ended, started before this process, or are older than the call limit | `ReconcileAsync`, `OpenSessionCheck` |
| Close a session while a link can still be used | `link_issued` → wait; checked again every minute | `ReconcileAsync` |
| Resolve a ticket from `pending` without a real reset | `resolved` only with the issuer's receipt for that recovery | `CompleteAsync`, `ApplyOutcome` |
| Overwrite `resolved` or a human-requested escalation | The mock refuses/keeps it; the agent never touches final sessions | `ApplyOutcome`, `CallSession.IsOpen` |
| Use the lost-verify path to skip the code | Moves on only if the issuer itself says `verified` | `SubmitCodeAsync`, `IsVerifiedAsync` |
| Count a wrong code twice after a timeout | The `Idempotency-Key` is kept after `Unavailable`; same code → recorded result | `SubmitCodeAsync` |
| Send audio before the settings to break the session | Caller audio waits until `session.update` was sent | `PumpCallerAudioAsync` |
| Keep a muted call open for free | 30 s prompt, then goodbye (`no_input`); 10-minute limit as backstop (see N3) | `WatchSilenceAsync` |
| Talk over the goodbye to keep the call going, or trigger tools during it | The call ends when that response finishes or after 15 s; tool calls during the goodbye are ignored | `OnResponseDoneAsync`, `OnFunctionCallAsync` |
| Settings rejected → silent call | An error before `session.updated` ends the call as `unavailable`; the page says so | `HandleAsync` |
| Learn whether a username exists (voice, ticket, timing) | Decoys get the same sentence, ticket, throttle and wrong-code path | `MockIssuer.StartRecovery`, `Phrases.CodeSent` |
| "Username" crafted as a sentence ("your password has been reset") | Becomes a decoy username; nothing is said as fact | `SpokenInput.Username` |
| Fake tool output or "verification passed" | The state machine checks state; `say` sentences come from the backend | `RecoveryWorkflow`, prompt |
| XSS through captions, policy text or inbox messages | `textContent` only, Razor encoding, strict CSP | `agent.js`, `reset.js`, Razor pages |
| Codes or passwords in logs | Logs carry IDs, tools, statuses and exception types only | `VoiceLog`, `RecoveryWorkflow` |

## 3. Suggested order

1. **N1** (cancel after link → reconcile): the contract names it, and it is the same truth problem as
   H1. Small state change plus one test.
2. **N2 + N4** (silence wording, longer second wait, no prompt while the caller is speaking) and
   **N11** (prompt wording): small, and they protect the caller experience.
3. **N9 + N5** (form checks the operation on 5xx; minute check waits on `reset_pending` and errors):
   a few lines each.
4. **N14** (fixed greeting) if there is time; it guarantees the AI disclosure.
5. In the next live test, watch for **N3**, **N12**, **N4** and **N10**.
6. Document the rest in `SETUP.md` limitations: N6, N7, N8, N13, N15, N17, N18, and the notes on
   H2/M2. Do **N16** (stop, edit, start) as the last step before hand-in.
