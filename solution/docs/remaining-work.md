# Remaining work

The application code is complete (80 tests passing, Release build with 0 warnings).
This list covers what is still to do.

## 1. Deploy to Azure

- Run `scripts/setup-azure.ps1`. The signed-in account needs Owner on the subscription
  (or Contributor + User Access Administrator).
- Run `scripts/deploy.ps1` and check that `/health` reports the deployed commit.
- On the first run, check:
  - the App Service runtime string (`az webapp list-runtimes --os linux`) and the role names
    (`Azure AI User` / `Foundry User`);
  - the Voice Live endpoint (`https://<ai-account>.services.ai.azure.com/`);
  - that the app settings arrive and pass startup validation;
  - that the `state` and `transcripts` containers exist;
  - that role assignments have propagated (the first start may restart for a few minutes until
    storage access works).
- Add a budget alert on the resource group.

## 2. First live calls (browser)

- Greeting plays and is captioned; barge-in stops playback.
- The goodbye after `end_call`, the safe line and the time-limit line are spoken. They use Voice
  Live's pre-generated assistant message. If the service rejects it, switch
  `VoiceLiveConnection.SayAsync` to the fallback ("say exactly this sentence").
- A filtered or failed response gets the safe line (verify the event shape).
- Full journey: username → code from the mock inbox → reset link → reset form → "password reset"
  confirmed only with a receipt → ticket resolved.
- Failure paths: wrong code twice, expired code, caller asks for a human, cancel, closing the tab.
- The transcript is written to Blob with codes and "password is …" masked; Application Insights
  holds no sensitive data; the browser console stays clean.

## 3. Documentation

- `docs/SETUP.md`: prerequisites, build/test, local run, Azure setup and deploy, configuration
  names (no values), how to use the three pages, trust boundaries, known limitations, cleanup.
- `docs/architecture.md`: components, call flow, state machine, guardrails as implemented,
  decisions and trade-offs.

## 4. Phone channel (ACS)

- Add an ACS inbound call as a second `IAudioChannel` feeding the same voice session, once a
  phone number can be obtained (new ACS tenants may not get numbers since the September 2026
  retirement announcement).

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
