# Setup

How to build, run and deploy the voice password reset agent. All commands run from the
`solution/` folder.

## Prerequisites

- .NET SDK 10 (`global.json` pins 10.0.302 and accepts newer feature bands)
- Azure CLI (`az`), PowerShell (Windows PowerShell 5.1 or PowerShell 7), git
- An Azure subscription. The account that runs the setup script needs **Owner** on the
  subscription (or Contributor plus User Access Administrator): it registers resource
  providers, creates a resource group and creates role assignments.
- Desktop Chrome or Edge with a microphone.

## Build and test

```powershell
dotnet build VoiceReset.slnx -c Release
dotnet test --project tests/VoiceReset.Tests
```

80 tests. They use a fake clock, an in-memory store and fake Voice Live and audio
connections, so they need no Azure resources.

## Run locally

```powershell
dotnet dev-certs https --trust     # once: the page and its cookies need HTTPS
$env:VoiceLive__Endpoint = 'https://<ai-account>.services.ai.azure.com/'
az login
dotnet run --project src/VoiceReset
```

Open `https://localhost:7180/` (the only launch profile). Locally:

- `appsettings.Development.json` holds local development values (access code, service
  credential, demo users and their inbox passwords). They are not used in Azure.
- `Storage:BlobEndpoint` is empty, so state is kept in memory and transcripts are not
  saved. A restart starts from a clean state.
- The mock issuer, inbox and reset form run inside the same app.
- **Voice Live always needs Azure.** The app signs in with `DefaultAzureCredential`, which
  uses your `az login`. Your own account needs the same roles on the AI Services account
  as the web app (`Cognitive Services User` and `Azure AI User` or `Foundry User`); the
  setup script only grants them to the web app. Without an endpoint and roles, the pages
  work but a call ends at once with "unavailable".

## Azure setup

```powershell
az login
./scripts/setup-azure.ps1 -SubscriptionId <subscription-id> -Suffix <3-8 lowercase letters or digits>
```

Optional parameters: `-Location` (default Sweden Central) and `-Runtime` (checked
against `az webapp list-runtimes --os linux`). In resource group `rg-voicereset` the
script creates:

| Resource | Name | Purpose |
|---|---|---|
| AI Services account | `ai-voicereset-<suffix>` | Voice Live (speech and model) |
| Storage account, containers `state` and `transcripts` | `stvoicereset<suffix>` | Session and mock state, call transcripts |
| App Service plan (B1 Linux) and web app | `plan-voicereset-<suffix>`, `app-voicereset-<suffix>` | The app: HTTPS only, WebSockets, Always On, TLS 1.2, FTPS off |
| Log Analytics workspace, Application Insights | `log-voicereset-<suffix>`, `appi-voicereset-<suffix>` | Telemetry |

The web app gets a system-assigned managed identity with `Cognitive Services User`,
`Azure AI User` (or `Foundry User`, whichever exists) and `Storage Blob Data Contributor`.
No keys are used for Voice Live or Storage.

The script generates the access code, the shared service credential and the demo
users' passwords, and writes them only to the web app settings (through a temporary
file, never on a command line or on screen). Running it again keeps existing secrets.

## Configuration

App settings in Azure; nested keys use `__` (`Access__Code` is `Access:Code`). Values
are never in the repository. Every section is validated at startup.

| Name | Purpose | Set by |
|---|---|---|
| `Access__Code` | Code that opens the agent page (at least 12 characters) | script (secret) |
| `Access__AllowedOrigin` | The page origin; the voice socket accepts only this origin | script |
| `VoiceLive__Endpoint` | AI Services endpoint for Voice Live | script |
| `VoiceLive__Model`, `VoiceLive__Voice` | Model and voice | defaults in `appsettings.json` |
| `Storage__BlobEndpoint` | Blob endpoint; empty means in-memory state and no transcripts | script |
| `Issuer__BaseUrl`, `Issuer__ServiceCredential` | Where the agent calls the issuer, and its credential | script (credential is secret) |
| `Mock__ServiceCredential` | The credential the mock issuer accepts (same value) | script (secret) |
| `Mock__ResetBaseUrl` | Base URL of the reset form, used in reset links | script |
| `Mock__Users__<i>__Username`, `__DisplayName`, `__RequiresUnlock` | Demo accounts | script |
| `Mock__Users__<i>__InboxPassword`, `__InitialPassword` | Demo inbox sign-in and starting password | script (secret) |
| `Limits__MaxCallSeconds` | Maximum call length (default 600, allowed 60 to 3600) | default in code |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Telemetry export; nothing is exported when unset | script |

Read the access code, the demo usernames and their inbox passwords with:

```powershell
az webapp config appsettings list -g rg-voicereset -n app-voicereset-<suffix> --query "[?name=='Access__Code' || ends_with(name,'__Username') || ends_with(name,'__InboxPassword')].{name:name,value:value}" -o table
```

## Deploy

```powershell
./scripts/deploy.ps1 -Suffix <suffix>
```

It refuses to run with uncommitted changes in `solution/`, runs the tests, publishes,
zips, deploys with `az webapp deploy`, and waits (`-TimeoutMinutes`, default 5) until
`/health` reports the commit that was deployed.

The first start after setup can fail and restart for a few minutes, until the role
assignments take effect (the app checks Blob Storage access at startup).

## Verify

`https://app-voicereset-<suffix>.azurewebsites.net/health` returns
`{"status":"ok","commit":"<sha>"}`; the commit equals `git rev-parse HEAD`.

## Using the three pages

1. **Agent page `/`**: enter the access code, press **Start call** and allow the
   microphone. The agent says it is automated and asks for your username. Spell a demo
   username (for example "alex dot morgan"); the agent reads it back and waits for "yes".
2. **Mock inbox `/mock/inbox/login`**: in another tab, sign in with the same username and
   its inbox password. The six-digit code arrives here (valid two minutes). Read it to the
   agent and confirm the read-back. After two wrong codes the reset is locked and escalated.
   Once the code is verified, the agent sends a reset link to the same inbox.
3. **Reset form**: the link in the inbox opens `/reset/`. Type the new password there,
   never to the agent. Then tell the agent you are done; it checks the status and confirms
   only when the issuer has a receipt. The demo user `sam.taylor` is also unlocked.

## Trust boundaries

| Boundary | What crosses | Protection |
|---|---|---|
| Caller to agent page | Access code, audio | Code exchanged for a cookie (`__Host-vr-access`, 2 hours, 5 attempts per minute per address) before `/voice/ws` opens; own origin only. The gate limits cost; it never proves identity. |
| Model to backend | Tool calls | `RecoveryWorkflow` decides; tools take only a username or a code; the session comes from the connection |
| Agent to issuer | HTTP over `/mock/v1` | Service credential; responses never contain codes, tokens, links or passwords |
| Inbox to caller | Code and link | Separate inbox sign-in (`__Host-mock-inbox`); the agent never sees the inbox |
| Caller to reset form | Token, new password | Token in the URL fragment, removed from the address bar; sent only to the reset API over HTTPS; `no-store`, `no-referrer`; never logged or sent to the model |
| App to Azure | Voice Live, Blob Storage | Managed identity, no keys |

## Known limitations

- **Phone channel: not built yet.** A phone number could not be obtained. It is designed
  to plug in as a second audio channel (`IAudioChannel`) on the same voice session.
- **Desktop Chrome and Edge only.** HTTPS is required (no `ws:` fallback), also locally.
- **One instance.** A live call is held in memory; state files are last-writer-wins, and
  a failed blob write leaves memory ahead of storage. The startup check runs once per
  start: it records a reset finished while no call was watching, and closes sessions
  older than `MaxCallSeconds`.
- If the caller cancels after the link was sent, or the call drops and the session is
  closed after a restart, a later reset with that link is not recorded on the ticket.
- If the reset response is lost, the next attempt gets "link already used"; the form says so.
- **Transcripts** are a debug aid with no retention rule. Masking is pattern-based: it
  masks links, digit runs and text after "password is", over-masks other numbers, and can
  miss other phrasings (for example "the password I chose is …", codes read as pairs,
  links without `https://`). Audio and text still pass through Voice Live.
- No rate limit or concurrency cap on `/voice/ws`; no rate limit on the inbox sign-in.
  The inbox refreshes every 5 seconds. Captions show only the agent's words.
- Within one call, a cancelled reset cannot be restarted; asking for a person after an
  escalation keeps the earlier ticket reason.
- Secrets are in App Service settings (encrypted at rest), not Key Vault; the AI Services
  account keys are not disabled.
- English only; no outbound calls; no transfer to a person (a help-desk ticket is
  recorded instead).
- In Development, error pages trigger content security policy messages in the console.

## Cleanup

```powershell
az group delete --name rg-voicereset --yes
az cognitiveservices account purge --name ai-voicereset-<suffix> --resource-group rg-voicereset --location swedencentral
```

A deleted AI Services account keeps its name for a while (soft delete). Purge it, or use
a new suffix, before running the setup script again.
