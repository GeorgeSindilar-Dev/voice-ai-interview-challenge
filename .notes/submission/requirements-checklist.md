# Challenge requirements checklist

Every requirement from the challenge [README](../../../README.md) and the
[mock contract](../../../docs/mock-contract.md), in one place. Tick an item only
when it is implemented **and** there is evidence (code, a test, or a doc) to point
to. Items we deliberately don't do go under **known limitations** in the setup
document instead.

Sources: **R** = README, **MC** = mock contract.

---

## A. Submission package

- [ ] `submission.json` matches [contracts/submission.schema.json](../../../contracts/submission.schema.json), validated with a draft 2020-12 validator. (R)
  - [ ] `schema_version` is `"1.0"`; `stage` is `initial`, later `walkthrough`
  - [ ] `submission_id`: nonblank, and the same in both stages
  - [ ] `phone_e164`: the real US toll-free number (if no phone, see notes)
  - [ ] `reset_base_url`: HTTPS, no credentials, query or fragment
  - [ ] `repository_url`: the `https://github.com/owner/repo` root; `commit_sha`: the full 40-hex SHA
  - [ ] `azure_architecture`: at most 500 characters
  - [ ] `setup_document`: the repo-relative path of our setup doc at that commit
  - [ ] `video_url`: `null` at the initial stage; a private HTTPS link at walkthrough
  - [ ] `notes`: limitations, assumptions and operational context, with no secrets
  - [ ] no extra fields
- [ ] **`submission.json` is never committed.** The repo is public and the file contains the phone number and endpoints. (R)
- [ ] Shared privately with the interviewer, **never** through public GitHub issues. (R)
- [ ] Nothing published anywhere: phone endpoints, recordings, credentials, tenant IDs, passwords, raw logs. (R)
- [ ] The deployment matches the pinned SHA, shown by a **health/version endpoint** that returns the commit SHA. (R)
- [ ] Any scoped access (for example the voice page access code) is shared separately, through a secure channel. (R)

## B. Setup document (`setup_document`)

A guide for reproducing the project on someone else's system: build and test on
their computer, then deploy into their own Azure subscription. The README requires
it to contain:

- [ ] exact build, test and run (deploy) commands
- [ ] isolated test configuration: how to deploy your own copy for restart testing, without production access
- [ ] browser/backend boundaries: what runs where, and what each side can see
- [ ] required configuration names, **without values**
- [ ] known limitations
- [ ] cleanup: how to remove all resources
- [ ] how to reach the agent: the voice page and access code process, the inbox, the reset form, and the phone

## C. Stack and data

- [ ] Azure/ACS-based stack: Voice Live + ACS telephony. (R)
- [ ] Only pre-enrolled **synthetic** accounts and mock systems. No real users, real passwords or Entra password writes. (R)
- [ ] Reuse of the reference template (if any) respects its MIT license and is attributed. (R)

## D. The journey

- [ ] **1.** The caller gives an identifier. Unknown accounts get the same response; no directory data is revealed. (R, MC)
- [ ] **2.** The recovery starts and the code is delivered to the mock inbox (separate inbox login). The caller speaks the code. (R)
- [ ] **3.** After verification, the reset link is delivered to the same inbox and **never** disclosed by the agent. The backend can't read inbox, codes or tokens. (R, MC)
- [ ] **4.** The new password is entered privately in our HTTPS form and sent to the reset backend, which enforces policy. Only safe violation codes/descriptions reach the agent. (R, MC)
- [ ] **5.** Success is confirmed **only** from an authoritative receipt (plus unlock if required). Ambiguous completions are reconciled, never guessed. (R, MC)
- [ ] **6.** A truthful ticket outcome and an accurate voice confirmation. Honest escalation when the browser is unavailable, verification is exhausted, or a human is requested. No fake human transfer. (R, MC)

## E. Security rules

- [ ] The backend, not the model, enforces state transitions and binds operations to the right account and recovery. The model can't authorize itself. (R, MC)
- [ ] Caller ID, employee-ID fragments, session IDs, and two self-created channels are **not** authorization. (R, MC)
- [ ] Codes expire after 120 s. Two completed wrong submissions exhaust the attempt. Speech fragments are clarified first and are not attempts. (R, MC)
- [ ] Links use high-entropy tokens (≥128 bits), expire after 10 min, and are single-use, account/session-bound, replay-safe and idempotent. (R, MC)
- [ ] Never solicit or repeat current or new passwords. (R)
- [ ] Browser passwords never reach model inputs, prompts, tool arguments, transcripts, analytics or logs. (R, MC)
- [ ] Unsolicited spoken secrets: not repeated, and the handling is documented (no promise of perfect redaction). (R, MC)
- [ ] Codes may be used during the live conversation but are **not retained in general logs**. (MC)
- [ ] Reset form: reads `#token=`, then removes it from the address bar. No third-party analytics, no referrer leakage. (MC)
- [ ] If a backend proxy is used: it doesn't log bodies, restricts origins, and is protected against cross-site requests. (MC)
- [ ] The service credential is kept server-side and never exposed to the browser. (MC)
- [ ] Requests are rejected on incorrect field types or unexpected fields. (MC)
- [ ] No credentials, tokens or passwords in logs, analytics, errors, model context or tool payloads. (MC)

## F. Reliability (what the assessors exercise)

- [ ] Ambiguous or invalid speech. (R)
- [ ] Timeouts. (R)
- [ ] Cancellation and dropped calls: stop new work, keep issuer history, honest ticket, and no claim that a link was revoked. (R, MC)
- [ ] Duplicate events: idempotency keys per logical action (`request_id`, `Idempotency-Key`, `operation_id`). (R, MC)
- [ ] Concurrent sessions: fully isolated; a caller-supplied ID never attaches another session. (R, MC)
- [ ] Process restarts: durable correlation/idempotency state and reconciliation, plus instructions for an isolated deployment. (R, MC)
- [ ] Timeouts, `503` and temporary `404` are **not** treated as success, nor as proof that nothing happened. (MC)
- [ ] Ticket rules: one ticket per recovery; `resolved` only with a valid receipt; history retained; stale events never overwrite a success; a human request is preserved. (MC)

## G. Conversation quality

- [ ] Clear guidance and expectation setting (inbox + browser needed, 2-minute code). (R)
- [ ] Ambiguity handling: clarify before acting. (R)
- [ ] Interruption (barge-in) and cancellation behaviour. (R)
- [ ] Honest outcomes, always. (R)
- [ ] Latency is reasonable. It's measured, but there's no fixed target. (R)

## H. Mock services we build (contract v1)

The interviewer didn't supply them, so we implement the candidate-facing API:

- [ ] `POST /v1/recoveries` (including decoy recoveries for unknown accounts)
- [ ] `POST /v1/recoveries/{id}/verify` (`Idempotency-Key`, attempt counting, expiry)
- [ ] `POST /v1/recoveries/{id}/reset-link` (token, 10-minute expiry, delivered to the inbox)
- [ ] `GET /v1/policy`
- [ ] `POST /v1/password/validate`
- [ ] `POST /v1/resets` (atomic token binding, receipts, `token_used`)
- [ ] `GET /v1/reset-operations/{operation_id}` (service auth or matching `ResetToken`)
- [ ] `GET /v1/recoveries/{id}`
- [ ] `POST /v1/tickets` and `POST /v1/tickets/{id}/outcome`
- [ ] The error format and HTTP codes from the contract; `Retry-After` on `429`
- [ ] Throttling: one active recovery per account; one new code per account per 120 s
- [ ] Mock inbox: a self-refreshing web page with its own separate login (no API)
- [ ] Pre-enrolled synthetic users (including one that needs an unlock)

## I. Video walkthrough (after feedback)

- [ ] 5–10 minutes; screen recording with narration or captions. (R)
- [ ] Shows or refers to the actual pinned artifact. (R)
- [ ] Architecture and trust boundaries. (R)
- [ ] Demonstrates the core journey, or refers to the assessment evidence. (R)
- [ ] Diagnoses the sanitized findings and proposes fixes. (R)
- [ ] Scaling and session isolation. (R)
- [ ] Cost drivers and assumptions. (R)
- [ ] Incident response: detection, containment, reconciliation, safe recovery. (R)
- [ ] What works now versus what would change before real production. Never calls anything "production-ready". (R)
- [ ] If code changed after feedback: the new SHA and matching deployment, with the earlier findings kept. (R)

## J. Our own additions (beyond the challenge)

- [ ] A browser voice page with the same behaviour as the phone, behind a private access code
- [ ] Conversation transcripts in Blob storage, with codes and spoken passwords masked, and limited retention
- [ ] Voice agent guardrails (on-topic only), with every attack scenario in [04-guardrails-plan.md](../planning/04-guardrails-plan.md) tested and the results documented
- [ ] Automation-friendly, accessible pages (semantic HTML, labels, stable IDs), so automated testers follow the same path as humans; the voice WebSocket is briefly documented
- [ ] Full documentation: architecture, infrastructure, process, submission
- [ ] A clean browser console on every page: no logs from the voice agent, and no errors or warnings, during a full journey and the failure paths
