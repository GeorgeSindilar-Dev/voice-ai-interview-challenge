# Voice AI engineering challenge

Build a small **inbound, voice-assisted password reset** and explain how you would
operate it responsibly. We assess your ability to independently own an Azure voice
agent end to end: security, caller experience, reliability, and engineering
judgment. This exercise demonstrates capability; it is not production certification.

**Readiness:** this repository is a specification, not a running starter, evaluator,
or available sandbox. Before issuing it, the interviewer must supply working
sandbox access, a usable toll-free test number/resource path, and versioned mock
identity, reset, and ticket services or ready sample adapters matching the
[mock contract](docs/mock-contract.md). You are not asked to build those services,
an evaluator, or provisioning scripts.

## Your task

Own an inbound US toll-free phone endpoint that the interviewer can call, plus a
minimal HTTPS browser reset form. You own its implementation and behavior; the
interviewer supplies or allocates usable sandbox telephony resources, so buying a
number is not required.

Use an **Azure/Teams/ACS-based stack**. Azure Voice Live or an Azure speech/LLM
architecture, with Azure Communication Services (ACS) or Teams telephony, are valid.
Language and framework are flexible; an arbitrary non-Azure managed voice platform
does not satisfy the stack requirement.

Use only pre-enrolled synthetic accounts and mock enterprise systems. No real
users or passwords, Entra password writes, purchases, or production integrations
are required or permitted for this exercise.

### Expected effort

The **average/expected hands-on commitment is 1-4 hours**, including setup after
access is ready, implementation, concise notes, and the final recorded walkthrough.
Provisioning/account-approval waits and waiting for assessment feedback are
excluded. This is **not a time limit, deadline, or speed score**. Prioritize a small
coherent implementation; document omissions and tradeoffs rather than overbuild.

AI coding tools and templates are allowed. We assess understanding and ownership,
not presumed authorship. There is no mandatory AI-use disclosure log or ban.
Respect attribution and license obligations for anything reused.

## End-to-end journey

1. A caller reaches your inbound number and provides a synthetic account identifier.
   Avoid revealing directory data or distinguishing unknown accounts to the caller.
2. Begin the approved mock recovery flow. The issuer delivers a verification code
   to the account's registered mock inbox, accessed with a separate, pre-enrolled
   user/browser credential. The caller may speak this **synthetic verification
   code**. Caller ID, employee-ID fragments, session IDs, or two self-created
   channels are not authorization.
3. After issuer-approved verification, request a secure reset link delivered to
   that same inbox, never disclosed by the voice agent. The candidate service
   credential cannot read the inbox, codes, or link tokens.
4. The caller opens the link and enters the new synthetic password privately in
   your browser form. Send it over HTTPS to the reset backend. The backend enforces
   policy; only safe violation codes/descriptions reach the voice agent.
5. Confirm success only from an authoritative reset receipt, including unlock if
   the fixture requires it. Reconcile an ambiguous completion instead of guessing.
6. Record a truthful mock ticket outcome and give an accurate voice confirmation.
   If the browser is inaccessible, verification is exhausted, or a human is
   requested, create an honest escalation. Do not pretend a human has answered;
   real human transfer is not required.

The backend, not the model, must enforce state transitions and bind operations to
the correct account and recovery session. A model may propose tool calls but
cannot authorize itself. The mock recovery channel simulates independently
verified recovery access; it does **not** establish real-world identity assurance.

Codes expire after **120 seconds**. **Two completed wrong code submissions**
exhaust the recovery attempt; repeated speech fragments are not extra attempts.
The issuer owns throttling, retry counters, and expiry across calls and restarts.
Links use high-entropy tokens, expire after **10 minutes**, and are single-use,
account/session-bound, and replay-safe with idempotent completion. See the mock
contract for retries, cancellation, and authoritative status.

Never solicit or repeat current/new passwords. Browser-entered passwords must
never reach model inputs, prompts, voice-tool arguments, transcripts, analytics,
or logs. The reset backend necessarily handles the HTTPS request safely; this is
not a claim that no server ever sees a password. Unsolicited spoken secrets can
reach raw audio or speech models: stop soliciting/repeating them, use synthetic
data, and document handling rather than promise perfect redaction.

## What we exercise

Assessment covers the normal journey and **ambiguous/invalid speech, timeouts,
cancellation/dropped calls, duplicate events, concurrent sessions, and process
restarts**. Restart testing happens only in an assessor-controlled isolated
deployment, never by killing your live cloud application. Supply instructions to
run your code there without production access.

We measure latency and interruption behavior but judge the overall caller
experience, with no arbitrary numerical latency pass/fail target. Stress failures
are evidence to score and diagnose, not a demand to spend days building an
enterprise platform. There are no unpublished surprise mandatory features.

A polished synchronized wizard, outbound callback reproduction, real enterprise
identity integration, a scale platform, and live human transfer are out of scope.

## Reference and optional starting point

The pinned [IT help desk password-reset gallery
demo](https://github.com/anujb-msft/voice-ai-template-gallery/tree/ea32df55646762aa8c0d1e76308ef82ba252a5ff/docs/templates/it-helpdesk-password-reset)
is an **outbound, browser-synchronized demo, not production-ready**.
You may reuse or adapt the **full template**, respecting its license and attribution
obligations and owning your submission. Reproducing its full outbound synchronized
wizard is **not required**; only this challenge's inbound secure-link requirements
are assessed. A minimal secure form is sufficient.

## Deliver privately

Send the interviewer a completed submission JSON, reviewer-accessible code pinned
to a commit, and concise setup notes. Public source is optional. **Do not submit
through public GitHub issues** or publish phone endpoints, recordings, credentials,
tenant IDs, passwords, or raw logs.

Use [the submission schema](contracts/submission.schema.json).
[submission.example.json](submission.example.json) is an **intentionally incomplete
template, not a valid live target**. Replace its placeholders and share your
completed copy privately; never dial a placeholder.

| Field | Contract |
| --- | --- |
| `schema_version`, `stage` | `"1.0"`; `initial` or `walkthrough`. |
| `submission_id` | Nonempty identifier, stable across stages. |
| `phone_e164` | Your actual assigned US toll-free number in E.164 form. |
| `reset_base_url` | HTTPS browser entry URL, without credentials, query, or fragment. |
| `repository_url`, `commit_sha` | HTTPS `github.com/owner/repository` URL and full 40-hex commit SHA. |
| `azure_architecture` | Nonempty short stack description, at most 500 characters. |
| `setup_document` | Tracked repo-relative file path at that commit, without traversal. |
| `video_url` | HTTPS private reviewer-accessible walkthrough URL; may be `null` initially, not at walkthrough stage. |
| `notes` | Optional string for limitations and operational context, never secrets. |

No extra JSON fields are accepted. HTTPS and GitHub URL restrictions are encoded
in the schema; reviewers also parse URLs and check access. Do not embed credentials
or secret tokens in URLs. Share any required scoped authentication separately
through a secure channel.

The deployment must correspond to the pinned SHA. Provide a health/version
response or deployment manifest to compare with the code; this is evidence, not
unforgeable proof. Setup notes should cover exact build/test/run commands, isolated
test configuration, browser/backend boundaries, required configuration names
without values, known limitations, and cleanup. No provisioning automation is
required.

## Submission stages

1. **Initial submission:** share `stage: "initial"`, the working endpoint, pinned
   source, and notes. `video_url` may be `null`.
2. **Assessment and feedback:** the interviewer runs automated calls and an
   independent code review, then supplies sanitized findings **before your final
   video**, including observed failures. Waiting for feedback is not effort.
3. **Final walkthrough:** share `stage: "walkthrough"` with the same
   `submission_id` and a non-null private reviewer-accessible `video_url`.
   Explain the submitted artifact, findings, limitations, and proposed corrections.
   A human reviewer then finalizes the scores.

No live interview, live coding, mandatory live code walkthrough, synchronous
follow-up, or timed fix is required. A working fix and retest are **not required**
to earn diagnosis/reasoning credit. If you choose to change code, identify the new
SHA and matching deployment; retain earlier findings and the version they describe.

## Published scoring: 100 points

Each of the four equally weighted categories has **15 points for observed
implementation/code evidence** and **10 points for production-readiness reasoning
in the final video**.
That is **60 implementation + 40 reasoning points**; there is no fifth category.

| Category | Observed implementation/code evidence (0-15) | Video reasoning (0-10) | Total |
| --- | --- | --- | --- |
| `security_and_correctness` | Verification, enforced boundaries, private password handling, authoritative reset/ticket truth. | Explain threats and observed failures; propose precise, credible corrections. | 25 |
| `conversation_quality` | Clear guidance, ambiguity handling, interruption/cancellation behavior, honest outcomes. | Explain caller experience, accessibility, and recovery tradeoffs. | 25 |
| `reliability` | Timeout handling, idempotency, session isolation, restart/reconciliation evidence. | Explain failure diagnosis, scaling/backpressure, and incident recovery. | 25 |
| `engineering_quality` | Understandable architecture, build/test reproducibility, useful tests and operational notes. | Explain design ownership, deployment, cost drivers, and maintainability. | 25 |

Unsafe resets, password disclosure, or false success remain documented evidence
and affect relevant implementation scores; they are **not automatic failures**.
Diagnosis and a proposed fix earn separate reasoning credit without erasing the
observed failure. Never describe an unsafe artifact as production-ready. This
specification sets no hiring cutoff or automatic-reject rule.

## Recorded walkthrough prompts

Target **5-10 minutes as a guideline, not a cutoff**. Screen recording with
narration or captions is enough; no face is required. There is no score for camera
presence, accent, or video polish. An accessible equivalent is available through
the interviewer without penalty, using the same private walkthrough link field.

Show or refer to the actual pinned artifact. Explain the architecture and trust
boundaries; demonstrate the core journey or refer to assessment evidence; diagnose
the sanitized findings and propose fixes. Cover scaling/session isolation, cost
drivers and assumptions, and incident response: detection, containment,
reconciliation, and safe recovery. Distinguish what works now from what you would
change before real production use.
