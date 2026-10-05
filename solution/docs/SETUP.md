# Setup and operations

How to build, test, run and deploy the voice password reset agent, how to test it in an
isolated copy, and what it does not do. The design is in [architecture.md](architecture.md).

All commands run from the `solution/` folder unless noted. Values shown as `<...>` are
placeholders; real values are never in the repository.

## What runs where

One ASP.NET Core app (.NET 10) on Azure App Service. It holds the voice agent and, under
`/mock/...`, small mock versions of the issuer, the ticket system and the recovery inbox.
Speech and the model are Azure Voice Live. State is JSON in Blob Storage.

Callers reach the agent by **phone**: a Twilio number streams the call to the app (see
[Connect a phone number](#3-connect-a-phone-number-twilio)). The browser agent page is a
second way in, to talk to the same agent without a phone.

| Entry | URL | Who uses it |
|---|---|---|
| Phone number | the Twilio number (webhook `/phone/incoming`) | The caller: calls the agent |
| Agent page | `/` | Optional: talk to the agent in the browser after entering the access code |
| Recovery inbox | `/mock/inbox/login` | The caller, signed in as the synthetic user: reads the code and the reset link |
| Reset form | `/reset/` (opened from the link) | The caller: types the new password |
| Work sign-in | `/mock/login` | Optional: checks the old or new password ("Forgot your password?" links to `/`) |
| Health | `/health` | Anyone: `{"status":"ok","commit":"<sha>"}` |

Pages target desktop Chrome or Edge (the agent page also needs a microphone). HTTPS is
required everywhere, also locally (the cookies are `__Host-` and `Secure`).

## Prerequisites

- .NET SDK 10 (`global.json` pins 10.0.302 and accepts newer feature bands).
- Azure CLI (`az`), PowerShell (Windows PowerShell 5.1 or PowerShell 7) and git.
- For Azure: a subscription where your account is **Owner** (or Contributor plus User
  Access Administrator). The setup script registers resource providers, creates a resource
  group and creates role assignments.
- For the phone: an upgraded Twilio account with a US voice number (see
  [Connect a phone number](#3-connect-a-phone-number-twilio)).

## Build and test

```powershell
dotnet build VoiceReset.slnx -c Release
dotnet test --project tests/VoiceReset.Tests
```

Release builds treat warnings as errors. The tests (xUnit) use a fake clock, an in-memory
store, the real mock issuer behind a test server, and fake Voice Live and audio
connections. They need no Azure resources and no network.

## Run locally

Voice Live always runs in Azure, so a local run needs an AI Services account (for example
the one the setup script creates) and your own access to it.

1. Once: give your account `Cognitive Services User` and `Azure AI User` (or `Foundry User`)
   on the AI Services account. The setup script grants these only to the web app.
2. Once, if not done before: `dotnet dev-certs https --trust`. The app calls its own mock
   issuer over HTTPS, so the development certificate must be trusted.
3. Run:

```powershell
az login
$env:VoiceLive__Endpoint = 'https://<ai-account>.services.ai.azure.com/'
dotnet run --project src/VoiceReset
```

Open `https://localhost:7180/`. Locally:

- `appsettings.Development.json` holds development-only values (access code, service
  credential, three synthetic users with inbox and initial passwords, a placeholder Twilio
  token). They are never used in Azure. Twilio can't reach `localhost`, so the phone is
  tried on the Azure deployment.
- `Storage:BlobEndpoint` is empty, so state is kept in memory and transcripts are not
  saved. A restart starts clean. To keep state across restarts, see
  [Isolated test deployment](#isolated-test-deployment-and-restart-tests).
- The app signs in to Voice Live with `DefaultAzureCredential` (your `az login`). Without a
  real endpoint or the roles, the pages load but a call ends at once with "The voice
  service is not available right now".

## Deploy to Azure

### 1. Create the resources (once)

```powershell
az login
./scripts/setup-azure.ps1 -SubscriptionId <subscription-id> -Suffix <3-8 lowercase letters or digits>
```

| Parameter | Default | Meaning |
|---|---|---|
| `-SubscriptionId` | required | Target subscription |
| `-Suffix` | required | Makes the global names unique |
| `-Location` | `swedencentral` | Region for everything except the App Service plan. It must offer Voice Live. |
| `-AppLocation` | same as `-Location` | Region for the App Service plan only, when the main region has no App Service quota |
| `-Runtime` | `DOTNETCORE:10.0` | Checked against `az webapp list-runtimes --os linux` |

In resource group `rg-voicereset` the script creates:

| Resource | Name | Purpose |
|---|---|---|
| AI Services account (S0) | `ai-voicereset-<suffix>` | Voice Live: speech and model |
| Storage account, containers `state` and `transcripts` | `stvoicereset<suffix>` | Sessions, mock state, transcripts (lifecycle rule: transcripts deleted after 7 days) |
| App Service plan (B1 Linux) and web app | `plan-voicereset-<suffix>`, `app-voicereset-<suffix>` | The app: HTTPS only, WebSockets on, Always On, TLS 1.2, FTPS off |
| Log Analytics workspace, Application Insights | `log-voicereset-<suffix>`, `appi-voicereset-<suffix>` | Logs and telemetry |

The web app gets a system-assigned managed identity with `Cognitive Services User`,
`Azure AI User` (or `Foundry User`, whichever exists in the tenant) and
`Storage Blob Data Contributor`. The app uses no keys for Voice Live or Storage.

The script generates the access code, the service credential and the synthetic users'
inbox and initial passwords. It writes them only to the web app's app settings, through a
temporary file, never on a command line or on screen. It is safe to run again: existing
resources are kept and existing secrets are not rotated.

Voice Live in model mode needs nothing created in Foundry. Adding a budget alert on the
resource group is recommended; the script does not create one.

### 2. Deploy the code

```powershell
./scripts/deploy.ps1 -Suffix <suffix> [-TimeoutMinutes 5] [-SkipTests]
```

`-SkipTests` is only for a quick live check; run the tests and deploy again afterwards.

The script refuses to run if `solution/` has uncommitted changes, runs the tests in
Release, publishes, zips, deploys with `az webapp deploy`, then waits until `/health`
reports the commit it deployed. There is no CI/CD; deploys are manual. The system prompt is
compiled into the app, so a prompt change also needs a deploy.

The first start after setup can fail and restart for a few minutes, until the new role
assignments take effect (the app opens Blob Storage at startup).

### 3. Connect a phone number (Twilio)

1. Upgrade the Twilio account and create its Primary Customer Profile (Trust Hub). A trial
   account can't buy numbers, and its shared trial number does not stream call audio to an
   outside server, so the agent can't answer on it.
2. Buy a US number with Voice: toll-free for the submission; a local number can also be
   called from abroad for testing (toll-free numbers usually can't).
3. Put the account's Auth Token in the web app setting `Phone__Twilio__AuthToken` (portal →
   web app → Environment variables). Restart the app if `/phone/incoming` still answers 404.
4. On each number: Voice configuration → primary method Webhook,
   `https://app-voicereset-<suffix>.azurewebsites.net/phone/incoming`, HTTP POST.
5. Call the number: the agent greets you. Twilio blocks forwarding calls from the account to
   its own new toll-free number (error 13225, "blacklisted"), so a toll-free number is best
   tested by someone calling from the US.

### 4. Check the deployment

`https://app-voicereset-<suffix>.azurewebsites.net/health` returns
`{"status":"ok","commit":"<sha>"}`. The commit must equal `git rev-parse HEAD` of the
checkout that was deployed. It comes from the build (the SDK adds the git commit to the
assembly's informational version); a build outside a git checkout shows `unknown`.

## Configuration

App Service app settings; `__` separates sections (`Access__Code` is `Access:Code`).
Every section is validated at startup, so a missing or invalid value stops the app instead
of failing on the first call. Business code never reads configuration directly.

| Name | Purpose | Set by |
|---|---|---|
| `Access__Code` | Shared code that opens the agent page (12+ characters) | script, secret |
| `Access__AllowedOrigin` | The page origin; the voice WebSocket accepts only this origin | script |
| `VoiceLive__Endpoint` | AI Services endpoint (https) | script |
| `VoiceLive__Model`, `VoiceLive__Voice` | Model and voice | defaults in `appsettings.json` |
| `Storage__BlobEndpoint` | Blob endpoint; empty means in-memory state and no transcripts | script |
| `Issuer__BaseUrl` | Where the agent calls the issuer (`<origin>/mock/`) | script |
| `Issuer__ServiceCredential` | The agent's service credential for the issuer | script, secret |
| `Mock__ServiceCredential` | The credential the mock issuer accepts (same value) | script, secret |
| `Mock__ResetBaseUrl` | Reset form URL used in reset links (`<origin>/reset/`) | script |
| `Mock__Users__<i>__Username`, `__DisplayName`, `__RequiresUnlock` | Synthetic accounts | script |
| `Mock__Users__<i>__InboxPassword`, `__InitialPassword` | Inbox sign-in and starting password | script, secret |
| `Limits__MaxCallSeconds` | Maximum call length (default 600, allowed 60 to 3600) | default in code |
| `Phone__Twilio__AuthToken` | The Twilio account's Auth Token; it checks webhook signatures (and looks up unsigned trial calls). Empty means the phone routes answer 404 | owner, in the portal, secret |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry export; nothing is exported when unset | script |

The synthetic users are `alex.morgan`, `jamie.lee` and `sam.taylor` (the last one also
needs an unlock). To read the access code and the inbox passwords of a deployment:

```powershell
az webapp config appsettings list -g rg-voicereset -n app-voicereset-<suffix> --query "[?name=='Access__Code' || ends_with(name,'__Username') || ends_with(name,'__InboxPassword')].{name:name,value:value}" -o table
```

## Trying the journey

1. **Call the phone number** (or, in a browser, open the agent page `/`, enter the access
   code, press **Start call** and allow the microphone). The agent says it is an automated
   AI assistant and asks for the username. Say it the normal way ("alex dot morgan"); the
   agent reads it back, spells it and waits for "yes".
2. **Recovery inbox:** open `/mock/inbox/login` in a browser (the agent page links to it)
   and sign in with the same username and its inbox password. A six-digit code arrives,
   valid for two minutes. Read it to the agent and confirm the read-back. Two wrong codes
   lock this reset attempt and record an escalation.
3. Once the code is verified, the agent sends a reset link to the same inbox.
4. **Reset form:** the link opens `/reset/`. Type the new password there, never to the
   agent. The form checks the link first, shows the policy and reports violations.
5. Tell the agent you are done. It asks the issuer and confirms only when the issuer has a
   receipt. For `sam.taylor` it also says the account is unlocked.
6. Optional: sign in on `/mock/login` with the new password (the old one is refused).

Other paths to try: an unknown username (same words, no code arrives), a wrong code twice,
waiting more than two minutes, "can I talk to a person?", "cancel", silence (after 30 s
"Are you still there?", after 60 s more a goodbye), and hanging up mid-call.

## Isolated test deployment and restart tests

Nothing needs production access: all accounts and systems are synthetic and inside the app.

**In Azure (recommended for restart tests).** Run the two scripts in a separate
subscription with your own suffix. The copy has its own storage, AI account, identity and
generated secrets, and shares nothing with any other deployment. The scripts use a fixed
resource group name (`rg-voicereset`), so use one copy per subscription. Restart it with:

```powershell
az webapp restart -g rg-voicereset -n app-voicereset-<suffix>
```

**Locally with durable state.** Set `$env:Storage__BlobEndpoint` to a test storage account
(your account needs `Storage Blob Data Contributor` on it), run as in
[Run locally](#run-locally), stop with Ctrl+C and start again.

What to expect after a restart:

- Issuer state survives: code windows, attempt counts, throttles, links, receipts and
  tickets. A restart can't reset or extend a code window or its two attempts.
- A call that was live is lost: the page says the connection was lost, and a phone call
  drops. The caller starts a new call; the account stays throttled until the old code window (or a link
  already sent) expires.
- The open-session check runs at startup and then every minute. For each session with no
  live call it asks the issuer: a reset that completed is recorded as `resolved` with its
  receipt; while a sent link can still be used it waits; otherwise it closes the session
  and records `cancelled` on the ticket (`caller_cancelled` if the agent ended the call,
  otherwise `call_dropped`).

## Operations

| Task | How |
|---|---|
| Is it up, which version? | `GET /health` |
| Logs | Application Insights `traces`. Messages: `CallStarted <session>`, `ToolCalled <session> <tool> <status>`, `CallEnded <session> <reason> <seconds>`, `VoiceLiveError <session> <code> <param>`, `ResponseNotCompleted`, `CallConnectionClosed`, `CallStepFailed`, `Open session check: <n> settled`, `Ticket <action> failed`, `TwilioWebhookChecked <signature or call lookup> Genuine=<true/false>`. Logs carry IDs, states, status codes and exception types only. |
| Example query | `traces \| where timestamp > ago(1h) \| order by timestamp desc \| take 100` |
| Call sessions | Container `state`, blobs `sessions/<sessionId>.json` (state, IDs, ticket outcome, receipt; never codes, tokens or passwords) |
| Transcripts (debug aid) | Container `transcripts`, `yyyy/MM/dd/<sessionId>.json`, masked, deleted after 7 days. Download with `az storage blob download --auth-mode login ...` (needs a Storage Blob Data role for your account). |
| Reset all mock state | The mock keeps its state in memory and writes it back on every change, so: `az webapp stop`, delete blob `mock/state.json` in container `state`, `az webapp start`. Removing one user's entry from `PasswordHashes` in that blob (same stop/start) puts only that user back to the initial password. |
| Ticket of a call | In `mock/state.json` under `Tickets` (outcome, reason, receipt, history) |

## Trust boundaries

| Boundary | What crosses it | Protection |
|---|---|---|
| Caller to agent page | Access code, microphone audio | The code is posted as JSON (never in the URL), compared in constant time, rate limited to 5 attempts per minute per address, and exchanged for a cookie (`__Host-vr-access`, HttpOnly, SameSite Strict, 2 hours). `/voice/ws` needs the cookie and accepts only the page's own origin. The gate limits who can spend money; it never proves identity. |
| Phone carrier (Twilio) to app | Incoming-call webhook, call audio | The webhook must carry a valid `X-Twilio-Signature` (or, unsigned from a trial number, be a live call on the account, checked with Twilio's API). It answers with a one-time 30 s token, and `/phone/stream` starts a session only with that token. No access code; the caller's number is never used or logged. |
| Caller's speech to the model | Audio and its transcription | Everything the caller says is data, not instructions. Spoken secrets reach Voice Live (audio and speech-to-text); the agent never asks for or repeats a password. |
| Model to backend | Tool calls | Seven tools; only `username` and `code` take an argument. Extra or unknown arguments are refused. The session comes from the WebSocket, never from the model. `RecoveryWorkflow` checks every tool against the call's state. |
| Agent to issuer | HTTPS to `/mock/v1/...` | Service credential (Bearer). No issuer response contains a code, token, link, password or inbox content. |
| Inbox to caller | Code and reset link | A separate sign-in per synthetic user (`__Host-mock-inbox` cookie). The agent can't read the inbox. |
| Reset form to issuer | Token and new password | The token is in the URL fragment (never sent to the server as part of the URL) and removed from the address bar. The password goes only to `/mock/v1/password/validate` and `/mock/v1/resets` over HTTPS. `no-store`, `no-referrer`, strict CSP. Never logged, never sent to the model. |
| App to Azure services | Voice Live, Blob Storage | Managed identity, no keys. |
| Mock issuer storage | Mock state (`mock/state.json`) | **Weaker than the API.** The mock shares the app's storage account and identity, and its inbox messages (codes and links) are stored in plain text. Anyone with the app's identity or the storage account key can read them. The "service credential can't read codes or links" boundary holds at the HTTP API only. |

Passwords never appear in logs, transcripts, prompts or tool arguments. Request bodies are
not logged. Codes and links never appear in logs; transcripts mask them (best effort).

## Mock issuer: what it implements

The mock follows [the mock contract](../../docs/mock-contract.md) under the base path
`/mock/` (`Issuer__BaseUrl`). These are the points the contract leaves to the adapter.

| Topic | Behaviour |
|---|---|
| Routes | All contract routes: `POST /v1/recoveries`, `POST /v1/recoveries/{id}/verify`, `POST /v1/recoveries/{id}/reset-link`, `GET /v1/recoveries/{id}`, `GET /v1/policy`, `POST /v1/password/validate`, `POST /v1/resets`, `GET /v1/reset-operations/{operation_id}` (service credential or `ResetToken <token>`), `POST /v1/tickets`, `POST /v1/tickets/{id}/outcome`. JSON in `snake_case`; unknown fields and wrong types are rejected. |
| Namespace | One namespace: the single service credential. |
| Rate limits | Per account only: one active recovery, and a new code at most once per 120 s; while a link is out, no new recovery until it expires. The answer is `429 throttled` with `Retry-After`. Unknown usernames get the same envelope and throttling (decoy recoveries, nothing delivered). There is **no namespace-wide rate limit** and no per-address limit on the mock routes. |
| Retention | Recoveries, idempotency (replay) records, inbox messages and tickets with their history are **kept until the state blob is deleted**, so far longer than any token lifetime. |
| Codes and links | Six-digit codes valid 120 s, two attempts. Links carry a 256-bit random token, valid 10 minutes, single use, sent as `<Mock__ResetBaseUrl>#token=...`. Recovery records keep only hashes. |
| Password policy | Version `2026-10-v1`: at least 12 characters, upper case, lower case, a digit, not containing the username, not the current password. |
| Not implemented | Resets always complete at once (`200 succeeded`). The mock never returns `202 pending`, a `failed` operation, `reset_pending`, `reset_failed` or `503`. The agent and the form handle those answers, but this mock never exercises them. |
| Simplifications | A retry of an accepted reset is compared with the account's *current* password, so a retry after a later reset of the same account gets `idempotency_conflict`. A preserved human-requested escalation ignores later outcome updates without adding them to the history. Passwords are stored as unsalted SHA-256 (synthetic data only). |

## Known limitations

**Channel and scope**

- **The phone carrier is Twilio, not ACS or Teams telephony:** ACS phone numbers could not be
  obtained for this subscription. Twilio only carries the call; speech, model, tools and
  state stay on Azure. Phone callers need no access code, so the budget alert, the call
  time limit and the silence goodbye are what limit cost. A US toll-free number is usually
  not reachable from outside the US.
- Pages: desktop Chrome and Edge only. English only. No outbound calls, no transfer to a person
  (an escalation ticket is recorded instead), no callback.
- Captions (both sides) are shown on the browser page only; the phone has no screen.

**Reliability and scale**

- **Single instance only.** State documents (mock issuer state and call sessions) are
  last-writer-wins JSON blobs, and the per-session and issuer locks are in-process. Two
  instances running at once could lose updates, for example the wrong-code count. The App
  Service plan runs one instance.
- **A live call is lost on restart.** Its session and ticket survive and are settled by the
  open-session check (see above).
- **No cap on concurrent calls** and no rate limit on `/voice/ws` or the phone number.
  Anyone with the shared access code, or anyone who calls the number, can hold 10-minute
  calls; only the silence goodbye, the time limit and a budget alert limit the cost.
- Records grow without limit: the mock state document is rewritten on every change and
  searched linearly, and session blobs are never deleted.
- A tool call runs inside the call's event loop, so a slow issuer answer (up to the 10 s
  timeout) also delays audio. The agent says "One moment." before slow tools.

**Mock issuer**

- Not hardened. The browser routes (`/mock/v1/resets`, `/password/validate`) and both
  sign-in pages need no credential and have no rate limit, and every issuer request takes
  one global lock. A flood of junk requests slows every call's issuer requests.
- Resets always complete synchronously (see the table above).
- Codes and links sit in plain text in the mock state blob, readable with the app identity
  or the storage account key.

**Security**

- The contract's throttle can be used to block an account: someone who knows a username
  can start a recovery every few minutes so the real user always hears "I can't start a
  reset for that username right now". That sentence also tells a caller that someone tried
  recently (not whether the account exists).
- **Spoken sentences can't be fully enforced.** The backend supplies every critical
  sentence in the tool result, but in Voice Live model mode the model speaks it. A prompt
  injection could make the model say something false; only the prompt prevents it. The
  safe line, the silence and time-limit lines and the goodbye are sent as fixed messages
  and are spoken word for word.
- **Transcript masking is best effort.** It masks links, digit runs (also spoken numbers
  like "forty-seven") and everything after "password is / was / would be". It can miss
  other phrasings and over-masks other numbers. Transcripts are deleted after 7 days.
  Audio and text still pass through Voice Live.
- **A link sent before a cancel stays valid** until it expires; the contract has no revoke.
  If the caller uses it after cancelling, the open-session check records the completed reset
  (`resolved`) once the call is over.
- Secrets are in App Service settings (encrypted at rest), not Key Vault. Account keys on
  the AI Services and storage accounts are not disabled.

**Conversation**

- Within one call, a cancelled reset can't be restarted; the caller calls again.
- Ticket reasons are coarse: "I can't use a browser" is recorded as `human_requested`, and
  a call that hits the time limit as `call_dropped`.
- Unknown usernames never get a code, by design, so testers must use a synthetic username.

## Cleanup

```powershell
az group delete --name rg-voicereset --yes
az cognitiveservices account purge --name ai-voicereset-<suffix> --resource-group rg-voicereset --location <location>
```

A deleted AI Services account keeps its name for a while (soft delete). Purge it, or use a
new suffix, before running the setup script again. Role assignments on the deleted
resources go with them. In Twilio, release the numbers (they cost a monthly fee). Locally,
nothing is left behind when `Storage:BlobEndpoint` is empty.
