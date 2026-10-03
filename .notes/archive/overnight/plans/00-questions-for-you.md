# Questions for you (morning review)

Every question has a **recommended default**. If you agree, just write "OK" (or
nothing), and implementation starts with the default. Questions marked **🔴 needed
first** block the start of the work; the rest can be answered during the day.

How to answer: write your answer under each question, or tell me in chat, e.g.
"Q-3: West Europe, rest OK".

---

## 🔴 Needed first (before we start)

### Q-0. Azure subscription and sign-in
You create the subscription and give me access. My plan:
1. You create the subscription (free trial, $200).
2. I install the Azure CLI with `winget install Microsoft.AzureCLI` (needs your OK).
3. You run `az login` in the terminal yourself (a Microsoft sign-in window opens).
   **No keys pasted into chat.** I then work through that signed-in session.
4. Your account is **Owner** on the subscription (the default for the person who
   creates it). That's needed so I can give the apps' managed identities their
   permissions.

**Default:** as above. ➜ Your answer:

### Q-6. Phone number: most likely impossible now (important news)
The overnight research found that Microsoft announced the **retirement of Azure
Communication Services in September 2026**. Since then, **tenants that create their
first ACS resource can't get phone numbers at all**: no trial number and no paid
number. In the portal the buttons are greyed out. New sign-ups close on 2026-10-23.
Your new subscription falls into this group. (The challenge was written before
this, which probably explains why the interviewer told you the phone isn't
mandatory.)

Does your Microsoft/Azure tenant **already** have an ACS resource with a phone
number (for example from work or an older project)? Only then could the phone work.

**Default:** no phone. We do a 5-minute check in the portal on Saturday. If the
number buttons are greyed out (expected), the phone channel is not built. It's
documented as a known limitation with the reason and Microsoft's retirement guide,
and the browser voice page is the channel for you and the interviewer. ➜ Your answer:

### Q-7. Install tools on your PC
I need:
- the **Azure CLI** (with Bicep);
- an **update of the .NET SDK** from 10.0.302 to **10.0.401** (the September 2026
  security release; we should build what we deploy with the patched SDK);
- later, optionally, **Playwright** browsers for the automated browser tests (about
  300 MB).

**Default:** OK to install all three from official sources (winget / dotnet). ➜ Your answer:

---

## Architecture decisions taken overnight (confirm or change)

### Q-1. The reset form sends the password straight to the mock issuer
The form page is served by our app, but its JavaScript posts the password
**directly** to the mocks app (the "reset backend"), never through our voice
backend. So our backend can't leak a password it never receives.

**Default:** yes. Alternative: a proxy in our backend (more code, more risk). ➜

### Q-2. Infrastructure as code: Bicep
One `infra/main.bicep` file creates everything with one command. It's re-runnable,
and it doubles as "deploy your own copy" for the reviewers.

**Default:** Bicep. Alternative: a PowerShell script of `az` commands. ➜

### Q-3. Azure region
**Default:** **Sweden Central**. It supports Voice Live, the models we need and the
high-quality voices. West Europe doesn't have the realtime models. ➜

### Q-4. Voice model
**Default:** **gpt-4.1-mini** through Voice Live (about 3 US cents per
conversation-minute, reliable tool calling), with Azure Speech for recognition and
the `en-US-Ava` HD voice. We may A/B test `gpt-realtime-mini` on Sunday if time
allows. All of these are just configuration values, so changing them later is
easy. ➜

### Q-5. Language
**Default:** English only. The agent politely says it can only help in English.
Simpler to guard and test. ➜

### Q-8. Git workflow
**Default:** keep working in this worktree (branch `worktree-challenge-planning`),
committing frequently with only your name. Push to GitHub (your public fork) at
milestones, after a secret scan. Merge to `main` at submission time. ➜

### Q-9. Synthetic users
**Default:** three users: `alex.morgan`, `jamie.lee`, and `sam.taylor` (locked, so
the reset also unlocks it). Inbox passwords are generated randomly into Key Vault.
➜

### Q-10. Transcript retention
**Default (changed after research):** transcripts are deleted automatically after
**14 days**. Assessment plus feedback can take more than a week, and transcripts help
diagnose the interviewer's findings for the video. Azure's deletion job may run up
to about a day late. Soft delete is switched off, so deleted really means deleted.
Alternative: 7 days. ➜

### Q-11. Call limits
**Default:** browser calls max 10 minutes, phone calls max 4 min 50 s (the trial
number cuts at 5 min), max 60 turns, 3 off-topic redirects, silence → one prompt
after 20 s, goodbye after 40 s. ➜

### Q-12. Agent voice and name
**Default:** a natural English voice (Azure `en-US-Ava` HD). No persona name: it
introduces itself as "the automated password reset assistant". ➜

### Q-13. Reading the code back
**Default:** the agent reads the digits back ("I heard 0-4-7-1-9-2, is that
right?") **before** submitting, so a misheard digit doesn't burn one of the two
attempts. ➜

### Q-17. Continuous integration
**Default:** no GitHub Actions. Tests run locally before each deploy, and a deploy
script does the rest (simpler). Alternative: a small Actions workflow that runs
`dotnet test` on push (15 min of work). ➜

### Q-18. Budget alert
**Default:** a budget of **$50** on the resource group, with email alerts at 50%
and 80%. Which email should receive the alerts? (It's stored only in Azure, never
in the repo.) ➜

---

## Questions from the step plans

(Collected from the end of each plan file. The numbering follows the step, e.g.
Q-5.2 is question 2 of step 5.)

Short version below. The full reasoning is at the end of each plan file. Only the
**bold** ones really need your eyes; for the rest, the default is fine for almost
anyone.

### Step 4: Azure setup ([plan](step-04-azure-setup.md))
- **Q-4.1 Your email for budget alerts** (passed at deploy time, never committed). ➜
- Q-4.2 Voice Live with **no API keys at all** (Entra ID only, your `az login` +
  the app's managed identity). Default: yes. ➜
- Q-4.3 Session affinity on the agent app. Default: on (harmless with one instance). ➜

### Step 6: Backend core ([plan](step-06-backend-core.md))
- Q-6.1 Keep a hash of (recovery + code) briefly, so a retry after a network glitch
  doesn't burn a second attempt. Default: yes. ➜
- Q-6.2 Escalate after 2 "issuer didn't answer" in one call. Default: 2. ➜
- Q-6.3 / Q-6.4 Ticket reasons for "link expired" and "reset failed" (not in the
  contract's list). Defaults: `verification_expired` / `dependency_unavailable`. ➜
- Q-6.5 Any end of call before completion → ticket `cancelled`/`call_dropped`, which
  can still become `resolved` later. Default: yes. ➜
- **Q-6.6 Username-limit wording:** "I can't start another reset on this call.
  Please contact the help desk directly." (No ticket can be offered before a
  recovery exists.) ➜
- Q-6.7 Install Azurite for 5 extra local Table Storage tests. Default: no. ➜
- Q-6.8 Test runner: Microsoft Testing Platform. Default: yes. ➜
- **Q-6.9 If the storage setting is missing in Azure:** fall back to in-memory with a
  warning (default), or refuse to start outside Development? I lean towards
  **refuse to start in Azure** (safer, because restart safety can't silently
  disappear). Your call. ➜
- Q-6.10 Ignore unknown fields in issuer responses (the contract allows extra
  metadata). Default: ignore. ➜
- Q-6.11 Allow "check status" after an escalation if a link was already sent.
  Default: yes. ➜
- Q-6.12 Create the Table client directly. Default: yes. ➜

### Step 8: Reset form ([plan](step-08-reset-form.md))
- Q-8.1 xUnit v3 everywhere. Default: yes. ➜
- Q-8.2 Browser tests download Chromium (about 150 MB) on the first run. Default: OK. ➜
- Q-8.3 Clear the password fields after a policy rejection. Default: yes. ➜
- **Q-8.4 One exception to the clean-console rule:** when the issuer answers with an
  *expected* error (link expired, already used, throttled, service down), Chrome
  itself prints "Failed to load resource" in the console. A web page can't
  suppress this. The happy path stays perfectly clean. Default: accept it and
  document it. ➜
- Q-8.5 The live test needs a fresh reset link pasted by hand. Default: yes. ➜
- Q-8.6 After an uncertain result, the form doesn't allow a second submission.
  Default: yes. ➜
- Q-8.7 Show the safe failure code ("Reference: dependency_unavailable"). Default: yes. ➜
- Q-8.8 Live-test password generator (23 characters). Default: keep. ➜

### Step 9: Phone ([plan](step-09-phone.md))
- See **Q-6** above. Q-9.1 If no number: write no untested ACS code. Default: no
  code, just the design doc. ➜

### Step 10: Transcripts ([plan](step-10-transcripts.md))
- Q-10.1 = Q-10 above (14 days).
- Q-10.2 Leave out caller turns whose recognition failed. Default: leave out. ➜
- Q-10.3 No ticket ID inside the transcript (the session ID links them). Default: yes. ➜
- Q-10.4 No extra "system" turns. Default: not now. ➜
- Q-10.5 Write the transcript once at the end of the call (plus a stub after a
  restart). Default: yes. ➜
- Q-10.6 Give your own account read access to transcripts until the video is done.
  Default: yes. ➜
- Q-10.7 Keep agent sentences that were cut off by an interruption. Default: keep. ➜

### Step 11: Reliability ([plan](step-11-reliability.md))
- Q-11.1 A live call is lost when the server restarts (state and ticket survive).
  Default: accept and document. ➜
- Q-11.2 "Asked for a human, then the reset completed": keep the escalation (the
  contract says so). Default: yes. ➜

### Step 12: Testing ([plan](step-12-testing.md))
- Q-12.1 Run the guardrail conversations 3 times against the real model (a few
  cents). Default: yes. ➜
- **Q-12.2 Can you do a 30-minute manual voice test on Sunday evening** with a
  headset (12 short scenarios)? ➜
- Q-12.3 No abusive test sentences in the repo (the content filter is tested with a
  fake). Default: yes. ➜

### Step 13: Deploy ([plan](step-13-deploy.md))
- Q-13.1 Every deploy runs the full test suite. Default: yes. ➜

### Step 14: Documentation ([plan](step-14-documentation.md))
- Q-14.1 Setup guide in PowerShell only (works on any OS with PowerShell 7).
  Default: yes. ➜

### Step 15: Submit ([plan](step-15-submit.md))
- Q-15.1 Merge into `main` and pin the commit there. Default: yes. ➜
- **Q-15.2 The `phone_e164` field:** the schema *requires* a US toll-free number.
  Without a phone (expected, see Q-6), the JSON can't be fully valid. Default: put
  a clear explanation in `notes` and in your message, and state that the browser
  voice page is the agreed alternative. We must never invent a number. ➜
- **Q-15.3 Your `submission_id`** (reused at the video stage). Default:
  `gs-voicereset-2026-10`. ➜
- Q-15.4 How do you send it to the interviewer (email or portal)? You do this
  yourself. ➜

### Step 16: Video ([plan](step-16-video.md))
- Q-16.1 Video hosting: OneDrive "anyone with the link". Default: yes. ➜
- Q-16.2 You narrate in English from prepared talking points. Default: yes. ➜

### Step 5: Mock services ([plan](step-05-mock-services.md))
- Q-5.1 Idempotency keys for "verify" and ticket updates are scoped per recovery/ticket.
  Default: keep (documented). ➜
- Q-5.2 Nothing makes a reset **fail** in the mock, so the agent's "reset failed"
  path can only be unit-tested. Default: leave it out. Alternative: a
  `Faults__ResetFailureUsernames` setting (about 30 min of work) to demo it live. ➜
- Q-5.3 An extra `tokens` table (token hash → recovery). Default: keep. ➜
- **Q-5.4 Same as Q-6.9:** if the storage setting is missing → in-memory with a
  warning, or refuse to start in Azure? I recommend **refuse to start outside
  Development** for both apps. ➜
- Q-5.5 The mock store methods take no cancellation token (simplicity; reason written
  down). Default: keep. ➜
- Q-5.6 Each table row stores its record as one JSON column. Default: keep. ➜
- Q-5.7 Password policy: 7 rules (min 12 characters, upper, lower, digit, symbol, not
  the username, not the current password), version `2026-10-v1`. Default: keep. ➜
- Q-5.8 Rate limits: 300 API calls/min per IP, 5 inbox logins/min per IP. Default: keep. ➜
- Q-5.9 A human-requested escalation stays escalated even if the reset completes later
  (the receipt is attached). Updates after "resolved" answer 200 with the unchanged
  ticket. Default: keep. ➜
- Q-5.10 Ticket ID format `tkt_<recovery id>` (one per recovery, no extra index).
  Default: keep. ➜
- Q-5.11 Azurite tests optional and skipped. Default: yes. ➜
- Q-5.12 An exhausted recovery keeps showing `exhausted` after its 2-minute window
  (not `expired`). Default: keep. ➜

### Step 7: Voice agent ([plan](step-07-voice-agent.md))
- Q-7.1 A wrong access code answers "200 + ok:false" instead of a 401 error (a 401
  would print a red line in the browser console). Default: yes. ➜
- **Q-7.2 Live captions on the voice page: the agent's words only.** Showing the
  caller's words too would put the code (and any spoken password) on screen.
  Default: agent only. ➜
- Q-7.3 Fixed sentences (safe line, goodbye, check-ins) are spoken word for word
  through a less common Voice Live feature, with a fallback if it doesn't work.
  Default: yes. ➜
- **Q-7.4 Off-topic strikes.** The guardrail rule says "3 off-topic requests →
  polite end". The code can only *see* off-topic talk if the model reports it
  through an **eighth tool** (`report_off_topic`, no arguments, about 20 lines plus
  tests). Without it, an endless off-topic caller is stopped by the turn or time
  limit instead. The plan's default is "no eighth tool"; **I recommend adding it**
  (cheap, and it makes the guardrail real and testable). ➜
- Q-7.5 A new setting `Access__AllowedOrigin` (already added to Bicep). Default: yes. ➜
- Q-7.6 The first real conversation happens in Azure. (Running locally would need
  you to trust a development certificate on Windows.) Default: Azure. ➜
- Q-7.7 Access cookie lasts 2 hours. Default: yes. ➜
- **Q-7.8 Rate limits:** 5 access-code tries per minute and 10 call starts per 10
  minutes per IP, 10 calls at the same time. If the interviewer's automated
  tester runs many calls from one IP, it may hit these. Default: keep; we mention
  them in the notes so they can be raised. ➜
- Q-7.9 The greeting is written by the model (must say it's automated). Default: yes. ➜
- Q-7.10 Session ID format clash: **already fixed overnight** (step 10 now accepts
  step 6's IDs). Nothing to decide.
- Q-7.11 Model settings: gpt-4.1-mini, temperature 0.6, short answers (300 tokens),
  Ava HD voice. Default: yes. ➜
- Q-7.12 Silence: re-prompt after 10 s, end after 40 s; during the browser step a
  check-in every 60 s and no hang-up. Default: yes. ➜
- Q-7.13 `/access` accepts JSON only. Default: yes. ➜
- Q-7.14 Application Insights uses its connection string (not a managed identity).
  Default: yes. ➜
