# Remaining work

The application is deployed to Azure (81 tests passing, 0 warnings). This list covers what is
still to do.

## 1. Deploy to Azure (done)

- Resources created with `scripts/setup-azure.ps1` (`-AppLocation` puts the App Service plan in
  another region when the main region has no App Service quota). Budget alert added.
- `scripts/deploy.ps1` deploys and waits until `/health` reports the commit.

## 2. Live calls (browser), in progress

- Done: a full call reaches the agent, the greeting plays, `start_recovery` sends the code, the
  goodbye after `end_call` is spoken.
- Fix: Voice Live rejects part of the session settings (`invalid_session_update_message`); the
  rejected parameter is now logged (`VoiceLiveError ... <param>`). Fix it in `VoiceLiveSettings`.
- Check after the prompt change: the agent asks "anything else?" before `end_call` and does not
  hang up on a plain "thank you".
- Still to verify: barge-in, the safe line, the time-limit line, the full journey (code → link →
  reset form → receipt → ticket resolved → sign-in on `/mock/login` with the new password), failure
  paths (wrong code twice, expired code, human, cancel, closing the tab), masked transcripts, a clean
  browser console.

## 3. Documentation

- `docs/SETUP.md`: prerequisites, build/test, local run, Azure setup and deploy, configuration
  names (no values), how to use the three pages, trust boundaries, known limitations, cleanup.
- `docs/architecture.md`: components, call flow, state machine, guardrails as implemented,
  decisions and trade-offs.

## 4. Phone channel

- New ACS tenants can't get phone numbers since the September 2026 retirement announcement, so the
  number comes from Twilio (US toll-free), which only carries the call; speech, model and tools stay
  on Azure Voice Live.
- Add `POST /phone/twilio` (returns TwiML that connects a media stream) and a Twilio media-stream
  WebSocket as a second `IAudioChannel` feeding the same voice session (μ-law 8 kHz, which Voice
  Live accepts). Validate the `X-Twilio-Signature` header. The Auth Token goes in app settings.

## 5. Code cleanup

- Move non-feature folders (`Http/`, `Observability/`, `Storage/`, possibly `Health/`) into one
  `Shared/` folder; keep only features at the top level.
- Remove unused code (dead-code pass).

## 6. Known limitations to document

- Single instance; JSON files are last-writer-wins; the startup check runs once per start.
- A live call is lost on a process restart (state and tickets survive).
- Transcript masking is pattern-based (it misses some phrasings and over-masks numbers); raw
  audio and text still pass through Voice Live.
- Agent-only captions; desktop Chrome/Edge only; no rate limit or concurrency cap on `/voice/ws`.
- A link sent before a cancel stays valid until it expires (no revoke in the contract).
