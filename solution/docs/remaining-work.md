# Remaining work

The application is deployed to Azure (89 tests passing, 0 warnings). This list covers what is
still to do.

## 1. Deploy to Azure (done)

- Resources created with `scripts/setup-azure.ps1` (`-AppLocation` puts the App Service plan in
  another region when the main region has no App Service quota). Budget alert added.
- `scripts/deploy.ps1` deploys and waits until `/health` reports the commit.

## 2. Live calls (browser), in progress

- Done: a full call reaches the agent, the greeting plays, `start_recovery` sends the code, the
  goodbye after `end_call` is spoken.
- Fixed: Voice Live rejected messages sent before the session settings
  (`invalid_session_update_message`); caller audio now waits for them.
- Check after the latest changes: the spoken username keeps its first letter, look-alike letters
  are clarified, "One moment" before slow tools, the silence prompt after 30 s, a used link is
  reported when the form opens.
- Check after the prompt change: the agent asks "anything else?" before `end_call` and does not
  hang up on a plain "thank you".
- Still to verify: barge-in, the safe line, the time-limit line, the full journey (code → link →
  reset form → receipt → ticket resolved → sign-in on `/mock/login` with the new password), failure
  paths (wrong code twice, expired code, human, cancel, closing the tab), masked transcripts, a clean
  browser console.

## 3. Documentation (written)

- `docs/SETUP.md`: pages and URLs, prerequisites, build/test, local run, Azure setup and deploy
  (script parameters and resources), configuration names (no values), trying the journey, isolated
  test deployment and restart tests, operations (logs, transcripts, resetting mock state), trust
  boundaries, what the mock issuer implements (rate limits, retention, what is not implemented),
  known limitations, cleanup.
- `docs/architecture.md`: components (diagram and table), call flow (sequence diagram) and call
  endings, recovery state machine and ticket outcomes, guardrails as implemented (code or prompt),
  reliability (idempotency keys, reconciliation, restarts), data and secrets, the planned phone
  channel, decisions and trade-offs, cost drivers, changes before production.
- Updated for the phone channel; update again after the live phone test.

## 4. Phone channel

- New ACS tenants can't get phone numbers since the September 2026 retirement announcement, so the
  number comes from Twilio (US toll-free), which only carries the call; speech, model and tools stay
  on Azure Voice Live.
- Built: `POST /phone/incoming` (signature check, TwiML with a one-time token) and `/phone/stream`
  (`TwilioAudioChannel`, μ-law 8 kHz straight to Voice Live). Setup steps are in `SETUP.md`.
- Live: a US local number (tests from abroad) and a US toll-free number both point at `/phone/incoming`.
  A call to the local number worked end to end (greeting, speech, goodbye played, hang-up).
- Still to do: one call to the toll-free number from the US (Twilio blocks forwarding to it from the
  account, error 13225), and a full reset journey on the phone.

## 5. Code cleanup

- Done: features under `Features/` (including `Health`), non-feature code under `Shared/`.
- Remove unused code (dead-code pass).

## 6. Known limitations to document

- Single instance; JSON files are last-writer-wins; locks are in-process.
- A live call is lost on a process restart (state and tickets survive).
- Transcript masking is pattern-based (it misses some phrasings and over-masks numbers); raw
  audio and text still pass through Voice Live.
- Captions show both sides (the caller's with a spoken password masked); desktop Chrome/Edge only; no rate limit or concurrency cap on `/voice/ws`
  (concurrent sessions are expected).
- The mock: no protection against junk requests (one lock, the whole state rewritten), records kept
  forever, resets always complete at once (never `pending`), codes and links readable in storage.
- The issuer throttle (contract) lets someone keep blocking an account's resets.
- Spoken sentences can't be fully enforced in model mode (the backend supplies them; the model says them).
- A link sent before a cancel stays valid until it expires (no revoke in the contract).
