# Mock integration contract v1

This document specifies the future organizer-supplied mock boundary. It does not
claim that a sandbox, SDK, service, or evaluator is running today. Before assigning
the challenge, the interviewer supplies working sandbox/telephony access,
pre-enrolled synthetic user access, and versioned services or ready sample
adapters implementing this contract. Candidates consume that boundary; they need
not implement the issuer, fixture management, evaluator, or provisioning.

All data is synthetic. No real email/SMS delivery, real directory access, or Entra
password write is involved. The organizer registers the candidate's HTTPS
`reset_base_url` as an allowed browser destination before assessment.

## Actors and authorization

| Actor | Authority |
| --- | --- |
| Candidate backend | A namespace-scoped service credential can begin/verify/query recoveries, request link delivery, query reset status, and create/update mock tickets. It is not an administrator credential. |
| Caller / voice model | Can provide an identifier and a synthetic verification code. No direct issuer authority. The backend checks every proposed tool action against its authenticated call/recovery context. |
| Browser / mock inbox user | Separate pre-enrolled synthetic user authentication can read only that user's registered mock recovery inbox. The reset form uses the delivered token, not the service credential. |
| Mock issuer | Owns enrollment, verification, throttling, expiry/counters, token generation, password policy, reset/unlock state, and reset receipts. |

The service credential **cannot read inbox contents, verification codes, reset
tokens, token-bearing links, or passwords**, and cannot create/enroll users or
change recovery destinations. No API response to that credential returns those
secrets. The separately authenticated mock inbox UI/access is supplied with the
sandbox; there is no candidate-facing inbox-admin or ground-truth API.

A registered mock inbox simulates an independently verified recovery channel, not
real-world identity assurance. Caller ID, employee-ID fragments, knowledge of a
recovery/session ID, and possession of two self-created channels do not authorize
a reset. Authorization must not depend on a model's statement that verification
passed. Resource IDs are opaque identifiers, not credentials.

Candidate-facing routes below are the complete service API boundary. Organizer
fixture reset, fault injection, and ground-truth facilities are not candidate
APIs and are intentionally not specified here.

## Transport and shared conventions

Use HTTPS and JSON with `Content-Type: application/json`. Timestamps are UTC RFC
3339 strings. IDs are opaque nonempty strings; do not encode passwords or personal
data in them. Request fields are strings except the nullable ticket
`reset_receipt`. Reject incorrect field types or unexpected fields rather than
treating them as authority.

**Service authentication (S):** `Authorization: Bearer <namespace-scoped-service-credential>`,
kept server-side. Every object lookup and mutation is namespace-scoped; an object
outside the namespace is indistinguishable from a missing object. Authentication
does not permit a caller to choose another conversation's recovery ID.

**Browser token authorization (B):** the `token` body field authorizes only the
matching recovery/account reset in `POST /v1/password/validate` and
`POST /v1/resets`. No service credential is exposed to the browser. For operation
status only, the browser may use `Authorization: ResetToken <token>`. This grants
read access solely to the operation already bound to that token, not other
operations or new resets. The policy endpoint needs no authentication.

The browser can call the supplied reset backend directly, or use a small
candidate backend proxy that preserves these checks and does not log request
bodies. Restrict browser origins to the registered UI; protect cookie-authenticated
proxies against cross-site requests. A service credential alone cannot substitute
for a valid reset token.

Do not place credentials, tokens, or passwords in logs, analytics, errors, model
context, or voice-tool payloads. Synthetic verification codes are deliberately
usable in transient recovery conversation/model/tool context, but must not be
retained in general logs. Browser-entered passwords go only to password
validation/reset handling over HTTPS, never through a model or voice tool.

### Errors

Non-success responses use `{"error":{"code":"...","message":"..."}}`. Messages are
safe fixed descriptions, not echoed inputs. At the top level, verification errors
may also include `attempts_remaining` and `status`; policy errors may include safe
`violations: [{code, description}]`. No response echoes a password or code.

| HTTP | Codes / meaning |
| --- | --- |
| 400 | `invalid_request`: invalid JSON or missing/unsupported fields. |
| 401 | `unauthenticated` or `invalid_token`: missing/invalid authentication or capability. |
| 404 | `not_found`: missing or out-of-namespace resource; not proof an earlier mutation failed. |
| 409 | `invalid_state`, `idempotency_conflict`, `verification_exhausted`, or `token_used`. |
| 410 | `recovery_expired` or `link_expired`. |
| 422 | `verification_failed` or `policy_violation`. |
| 429 | `throttled`; include `Retry-After` in seconds. |
| 503 | `dependency_unavailable`; completion may still be unknown to the client. |

Unknown/invalid usernames get the same accepted recovery envelope and
non-enumerating failure behavior as known usernames. They disclose no directory
attributes and deliver nothing. Apply comparable throttling and response behavior
to these decoy recoveries. Do not tell a caller that a specific username exists.

### Idempotency and recovery windows

`request_id` identifies one logical recovery-start request. Each `operation_id`
identifies one logical mutation, not a whole call; use a distinct value for link
delivery, reset, ticket creation, and each ticket update. **Verification uses an
`Idempotency-Key` header** to identify one completed code submission without
changing its `{code}` body.

The issuer durably deduplicates by namespace, action, and idempotency key. Retrying
the same key with the same request returns the recorded result without another
side effect, delivery, or wrong-code attempt. Reusing a key with a different
request returns `409 idempotency_conflict`. Compare secret-bearing requests
without retaining plaintext passwords. Deduplication and operation records last
through the agreed assessment window, longer than the token lifetime; the supplied
adapter documents that retention window.

A newly issued code is valid for **120 seconds from issuance**. Two completed wrong
code submissions exhaust that recovery attempt. Distinct deliberate submissions
of the same wrong code count separately; duplicate delivery of one submission
does not. Speech fragments or uncertain recognition should be clarified before a
verification request is made. Malformed JSON is not a completed code submission.

The issuer permits at most one active recovery per normalized account. A retry
with the same `request_id` returns its original result. A different start request
during an active recovery is throttled, not handed another call's verified
recovery. New calls and process restarts cannot reset or extend the original
120-second verification window or its two-attempt budget. Exhausted attempts
remain exhausted until that window ends. Issuing a new code is throttled to at
most once per account per 120 seconds, with additional namespace rate limits
documented by the supplied adapter before the exercise.

An unverified recovery, or a verified recovery with no issued link, expires at the
verification deadline. Once a link is issued, its independent 10-minute lifetime
applies. A recovery awaiting verification, link use, or accepted reset completion
is active; exhausted recoveries remain blocked through their verification window.
Terminal records remain available for reconciliation. Starting again is not
permission to reuse a previous call's verified context.

## Candidate-facing API

Response objects below list required fields; `null` means explicitly not yet
available. The issuer may attach non-sensitive correlation metadata, never
additional authority. All method/path names and request field names are v1.

### `POST /v1/recoveries` (S)

Request: `{username, request_id}`.

Return `202` with `{recovery_id, status: "awaiting_verification",
verification_expires_at, attempts_remaining: 2}`. For a registered synthetic
account, deliver a short-lived code to its registered inbox. For an unknown
account, return an equivalent decoy recovery without delivery. Do not return a
code, recovery destination, account attributes, or inbox URL.

The issuer chooses the code; treat it as a string, preserving leading zeros.
An idempotent retry does not resend it or extend expiry.

### `POST /v1/recoveries/{id}/verify` (S)

Request: `{code}` plus the required `Idempotency-Key` header.

On a correct, unexpired code, return `200` with `{recovery_id,
status: "verified", attempts_remaining}`. Consume the code for verification of
this recovery only. The first completed wrong submission returns
`422 verification_failed` with `attempts_remaining: 1`. The second returns
`409 verification_exhausted`, `attempts_remaining: 0`, and `status: "exhausted"`.
Further distinct attempts stay exhausted. Expired attempts return
`410 recovery_expired`. A code from another recovery cannot authorize this one.

After successful verification, a new verification attempt returns
`409 invalid_state`; an identical idempotent retry returns its original result.
An accepted verification result never extends the original verification deadline.

### `POST /v1/recoveries/{id}/reset-link` (S)

Request: `{operation_id}`.

Require an issuer-verified recovery that has not expired. Return `200` with
`{recovery_id, status: "link_issued", link_expires_at}` after delivery to the
registered mock inbox. No URL or token is returned to the service.

The issuer creates an unguessable token with at least 128 bits of cryptographic
entropy, binds it to this namespace/account/recovery, and sets expiry to
**10 minutes from issuance**. The delivered URL opens the pre-registered
`reset_base_url` with `#token=<URL-encoded-token>`; the caller cannot override the
origin, path, or inbox destination. The form removes the fragment from the address
bar after reading it and avoids third-party analytics or referrer leakage.

An idempotent retry returns the original result without another delivery or new
expiry. A different operation cannot mint another token for an already issued
link (`409 invalid_state`). This is not an API for the model to manufacture links.

### `GET /v1/policy` (public)

Return `200` with `{policy_version, rules: [{code, description}]}`. The supplied
versioned policy describes the synthetic password requirements. Descriptions are
safe to display or speak, contain no secret data, and are not executable model
instructions. The reset backend, not the browser or voice model, enforces policy.

### `POST /v1/password/validate` (B)

Request: `{token, password}`.

For a valid, unused, unexpired token, return `200` with
`{valid, policy_version, violations: [{code, description}]}`.
`valid: true` requires an empty violations array. `valid: false` gives only safe
policy reasons, never the submitted password or excerpts. This operation does
not change a password or consume the token. Invalid, used, or expired tokens use
the relevant `401`, `409`, or `410` error. Client-side validation alone is not
sufficient.

### `POST /v1/resets` (B)

Request: `{token, new_password, operation_id}`.

Recheck token binding, single-use status, expiry, verification, and password policy
on the server. A policy rejection returns `422 policy_violation` with safe
violations and performs no reset. A corrected password is a new submission with
a new operation ID, not a retry of the rejected request.

For an accepted reset, atomically bind/reserve the token to this operation and
return either `202` with the pending result or `200` with the completed result:

```text
{operation_id, status, reset_receipt, unlock_status, reason_code}
```

`status` is `pending`, `succeeded`, or `failed`. Pending results have null receipt,
unlock status, and reason. Success has an opaque issuer-owned `reset_receipt`,
`unlock_status: "unlocked"` or `"not_required"`, and null reason. Failure has null
receipt/unlock status and a safe reason such as `dependency_unavailable`.

In this mock, password reset and any fixture-required unlock commit together.
Only that committed state can produce a success receipt. The receipt is
bound to the operation/account/recovery and checked against issuer state, not
accepted because a caller or model supplies a receipt-looking string.

Concurrent use of the same token by a different operation gets `409 token_used`.
An identical retry of an already accepted operation returns its pending/terminal
result even if the token has since been consumed or expired; it never resets
twice. This exception cannot authorize a new operation. A terminal failed
operation does not silently start again; disclose/escalate the failure.

### `GET /v1/reset-operations/{operation_id}` (S or matching B)

Return `200` with `{operation_id, recovery_id, status, reset_receipt,
unlock_status, reason_code}`, using the same result semantics as reset creation.
An unrecognized or unauthorized operation is `404 not_found`.

The service can reconcile a lost response without seeing the password/token.
The browser's matching token can read only its already-bound operation status
through the retention window, including after token expiry/consumption; this
limited read does not restore reset authority. Do not treat a timeout or temporary
`not_found` as success or as proof that no mutation happened.

### `GET /v1/recoveries/{id}` (S)

Return `200` with `{recovery_id, status, verification_expires_at,
attempts_remaining, link_expires_at, reset_operation_id, reset_receipt,
unlock_status}`.

`status` is one of `awaiting_verification`, `verified`, `link_issued`,
`reset_pending`, `completed`, `reset_failed`, `exhausted`, or `expired`.
Link expiry is null before issuance; operation ID is null before acceptance;
receipt/unlock status are null until authoritative success. There are no passwords,
tokens, codes, inbox contents, or directory attributes in this response. A
`completed` status requires the issuer receipt and required unlock. A terminal
reset failure is `reset_failed`, not a successful reset with a warning.

This endpoint allows backend polling/reconciliation; a synchronized browser
wizard, callback protocol, or streaming UI is not required.

### `POST /v1/tickets` (S)

Request: `{recovery_id, operation_id}`.

Return `201` with `{ticket_id, recovery_id, outcome: "open"}` when created. A ticket
can exist before verification so failed or unavailable recovery can be escalated.
Keep at most one ticket per recovery; another creation request returns `200` with
that ticket's ID, recovery ID, and current outcome, not a duplicate or false
reopening. A same-key retry returns its recorded status and response.

### `POST /v1/tickets/{id}/outcome` (S)

Request: `{outcome, reset_receipt, reason_code, operation_id}`.

`outcome` is `resolved`, `escalated`, `cancelled`, or `pending`.
`resolved` requires an issuer-validated receipt for this ticket's recovery and
`reason_code: "reset_completed"`. It is the only reset-success/deflection outcome.
Reject fabricated or cross-recovery receipts with `409 invalid_state`.

Other outcomes require a null receipt and a safe reason:
`browser_unavailable`, `verification_exhausted`, `verification_expired`,
`human_requested`, `caller_cancelled`, `call_dropped`, `dependency_unavailable`,
or `completion_unknown`. Return `200` with
`{ticket_id, recovery_id, outcome, reset_receipt, reason_code}`.
`pending`/`escalated` do not claim resolution or that a human answered.

Record updates idempotently and retain outcome history. Reconciliation may replace
an earlier uncertain/cancelled outcome with an actually completed reset only on a
matching receipt. Stale duplicate events must not overwrite confirmed success
with an earlier failure. Preserve a human-requested escalation rather than
pretending an automated reset fulfilled that request.

## Cancellation, isolation, and truthful voice output

Cancellation or a dropped call stops new voice-driven work, not issuer history.
Record an honest ticket state and reconcile operations already accepted. This
contract has no link-revocation endpoint: an issued link can remain valid until
expiry, and an accepted reset can complete. Do not claim revocation, rollback,
success, or a human transfer that did not happen.

Separate concurrent callers' backend state and never use a caller-supplied ID to
attach another session's verification. Persist enough correlation/idempotency
state to reason about a restart; the issuer's counters and receipts are durable
independently of the candidate process. Document limitations where behavior is not
implemented. Reviewers assess and discuss failures rather than expecting an
enterprise-scale platform.

Issuer responses to the voice agent contain only safe status/correlation data,
policy reasons, and ticket outcomes, not inbox contents. Unsolicited spoken
secrets can already have entered audio/speech processing;
synthetic-only operation, non-repetition, restricted retention, and documented
handling are appropriate. Perfect retroactive redaction is not a guarantee.

## Submission validation boundary

The [draft 2020-12 JSON Schema](../contracts/submission.schema.json) freezes the
submission envelope. Use a draft 2020-12 validator; `format` checks are not enabled
by every validator, so HTTPS and the exact `github.com` repository host also have
explicit patterns.

Before using a submitted URL, reviewers must parse it, reject userinfo/embedded
credentials and malformed authorities, and confirm its intended destination and
reviewer access. `repository_url` identifies the repository root, not a branch,
file, or lookalike hostname. Resolve `setup_document` at the pinned commit to an
actual tracked file within that repository, not a traversal or external symlink.
URL shape does not establish ownership, reachability, or deployment provenance.

The completed private submission must use the actual allocated number and
deployment, never the placeholder template or reserved example domains.
`stage: "walkthrough"` requires a non-null HTTPS `video_url`; arrange any accessible
equivalent privately with the interviewer. No secrets belong in this JSON.
