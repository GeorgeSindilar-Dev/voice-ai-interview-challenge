# Step 9: Phone Channel (Conditional) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decide quickly whether a phone number is possible. If not (expected), document the phone channel honestly as designed-but-not-built. If yes, add ACS inbound calls as a second audio channel into the same voice session.

**Architecture:** Since the ACS retirement announcement (September 2026), tenants whose first ACS resource is created after it can't acquire phone numbers ([sdk-reference.md, section B](../research/sdk-reference.md)). Our new subscription is in that group. The voice session code from step 7 already talks to an `IAudioChannel`, so the phone would be a second implementation (`AcsMediaChannel`) plus two webhook endpoints. Nothing in the shared session changes.

**Tech Stack:** Azure portal, Azure CLI, `Azure.Communication.CallAutomation` 1.6.1 and `Azure.Messaging.EventGrid` 5.0.0 (only in branch B).

**When:** Saturday, during step 4 (the check takes 5 minutes). Branch B only if time allows on Sunday.

---

### Task 1: Eligibility check (5 minutes)

- [ ] **Step 1: Ask the owner** (question Q-6) whether their tenant already has an ACS resource with phone numbers. If yes, use that resource and go to Branch B.
- [ ] **Step 2: Otherwise, try with a fresh resource.** Create the ACS resource (cheap; it's deleted at cleanup if unused):

```powershell
az communication create --name acs-vr-<suffix> --resource-group rg-voicereset --location global --data-location Europe
```

- [ ] **Step 3: The owner opens** Azure portal → the ACS resource → **Phone numbers** → **Get**, and checks whether **Get** or **Trial phone number** is enabled. Take a screenshot (no IDs visible) for the docs as evidence.
- [ ] **Step 4: Decide.** Greyed out → **Branch A**. Enabled → **Branch B**.
- [ ] **Step 5 (Branch A only): delete the unused ACS resource**

```powershell
az communication delete --name acs-vr-<suffix> --resource-group rg-voicereset --yes
```

---

## Branch A (expected): document, don't build

### Task A1: Telephony design document

**Files:**
- Create: `solution/docs/architecture/telephony.md`

- [ ] **Step 1: Write the document with this content** (adjust the class names to step 7's final code):

````markdown
# Telephony (phone channel): designed, not deployed

## Why there is no phone number
Microsoft announced the retirement of Azure Communication Services in September
2026. Tenants whose first ACS resource is created after the announcement can't
acquire phone numbers (trial or paid). This project's subscription was created in
October 2026, so the "Get phone number" and "Trial number" actions were unavailable
(checked on <date>). Source: Microsoft's ACS retirement guide (link) and the ACS
phone number documentation (link).

The interviewer confirmed that another way of talking to the agent is acceptable.
The **browser voice page** is that channel. It uses the same session, prompt,
tools, guardrails and state machine the phone would use.

## How the phone would plug in
```mermaid
sequenceDiagram
    participant PSTN as Caller (phone)
    participant ACS as ACS
    participant EG as Event Grid
    participant AG as app-agent
    participant VL as Voice Live
    PSTN->>ACS: dials the toll-free number
    ACS->>EG: Microsoft.Communication.IncomingCall
    EG->>AG: POST /acs/incoming (validated subscription)
    AG->>ACS: AnswerCallAsync(MediaStreamingOptions: bidirectional, Pcm24KMono, wss://.../acs/media)
    ACS->>AG: WebSocket /acs/media (JWT-signed handshake)
    AG->>VL: same VoiceSession as the browser (IAudioChannel = AcsMediaChannel)
    ACS->>AG: POST /acs/events (CallConnected, CallDisconnected; JWT-signed)
```

- **AcsMediaChannel** implements the same `IAudioChannel` as the browser channel:
  audio frames are `AudioData` JSON messages (base64 PCM16 24 kHz, passed through
  without resampling), playback stop is `StopAudio`, and the end of the call is
  `CallDisconnected`.
- **Security:** callbacks and the media WebSocket handshake carry an ACS-signed JWT
  (issuer `https://acscallautomation.communication.azure.com`, audience = the ACS
  resource's immutable ID), which would be validated. The incoming-call webhook is
  secured on the Event Grid side.
- **Limits:** 30 s to answer (App Service Always On is required); the session
  duration limit is shorter for phone calls.
- **Caller ID is never used as authorization.**

## What would change before production
Use a Teams Phone extensibility path (the supported future for Call Automation per
the retirement guide), or an existing tenant with numbers.
````

- [ ] **Step 2: Add a known limitation** to `solution/docs/submission/known-limitations.md`: "Phone channel not deployed: no ACS number obtainable for new tenants after the September 2026 retirement announcement. The browser voice page provides the same agent. See architecture/telephony.md."
- [ ] **Step 3: Add the phone status to `submission-notes.md`** (step 14), and make sure the setting `Telephony__Enabled=false` is documented in the configuration doc.
- [ ] **Step 4: Commit**

```bash
git add solution/docs/architecture/telephony.md solution/docs/submission/known-limitations.md
git commit -m "docs(telephony): document phone channel design and why it is not deployed"
```

---

## Branch B (only if a number is available): build it

Branch B is written at task level, because it's unlikely to run. All the API
details and **compiled** code snippets are in [sdk-reference.md section B](../research/sdk-reference.md).
If Branch B is chosen, first expand these tasks into full TDD steps using that
section and the final step 7 code (about 20 minutes of planning).

| Task | Files | What |
|---|---|---|
| B1 | `Directory.Packages.props`, `src/VoiceReset.Agent/VoiceReset.Agent.csproj` | Add `Azure.Communication.CallAutomation` 1.6.1 and `Azure.Messaging.EventGrid` 5.0.0 |
| B2 | `src/VoiceReset.Agent/Configuration/TelephonyOptions.cs` | `Enabled`, `AcsEndpoint`, `PublicBaseUrl`, `AcsResourceId` (for JWT audience); validated only when `Enabled` |
| B3 | `src/VoiceReset.Agent/Telephony/IncomingCallEndpoint.cs` + test | `POST /acs/incoming`: handle `SubscriptionValidationEventData` (return the validation code), parse `AcsIncomingCallEventData`, create a `CallSession` (channel `phone`), `AnswerCallAsync` with `MediaStreamingOptions(MediaStreamingAudioChannel.Mixed)` { `TransportUri = wss://…/acs/media?session=<new id>`, `MediaStreamingContent.Audio`, `StartMediaStreaming = true`, `EnableBidirectional = true`, `AudioFormat = AudioFormat.Pcm24KMono` } |
| B4 | `src/VoiceReset.Agent/Telephony/AcsJwtValidator.cs` + test | Validate the ACS JWT on callbacks and the media handshake (OpenID config from the issuer, audience = ACS immutable ID) |
| B5 | `src/VoiceReset.Agent/Telephony/CallEventsEndpoint.cs` + test | `POST /acs/events`: `CallAutomationEventParser.ParseMany`; dedupe by event ID; `CallDisconnected` → `RecoveryWorkflow.EndAsync(callDropped)` |
| B6 | `src/VoiceReset.Agent/Channels/AcsMediaChannel.cs` + test | `IAudioChannel` over the ACS media WebSocket: `StreamingData.Parse` → `AudioData` frames; outbound `OutStreamingData.GetAudioDataForOutbound`, stop via `GetStopAudioForOutbound` |
| B7 | `src/VoiceReset.Agent/Program.cs` | Map the endpoints only when `Telephony:Enabled` |
| B8 | `infra/main.bicep` | ACS resource; Event Grid system topic + subscription for `Microsoft.Communication.IncomingCall` → `/acs/incoming`; role for the agent identity on ACS (`Contributor` per the quickstart, unless a narrower role is verified) |
| B9 | Manual test | Call the number: the full journey, barge-in, hang-up mid-flow → reconciliation |
| B10 | `docs/architecture/telephony.md` | The same doc as Branch A, minus the "why there is no number" section, plus the test evidence |

---

## Self-review

- The README requires an inbound toll-free endpoint. Branch A explains honestly why
  it's missing, and the interviewer's stated acceptance of another channel is
  recorded ✔
- The channel parity rule (CLAUDE.md) holds: no behaviour in channel code ✔
- No ACS code is written that can't be demonstrated (simplicity) ✔

## Questions for the owner

- **Q-6** (in the main questions file): does your tenant already have an ACS
  resource with phone numbers?
- **Q-9.1:** In Branch A, do you want the `AcsMediaChannel` **code** written anyway
  (untested, behind `Telephony__Enabled=false`) to show the design in code?
  **Default: no.** Untested code is a liability in a review; the design doc and the
  `IAudioChannel` seam show the same thing.
