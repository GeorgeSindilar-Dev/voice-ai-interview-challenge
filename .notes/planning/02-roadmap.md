# Roadmap: what we have to do

The high-level steps for the whole challenge, in a sensible order. These are not
tasks yet: for each step we will later define concrete tasks.

**The plan we follow:** the lean plan (built; history in git). This roadmap was the starting point.

**Deadline:** initial submission by Monday, 2026-10-05. The recorded video comes
later, after the interviewer sends feedback.

**Ordering principle:** reduce risk early. The browser voice agent comes before the
phone, so we always have something that works even if telephony fails.

**Channels:** the browser voice page is the primary channel. The phone line is
optional: the interviewer confirmed that if a toll-free number isn't possible, any
other way of talking to the agent is fine. **Everything runs in Azure**: the
interviewer gets access and talks to the agent there.

---

**Automated callers:** the interviewer may test with **his own AI agent acting as the
caller** (the README mentions "automated calls"). So:
- **The phone line is worth real effort.** It's the most natural way for an
  automated caller to reach us. It stays optional, but it has high value.
- **The same path as a human, with no special APIs.** An automated tester uses the
  phone or the voice page to talk, and browser automation (for example Playwright)
  to read the inbox page and fill in the reset form, exactly like a person. So the
  pages are **automation-friendly and accessible**: semantic HTML, proper labels,
  stable element IDs.
- The voice page's audio WebSocket is documented briefly, so a bot that can't use a
  browser microphone can connect to that **same** endpoint.
- **Bots are treated exactly like humans**: no bot detection.

Our agent also has to cope with how bots behave:
- they talk over our agent, or answer instantly
- they systematically try prompt injection
- they get stuck in polite loops ("Anything else?" "No, thank you!" …), so we need a
  maximum call duration and turn limit
- they run calls in parallel, so we need session isolation and graceful handling of
  Voice Live quota limits

---

### 1. Clarify and decide
Settle the open decisions:
- ~~programming language~~ → **decided: .NET (C#, ASP.NET Core)**
- where the agent lives (see [01-decision-where-the-agent-lives.md](01-decision-where-the-agent-lives.md))
- where session state is stored
- how the mock services are hosted
- what we consciously leave out

### 2. Research
Run the two research items in the research list (done): coding best practices for
the chosen stack, and guardrails used by similar voice agents. Feed the results
into CLAUDE.md.

### 3. Design the architecture
Draw the components, trust boundaries ("who can see what"), the recovery state
machine, the tools the model may call, and the data we persist. Output: a short
architecture document that the code and the video both refer to.

### 4. Set up Azure
Create the subscription and a single resource group containing:
- Foundry with Voice Live
- ACS, with a trial toll-free number
- Event Grid
- App Service
- a storage account (session state and the transcript Blob container), Key Vault
  and Application Insights

Add a budget alert and a cleanup plan.

### 5. Build the mock services
We build these ourselves, since the interviewer didn't supply them:
- the mock issuer (recovery, verification codes, reset links, password policy,
  resets, receipts)
- the mock ticket service
- a mock inbox with its own separate login: a fake-email web page that refreshes
  itself (no API: testers use the page, like a human)

The mocks run as **their own web app** (same App Service plan, no extra cost), with
their own data, so our voice backend can't read inbox contents, codes or tokens.
Synthetic users' inbox logins are shared privately, never committed.

They follow [docs/mock-contract.md](../../docs/mock-contract.md) closely.

### 6. Build the backend core (no voice yet)
The recovery state machine, the session store, the clients for the issuer and
ticket APIs, and the reconciliation worker. All of it is testable without any AI or
phone.

### 7. Build the voice agent
- the prompt and guardrails
- the tools the model may call
- the Voice Live connection
- the **browser voice page** as the first audio channel

The voice page must **keep working in a background tab**, because the user switches
to the mock inbox and the reset form during the call:
- audio capture and playback run in an `AudioWorklet` (not throttled in background
  tabs)
- no timers in the page (timeouts live in the backend)
- links open in new tabs
- a lost connection is handled as a dropped call, with a visible "Connection lost"
  message

Desktop browsers support this. Mobile browsers (especially iOS Safari) stop the
microphone in background tabs, so the notes recommend desktop or the phone line.

### 8. Build the browser reset form
A minimal HTTPS page. It reads the token from the link, enters the password
privately, validates it, and submits it to the reset backend. The password never
goes near the voice side.

### 9. Connect the phone line (optional)
Wire the inbound toll-free number (ACS + Event Grid) to the same backend session.
After this, the phone and the browser are two entry points to the same agent.

This is a stretch goal, not mandatory. **Update (research, 2026-10-03):** Microsoft
announced the ACS retirement in September 2026, and new ACS tenants can't get phone
numbers anymore. The step starts with an eligibility check. If no number is
possible (expected), the phone channel is documented (design + reason) instead of
built.

### 10. Save conversation transcripts
Store each conversation's transcript (from both the phone and the browser) in an
Azure Storage **Blob** container: one transcript per session, linked to its session
and ticket IDs.

The challenge restricts what transcripts may contain, so this needs care:
- **Passwords never appear.** Browser passwords never reach the voice side anyway.
  A password the caller *speaks* anyway is masked.
- **Verification codes are masked.** They may be used during the call, but must not
  be kept in stored records.
- **No reset links or tokens.**

The container is private (no public access) and reached through managed identity.
A lifecycle rule deletes transcripts after a short retention period.

### 11. Harden reliability
Timeouts, cancellation, dropped calls, duplicate events, concurrent sessions,
process restarts, and reconciling ambiguous results. These are the things the
assessors explicitly exercise.

**Restart safety:** session state lives in storage, not memory. On startup, a
reconciliation step finishes open sessions and tickets truthfully, checking with
the issuer what really happened.

### 12. Test
Unit tests for the state machine and the rules, scenario tests for the edge cases,
and real end-to-end runs through the browser and the phone.

**Clean browser console:** after each journey (happy path, microphone denied, wrong
code, connection lost), read the browser console of the voice page, reset form and
inbox. It must have no logs, errors or warnings.

**Restart evidence:**
- an automated integration test stops the app mid-reset, starts a fresh instance on
  the same storage, and checks that the outcome is truthful
- one manual restart test on a **second copy** in Azure (a second web app on the same
  App Service plan, not the live app), with the result documented

### 13. Deploy and pin the version
Deploy a specific commit. Expose a health/version endpoint that shows the commit
SHA.

**How the interviewer talks to the agent:** everything runs in our Azure
deployment.
- **Browser voice page,** hosted on App Service behind a **private access code**
  (decided). The owner sends the code to the interviewer separately, so strangers
  can't spend the Voice Live credit. The code is stored in Key Vault, never in the
  repo or a URL, and it gates access only: it is never part of reset authorization.
- **Phone,** if we get the toll-free number.

Keep the deployment running, on the same commit, until feedback and the video are
done.

**Deploy-your-own-copy guide:** the README asks for instructions so the assessors can
run restart tests on their own isolated copy, never on our live app. Our Azure
deployment guide covers this: short steps to deploy the same commit into another
subscription. It's all in Azure; there is no local run.

We can't ask the interviewer questions before submitting, so any assumptions we
make are stated in the submission `notes` and the docs.

### 14. Complete the documentation
Documentation is written **alongside each step**, not left to the end. This step
reviews it for completeness and consistency. There are four areas, all in `docs/`:

**1. Code and architecture** (`docs/architecture/`)
- system overview and component diagram
- trust boundaries: who can see what
- the recovery state machine and the main sequence flows (happy path, failures)
- the code guide: solution structure, the main classes and responsibilities, the
  design patterns used and why
- decision records: one short page per significant decision (for example
  [01-decision-where-the-agent-lives.md](01-decision-where-the-agent-lives.md))
- guardrails, and the secret/data-handling rules

**2. Infrastructure** (`docs/infrastructure/`)
- every Azure resource: what it's for, its SKU/tier, and why
- identities and permissions (managed identity and roles)
- configuration names, without values
- deployment steps and the pinned-commit/version check
- monitoring (Application Insights), and transcript storage and retention
- cost drivers and the budget
- cleanup

**3. Process** (`docs/process/`)
- **The business process:** the password-reset journey, escalation paths, and
  ticket outcomes.
- **Operations:** runbooks for incident detection, containment, reconciliation and
  safe recovery.
- **The engineering process:** how we planned and built it (this `docs/planning/`
  folder), how to build, test and deploy, and known limitations.

**4. Submission** (`docs/submission/`): everything the challenge itself requires
- **`SETUP.md`**: the `setup_document` from the submission. It's **a guide for
  reproducing the project on someone else's system**: build and test on their own
  computer, then deploy into their own Azure subscription. Outline:
  1. prerequisites (.NET 10 SDK, Azure CLI, Git, an Azure subscription)
  2. get the code at the pinned commit
  3. build and test: the exact commands
  4. create the Azure resources, step by step
  5. configure: setting names, without values, and where they go
  6. deploy both apps: the exact commands
  7. seed the synthetic users and their inbox logins
  8. verify: the health/version endpoint shows the commit
  9. use it: voice page, inbox, reset form, phone
  10. what runs where (browser/backend boundaries)
  11. known limitations
  12. cleanup (delete the resource group)

  The same guide covers the assessors' isolated restart testing (deploy your own
  copy).
- **requirements checklist** (replaced by the spec audits in `../audit/`): every
  README and mock-contract requirement, ticked only with evidence.
- the draft submission `notes` (assumptions and limitations)
- video preparation: the talking points, mapped to the README's walkthrough prompts

Diagrams use Mermaid so they render on GitHub.

### 15. Submit (initial stage)
Push to a private GitHub repository, fill in `submission.json`, validate it against
the schema, and send it privately to the interviewer.

### 16. After feedback: the recorded walkthrough
A 5–10 minute video covering:
- the architecture
- a demo
- a diagnosis of the interviewer's findings, with proposed fixes
- scaling, cost and incident response
- what works now versus what we'd change for real production

Then the final `walkthrough` submission.
