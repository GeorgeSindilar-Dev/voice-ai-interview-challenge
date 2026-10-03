# Step 16: Recorded Walkthrough Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A 5–10 minute screen recording that covers every README walkthrough prompt. It earns the 40 "reasoning" points: 10 per category.

**Architecture:** This happens **after** the interviewer's sanitized findings arrive. Claude prepares the outline, the diagnosis of each finding, and the talking points. The owner records and narrates. The final `submission.json` (stage `walkthrough`) adds the private video link.

**Tech Stack:** Screen recorder (Clipchamp, built into Windows 11, or OBS Studio), Markdown, the deployed app.

**When:** After feedback (not part of the Monday deadline).

---

### Task 1: Diagnose the findings

**Files:**
- Create: `solution/docs/submission/findings.md`

- [ ] **Step 1:** For each sanitized finding, add a row:

| # | Finding (as received) | Category | Root cause (file:line) | Proposed fix | Fixed? (new SHA) |
|---|---|---|---|---|---|

Root causes come from reading the code at the **assessed SHA** (`git show <sha>:<path>`), not the latest code.
- [ ] **Step 2:** For each finding, Claude writes a precise, credible fix proposal: what changes, which file, which test proves it. Fixes are optional (the README says a working fix isn't required). If one is made, record the new SHA and redeploy, and **keep the original finding and its SHA** in the table.
- [ ] **Step 3: Commit**

```bash
git add solution/docs/submission/findings.md
git commit -m "docs(submission): diagnose assessment findings"
```

---

### Task 2: Video outline

**Files:**
- Create: `solution/docs/submission/video-outline.md`

- [ ] **Step 1: Write the outline with timings** (total about 9 minutes):

| Time | Section | Show on screen | README prompt / score category |
|---|---|---|---|
| 0:00–0:30 | What this is | The voice page | Intro |
| 0:30–2:00 | Architecture and trust boundaries | `architecture/overview.md` + `trust-boundaries.md` diagrams | "Explain the architecture and trust boundaries"; engineering_quality |
| 2:00–3:30 | Core journey demo | Live (or recorded) browser call: code → link → form → confirmation; the ticket outcome | "demonstrate the core journey"; conversation_quality |
| 3:30–5:30 | Findings: diagnosis and fixes | `findings.md`, the code at the assessed SHA | "diagnose the sanitized findings and propose fixes"; security_and_correctness |
| 5:30–6:30 | Scaling and session isolation | Session store, per-call Voice Live connection, App Service scale-out, Voice Live TPM quota | "scaling/session isolation"; reliability |
| 6:30–7:15 | Cost drivers and assumptions | `infrastructure/cost.md` | "cost drivers and assumptions"; engineering_quality |
| 7:15–8:15 | Incident response | `process/runbook-incidents.md`: detection (KQL), containment (rotate access code, disable phone), reconciliation, safe recovery | "incident response"; reliability |
| 8:15–9:00 | Now vs. production | `known-limitations.md` + "what I'd change before real production" | "Distinguish what works now…" |

- [ ] **Step 2: The "before real production" list** (adjust to reality):
  - real identity proofing; the mocks only simulate inbox access
  - a link revocation endpoint
  - multi-instance scale with session affinity, or a shared WebSocket layer
  - private networking (VNet, private endpoints)
  - load and soak tests; Voice Live quota increase
  - stronger spoken-secret handling (for example a DTMF code entry option)
  - real human transfer
  - an accessibility review with users
  - alerting rules (not only queries)
  - penetration testing

  **Never call anything "production-ready"** (the README says so explicitly).
- [ ] **Step 3: Talking points per category**, two or three sentences each, which the owner reads in their own words. Each category's 10 reasoning points need: threats and failures explained (security); caller experience, accessibility and recovery trade-offs (conversation); failure diagnosis, scaling/backpressure and incident recovery (reliability); design ownership, deployment, cost and maintainability (engineering).
- [ ] **Step 4: Commit**

```bash
git add solution/docs/submission/video-outline.md
git commit -m "docs(submission): add walkthrough video outline"
```

---

### Task 3: Record and submit

- [ ] **Step 1:** The owner records: screen plus narration (captions are optional; no face needed). Target 5–10 minutes. Check that **no secrets are visible**: no access code on screen, no Key Vault values, no tenant IDs, no phone number. Use the browser at 125% zoom so the text is readable.
- [ ] **Step 2:** Upload privately with a reviewer-accessible link (for example a OneDrive share link limited to "people with the link", or an unlisted YouTube video). It must be HTTPS. (Q-16.1)
- [ ] **Step 3:** Update `submission.json`: `stage` → `walkthrough`, `video_url` → the link, the same `submission_id`; `commit_sha` stays the assessed SHA unless a fix was deployed (then the new SHA, with the findings table showing both). Validate:

```powershell
npx --yes ajv-cli@5 validate --spec=draft2020 -c ajv-formats -s contracts/submission.schema.json -d solution/submission.json --strict=false
```

Expected: `solution/submission.json valid`.
- [ ] **Step 4:** The owner sends it privately.

---

## Questions for the owner

- **Q-16.1: Video hosting.** Default: OneDrive "anyone with the link" (you have a
  Microsoft account). Alternative: unlisted YouTube.
- **Q-16.2: Narration.** Default: you narrate in English from the talking points.
  Alternative: captions only (Clipchamp can auto-caption).
