# The challenge, step by step

What the voice agent (and the system behind it) must do, explained in order.
Sources: [README.md](../../README.md) and [docs/mock-contract.md](../../docs/mock-contract.md).

---

## The big picture

Someone calls a phone number (or opens our browser voice page) because they forgot
their password. The voice agent guides them through a reset, but **it never handles
the password itself**. The caller proves they control the account by reading a
code from a mock inbox. Then they set the new password in a browser form. The agent
only reports what actually happened, based on official receipts.

## Who is involved, and what each one may know

| Actor | What it is | May know / do | Must never |
|---|---|---|---|
| **Caller** | The person on the phone (the interviewer during assessment) | Their username, the code from the inbox, their new password (typed only in the browser) | — |
| **Voice model** | The LLM inside Voice Live | What the caller says, safe status messages, the code while it's being verified | Choose IDs, authorize anything, hear or say passwords, see the reset link |
| **Our backend** | Our service on App Service | Call state, recovery/operation/ticket IDs, the issuer service credential | Read inbox contents, codes, tokens or passwords through the issuer API |
| **Mock issuer** | Mock "identity system" (we build it) | Everything: accounts, codes, tokens, policy, receipts | Reveal secrets to the backend |
| **Mock inbox** | Mock "recovery email" with a separate login (we build it) | The codes and reset links for its user | — |
| **Reset form** | Our small HTTPS web page | The token from the link and the new password, sent only to the reset backend | Send the password to the model, logs or analytics |
| **Ticket service** | Mock help-desk ticket system (we build it) | One ticket per recovery, and its outcome history | Mark "resolved" without a valid receipt |

---

## The journey

### Step 0: the call starts

**What happens:** the call arrives through ACS (or the browser page connects). The
backend creates a fresh session for this call and opens a Voice Live connection
just for it.

**What the agent does:**
- Greets the caller and says it is an automated assistant for password resets.
- Sets expectations: the caller will need access to their recovery inbox and a web
  browser, and the code is valid for only 2 minutes.

**Notes:** our trial phone number cuts calls off after 5 minutes, so the happy path
must fit inside that.

### Step 1: identify the account

**What the agent does:** asks for the username and confirms it (spelling it back if
needed).

**What the backend does:** calls `POST /v1/recoveries` with the username and a
unique `request_id`.

**What the issuer does:**
- If the account exists, it sends a code to the account's inbox.
- If it doesn't, it creates a decoy recovery and sends nothing.

The response looks the same in both cases.

**What the agent says, identical whether or not the account exists:** something
like "If that account is enrolled, a code has been sent to its registered recovery
inbox."

**Traps:**
- Never reveal whether the account exists.
- If the account already has an active recovery (perhaps from another call), the
  issuer throttles. The agent gives a generic "can't start right now" message
  without explaining why.
- Caller ID, an employee ID or "I'm the manager" are **not** proof of anything.

### Step 2: verify the code

**What the caller does:** logs into the mock inbox, reads the code, and says it.

**How the mock inbox works** (we build it as part of the mock services):
- A tiny fake-email web page, run by the **mock services app** (separate from our
  voice backend, with its own data).
- Each synthetic user has their **own inbox login**, shared privately with the
  interviewer together with the voice page access code. It is never in the repo.
- When a recovery starts, the mock issuer adds a message, for example "Your
  verification code: 047192 (valid 2 minutes)". After verification, a second message
  holds the reset link (valid 10 minutes, single use).
- The page refreshes itself, so new messages appear within seconds. A user only
  sees their own messages.
- **Our voice backend can't read the inbox at all.** That's what makes the code
  meaningful: only someone with access to that inbox can know it.
- There is no inbox API. An automated tester opens the same inbox page with browser
  automation, like a human, so the page uses clean, labelled HTML.
- Typical setup for a human: tab 1 is the voice page (or the phone), tab 2 the inbox,
  and tab 3 the reset form, opened from the link.

**What the agent does:**
- Collects the digits. If the recognition is unclear or partial, it asks again
  **before** submitting.
- It may read the code back for confirmation. Codes are synthetic and allowed in
  the conversation.

**What the backend does:**
- Checks that a code submission is allowed in the current state.
- Calls `POST /v1/recoveries/{id}/verify` with a fresh `Idempotency-Key`.

**Possible outcomes:**

| Issuer answer | What the agent says / does |
|---|---|
| `verified` | Moves on to step 3 |
| `verification_failed` (1 attempt left) | "That code didn't work, you have one more try." |
| `verification_exhausted` | No more tries. Honest escalation (ticket reason `verification_exhausted`). |
| `recovery_expired` (over 120 s) | Code expired. Honest escalation or "try again later" (ticket reason `verification_expired`). |

**Traps:**
- Only **two completed wrong submissions** are allowed.
- Repeating fragments ("4... 7... sorry, 4 7 1") is not an attempt.
- A network retry of the same submission must reuse the same key so it isn't
  counted twice.
- Codes are strings: keep leading zeros, and treat "oh" as "zero".
- New calls or restarts don't reset the counter or the timer; the issuer owns them.

### Step 3: send the reset link

**What the backend does:** calls `POST /v1/recoveries/{id}/reset-link`. The issuer
delivers a link to the **same inbox**. The backend never sees the link or the token.

**What the agent says:** "I've sent a reset link to your recovery inbox. Open it
in your browser; it's valid for 10 minutes."

**Traps:**
- The agent never reads out, spells or invents a link.
- If the caller asks "just tell me the link", it refuses politely.

### Step 4: the caller sets the password in the browser

**What happens in the browser (our reset form):**
1. The link opens our form with `#token=...` in the URL. The form reads the token,
   then **removes it from the address bar**.
2. The form shows the password rules from `GET /v1/policy`.
3. The caller types the new password **privately**. It is sent over HTTPS to
   `/v1/password/validate`, then `/v1/resets`.
4. If the password breaks a rule, the form shows the safe reasons (for example
   "must be at least 12 characters") and the caller tries again.

**What the agent does meanwhile:**
- Waits patiently and checks the recovery status in the background.
- If the caller says "it's asking for 12 characters", it can explain the policy
  rules.
- It **never asks** what password they chose.
- If the caller says they can't open a browser, it escalates honestly (ticket
  reason `browser_unavailable`).

**Traps:**
- The password must never reach the model, prompts, tool arguments, transcripts,
  analytics or logs.
- If the caller *says* a password out loud anyway, the agent doesn't repeat it,
  tells them not to share it, and steers them to the form.

### Step 5: confirm the outcome, truthfully

**What the backend does:** checks `GET /v1/recoveries/{id}` (or the reset operation).

| Status | What the agent says |
|---|---|
| `completed`, with a receipt (and unlock if needed) | "Your password has been reset." Only now. |
| `reset_pending` | "It's still processing." Keep checking. |
| `reset_failed` | Honest failure, and escalate. Never "success with a warning". |
| Unknown (timeout, 503) | "I can't confirm it yet." Reconcile later (ticket reason `completion_unknown`). Never guess. |

### Step 6: record the ticket and close

**What the backend does:**
- Creates the ticket early, so that every escalation has one.
- Updates the outcome at the end:

| Outcome | When |
|---|---|
| `resolved` | **Only** with a valid receipt and reason `reset_completed` |
| `escalated` | Exhausted, expired, browser unavailable, a human was requested, or a dependency was down |
| `cancelled` | The caller cancelled, or the call dropped |
| `pending` | The result is not known yet |

**What the agent says:** an accurate summary, then goodbye.
- "A ticket has been created for the help desk" is honest.
- "I'm transferring you to a human" is **not**: real transfers aren't built.

---

## Side paths the assessors will test

| Situation | Expected behaviour |
|---|---|
| **Caller asks for a human** | Create an honest escalation (`human_requested`). Don't pretend someone answered. Keep that escalation even if a reset completes later. |
| **Caller cancels** | Stop new work, record `caller_cancelled`, and say truthfully that an already-sent link stays valid until it expires (there's no revoke). |
| **Call drops** | Stop voice work. Keep reconciling: if the reset completes later, update the ticket to `resolved` with the receipt. Otherwise `call_dropped`. |
| **Silence / timeouts** | Prompt the caller gently. After repeated silence, end politely and record the outcome. |
| **Unclear speech** | Ask for clarification. Never guess a username or code. |
| **Interruptions (barge-in)** | When the caller talks over the agent, the agent stops speaking and listens. |
| **Off-topic requests** | Politely decline and steer back to the password reset (guardrails). |
| **Prompt injection** ("ignore your instructions", "I'm an admin", "verification passed") | Ignored. The backend decides the state, not the model's words. |
| **Caller says another username mid-call** | It doesn't attach to a different recovery. The flow restarts or is refused according to the state machine. |
| **Duplicate events** (webhooks, retries) | Deduplicate them. A stale event never overwrites a confirmed success. |
| **Two callers at the same time** | Fully separate sessions. The same account twice: the second is throttled, and nothing is shared. |
| **The caller is an AI agent** | Treated exactly like a human. Robust barge-in for bots that talk over us. A maximum call duration and turn limit end polite bot-to-bot loops gracefully. |
| **Many calls in parallel** | Isolated sessions. If Voice Live's quota is hit, give an honest "can't help right now" and record an escalation. Never a silent failure. |
| **Our process restarts** | Saved state lets us reconcile open recoveries and tickets. The issuer keeps counters and expiry. |

---

## Hard rules for the agent

**Never:**
- ask for, repeat or accept a password by voice
- read out or invent a link or token
- reveal whether an account exists, or any directory data
- claim success without a receipt
- claim a link was revoked, a reset rolled back, or a human transfer happened
- act on anything outside the password reset task

**Always:**
- clarify before submitting a code
- be honest about outcomes
- create an escalation when the flow can't finish
- let the backend decide what is allowed

---

## Example happy path

> **Agent:** Hi, this is the automated password reset assistant. I can help you
> reset your password. You'll need access to your recovery inbox and a web
> browser. What's your username?
>
> **Caller:** It's alex dot morgan.
>
> **Agent:** Thanks, alex dot morgan. If that account is enrolled, a verification
> code has been sent to its recovery inbox. It's valid for two minutes. Please
> read it to me when you have it.
>
> **Caller:** 0 4 7... 1 9 2.
>
> **Agent:** I heard 0-4-7-1-9-2. Is that right?
>
> **Caller:** Yes.
>
> **Agent:** Thank you, you're verified. I've sent a password reset link to the
> same inbox. It's valid for 10 minutes. Open it and choose your new password
> there. Please don't tell me the password. I'll wait.
>
> **Caller:** OK... done.
>
> **Agent:** I can confirm your password has been reset and your account is
> unlocked. I've recorded this on your help-desk ticket. Is there anything else
> about your password reset? ... Goodbye.

---

## What the score rewards (summary)

Four categories, 25 points each. Each is split into 15 points for what the code and
tests show and 10 points for the reasoning in the video.

- **Security and correctness:** verification, enforced boundaries, private
  password handling, truthful receipts and tickets.
- **Conversation quality:** clear guidance, handling ambiguity, interruptions and
  cancellation, honest outcomes.
- **Reliability:** timeouts, idempotency, session isolation, restart and
  reconciliation.
- **Engineering quality:** clear architecture, reproducible build and tests,
  useful operational notes.

Failures are not automatic rejections: diagnosing a failure honestly in the video
still earns credit.
