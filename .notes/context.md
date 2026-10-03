# Working context (for any session, local or cloud)

Facts and owner preferences that aren't obvious from the code. Read with `CLAUDE.md`.

## Owner preferences
- **No AI attribution anywhere on GitHub:** commits, commit messages, branch names, PRs, PR/issue
  comments and code comments never mention Claude, Claude Code or AI. No `Co-Authored-By`, no
  "Generated with". The only author is George Sindilar. Using AI is fine and `CLAUDE.md` is committed.
- **Keep it simple.** Smallest design that works; the owner must be able to explain every line.
- **Desktop only.** Pages target desktop Chrome/Edge. No mobile/responsive work, checks or review findings.
- **Never name the interviewer** in files, code or commits; write "the interviewer".
- **Structure (agreed for the cleanup pass):** features stay as top-level folders; everything that is
  not a feature (`Http/`, `Observability/`, `Storage/`, possibly `Health/`) moves into one `Shared/`
  folder. Also remove unused code. Done after the app works end to end.
- No local-run deliverable: everything is deployed to Azure.

## Status (2026-10-03, evening)
- Code: T1–T15 done; work since then is on branch `deploy-azure` (pushed, not yet merged to `main`).
  81 tests, 0 warnings. T16 docs not written yet; an unreviewed draft is in `drafts/SETUP-draft.md`.
- Remaining work: `solution/docs/remaining-work.md`.
- Implementation process used: superpowers subagent-driven development: implementer → spec review →
  code-quality review → fixes, per task. Plans: `plans/` (`00-contracts.md`, `00-reconciliation.md`
  wins over the task plans and records the real names after each task).
- Added after deployment (on `deploy-azure`):
  - `/mock/login`: a mock work-account sign-in (Razor page `Pages/Mock/Login`). It only checks the
    password (`MockIssuer.CheckPasswordAsync`: current hash or `InitialPassword`) and shows success or
    failure. Intended flow: try to sign in → "Forgot your password?" link to the agent page → call →
    inbox (new tab, linked from the agent page) → reset form → "Sign in with your new password" link
    back to `/mock/login`.
  - Prompt step 5: before `end_call` the agent asks "anything else?"; "thank you"/"okay" alone is not a
    goodbye. (In the first test the model hung up on a plain "thank you".)
  - `VoiceLiveError` logs now include the rejected parameter name (`Param`), never the message text.

## Azure (deployed)
- Owner's own pay-as-you-go subscription, now with about $200 credit. Budget `budget-voicereset`:
  $50/month on the resource group, email alerts at 50/80/100% actual and 100% forecast.
- Resource group `rg-voicereset` (eastus2): AI Services `ai-voicereset-gs01` (Voice Live), storage
  `stvoiceresetgs01` (containers `state`, `transcripts`), App Insights `appi-voicereset-gs01`,
  Log Analytics `log-voicereset-gs01`. App Service plan `plan-voicereset-gs01` is in **centralus**
  (no B1 quota in eastus2/eastus on a new subscription); web app `app-voicereset-gs01`.
- Created with `solution/scripts/setup-azure.ps1 -SubscriptionId <id> -Suffix gs01 -Location eastus2
  -AppLocation centralus` (rerunnable; secrets generated only when missing, never printed).
- Deploy: `solution/scripts/deploy.ps1 -Suffix gs01` (clean tree → tests → publish → zip → deploy →
  waits for `/health` to show the commit). Manual, no CI/CD. The prompt is compiled in, so prompt and
  guardrail changes need a deploy. Voice Live model mode needs nothing created in Foundry.
- URLs: agent `https://app-voicereset-gs01.azurewebsites.net/`, inbox `/mock/inbox/login`,
  work sign-in `/mock/login`, health `/health`.
- Demo users (setup script): `alex.morgan`, `jamie.lee`, `sam.taylor` (needs unlock). Access code,
  inbox passwords and initial passwords are only in the web app settings (`Access__Code`,
  `Mock__Users__N__*`). After a reset the new password is a SHA-256 hash in blob
  `state/mock/state.json` → `PasswordHashes`; delete that blob to reset all mock state.
- Local Windows: prepend `C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin` to PATH in PowerShell.
  Sign in with `az login --tenant <tenant>` (MFA). From a cloud session: `az login --use-device-code`.

## Debugging live calls
- Transcripts (masked): blob container `transcripts`, path `yyyy/MM/dd/<sessionId>.json`. Read with
  `az storage blob download --account-name stvoiceresetgs01 --account-key <key> -c transcripts -n <path> -f <file>`
  (key from `az storage account keys list`).
- Logs: `az monitor app-insights query -g rg-voicereset -a appi-voicereset-gs01 --analytics-query
  "union traces, exceptions | where timestamp > ago(3h) | order by timestamp desc | take 50"`.
  Useful messages: `CallStarted`, `ToolCalled <session> <tool> <status>`, `CallEnded <session> <reason>`,
  `VoiceLiveError <session> <code> <param>`.
- **Open issue:** Voice Live answers our `session.update` with `invalid_session_update_message`. The
  first call failed (`max_config_attempts_exceeded`); the next got one error and then worked. Find the
  rejected parameter from the new `Param` in the logs and fix `Voice/VoiceLiveSettings.cs`
  (suspects: Dragon HD voice, echo cancellation, semantic VAD options, max output tokens).
- First test notes: background noise is transcribed as words; testers must use a demo username
  (unknown usernames never get a code, by design).

## Phone channel decision
- ACS numbers are effectively unavailable for new tenants (Sept 2026 retirement; see sdk-reference).
  The README says the interviewer supplies sandbox telephony, but the owner chose **Twilio** (Telnyx as
  fallback): US toll-free number, voice webhook → our app. Risk accepted: the spec wants ACS/Teams
  telephony; document that Twilio only carries the call and everything else is Azure.
- Owner creates the Twilio account, upgrades it (no trial message), buys the number and puts the Auth
  Token into the app settings in the portal (never in chat or the repo).
- Code still to write: `POST /phone/twilio` returns TwiML `<Connect><Stream url="wss://.../phone/stream">`;
  a `TwilioAudioChannel : IAudioChannel` on that WebSocket (μ-law 8 kHz; Voice Live supports
  g711_ulaw, so set the audio format per channel); validate `X-Twilio-Signature`; tests; deploy.

## Useful references
- Verified Voice Live 1.2.0 / ACS facts: `archive/overnight/research/sdk-reference.md`.
- Guardrails research (attack scenarios, prompt draft): `archive/overnight/research/guardrails-research.md`.
- Challenge-related planning (roadmap, walkthrough, requirements checklist): `planning/`, `submission/`.
