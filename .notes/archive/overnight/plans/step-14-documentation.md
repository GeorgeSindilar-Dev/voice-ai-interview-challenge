# Step 14: Documentation Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the four documentation areas (architecture, infrastructure, process, submission) complete, consistent with the code at the final commit, and useful to a reviewer who has never seen the project.

**Architecture:** Most docs are written during steps 3–13 (CLAUDE.md rule 5: a step isn't done until its docs are). This step **fills the remaining gaps**, writes `SETUP.md` from the commands actually run, and does a consistency pass against the code and the requirements checklist.

**Tech Stack:** Markdown, Mermaid, KQL (Application Insights queries).

**When:** Monday morning. About 2 hours.

---

## File structure (final state)

```
solution/docs/
├── architecture/      (step 3 + updates from steps 5–10)
│   ├── README.md, overview.md, trust-boundaries.md, sequence-flows.md
│   ├── recovery-workflow.md, voice-agent.md, reset-form.md, mock-services.md
│   ├── telephony.md, transcripts.md
│   └── decisions/ADR-00x-*.md
├── infrastructure/
│   ├── README.md            index
│   ├── resources.md         every Azure resource: purpose, SKU, why
│   ├── identity-and-access.md  managed identities, role assignments, secrets
│   ├── configuration.md     every app setting NAME (no values), meaning, secret yes/no
│   ├── monitoring.md        App Insights, useful KQL queries, what is never logged
│   └── cost.md              cost drivers, estimate, budget, how to stop spending
├── process/
│   ├── README.md
│   ├── reset-journey.md     business process: journey, escalations, ticket outcomes
│   ├── runbook-incidents.md detection, containment, reconciliation, safe recovery
│   └── engineering.md       how we planned, built, tested, deployed; branching; TDD
└── submission/
    ├── SETUP.md             the setup_document (reproduce on another system)
    ├── requirements-checklist.md
    ├── known-limitations.md
    ├── submission-notes.md  draft text for the JSON "notes" field
    └── video-outline.md     (step 16)
```

---

### Task 1: Infrastructure docs

**Files:**
- Create: `solution/docs/infrastructure/README.md`, `resources.md`, `identity-and-access.md`, `configuration.md`, `monitoring.md`, `cost.md`

- [ ] **Step 1: `resources.md`.** One table built from `infra/main.bicep` (step 4). For each resource: name pattern, type/SKU, purpose, and why this SKU. Use the names from [00-overview.md §4](00-overview.md). Read the real values with:

```powershell
az resource list --resource-group rg-voicereset --query "[].{name:name, type:type, sku:sku.name, location:location}" -o table
```

Copy only names, types and SKUs. **Never copy IDs, tenant IDs or connection strings.**

- [ ] **Step 2: `identity-and-access.md`.** List each managed identity and its role assignments, read with:

```powershell
$agentId = az webapp identity show -g rg-voicereset -n <agent-app-name> --query principalId -o tsv
az role assignment list --assignee $agentId --all --query "[].{role:roleDefinitionName, scope:scope}" -o table
```

In the doc, write scopes as resource **names** (e.g. "storage account stvragent…"), not full IDs. Add a paragraph: "app-agent has no role on the mocks storage account. That is how the trust boundary is enforced in Azure."

- [ ] **Step 3: `configuration.md`.** Copy the two configuration tables from [00-overview.md §4](00-overview.md), updated with any keys added during implementation. Find them all with:

```powershell
Select-String -Path solution/src/**/*.cs -Pattern 'class \w+Options' -List
```

Mark every secret as "Key Vault reference".

- [ ] **Step 4: `monitoring.md`.** Explain what's sent to Application Insights (requests without bodies, dependencies, exceptions, the custom events from step 7/11) and what is **never** sent (bodies, codes, tokens, passwords, transcripts). Include these KQL queries, verified to run in the portal:

```kusto
// Failed requests in the last hour
requests | where timestamp > ago(1h) and success == false
| summarize count() by name, resultCode | order by count_ desc

// Issuer dependency health (latency and failures)
dependencies | where timestamp > ago(1h) and target contains "app-vr-mocks"
| summarize calls=count(), failures=countif(success == false), p95=percentile(duration, 95) by name

// Sessions ended by reason (custom event from the agent)
customEvents | where name == "SessionEnded" and timestamp > ago(1d)
| summarize count() by tostring(customDimensions.EndReason)

// Tickets left non-final for more than 15 minutes (custom event from the reconciler)
customEvents | where name == "ReconciliationPending" and timestamp > ago(1h)
| summarize max(timestamp) by tostring(customDimensions.SessionId)
```

The event names must match the code. Check with `Select-String -Path solution/src/**/*.cs -Pattern 'TrackEvent|"SessionEnded"|"ReconciliationPending"'` and adjust the queries to match the code.

- [ ] **Step 5: `cost.md`.** The cost drivers, with a 7-day assessment estimate:

| Driver | Unit | Estimate |
|---|---|---|
| App Service plan B1 Linux | hourly | about $13/month → about $3/week |
| Voice Live (model per Q-4) | tokens (audio in/out) | dominant cost; estimate per 5-minute call from the pricing page at the time of writing |
| Storage (2 accounts) | GB + transactions | cents |
| Application Insights | GB ingested | cents (bodies not logged) |
| Key Vault | operations | cents |
| ACS (optional) | number/month + per-minute | trial number free; a paid toll-free number is a few $ |

Explain how to stop spending: `az group delete -n rg-voicereset` (the cleanup in SETUP.md), and that the budget alert from step 4 warns at 50% and 80%.

- [ ] **Step 6: `README.md` index** linking the five docs.
- [ ] **Step 7: Commit**

```bash
git add solution/docs/infrastructure
git commit -m "docs(infrastructure): add resources, identity, configuration, monitoring and cost"
```

---

### Task 2: Process docs

**Files:**
- Create: `solution/docs/process/README.md`, `reset-journey.md`, `runbook-incidents.md`, `engineering.md`

- [ ] **Step 1: `reset-journey.md`.** Base it on [03-challenge-walkthrough.md](../planning/03-challenge-walkthrough.md), updated to the **actual** behaviour: the real tool names, real limits, and real phrases from `Prompts/system-prompt.md`. Include the ticket outcome table from [00-overview.md §7](00-overview.md) as implemented.

- [ ] **Step 2: `runbook-incidents.md`.** Four sections, each with concrete commands:

  - **Detection:** the KQL queries from monitoring.md; signs like a spike in `dependency_unavailable`, sessions stuck in `LinkSent`, Voice Live errors (quota 429).
  - **Containment:**
    - Stop new voice sessions by rotating the access code:

      ```powershell
      az keyvault secret set --vault-name <kv> --name AccessCode --value (New-Guid).Guid
      az webapp restart -g rg-voicereset -n <agent-app>
      ```

    - Disable the phone: set `Telephony__Enabled=false`.
    - Stop app-agent entirely: `az webapp stop`.
    - **What containment does NOT do:** issued links stay valid until expiry, because there's no revoke endpoint. Say so honestly.
  - **Reconciliation:** how the reconciler works; how to see non-final sessions (query the `sessions` table with `az storage entity query --account-name <st> --table-name sessions --auth-mode login --filter "IsFinal eq false"`, with the property name adjusted to match the code). A ticket is only ever moved to `resolved` with a receipt.
  - **Safe recovery:** restart order (mocks first, then agent); verify `/health` shows the expected commit; run the smoke test from step 13; check that the reconciler has resolved the open sessions.

- [ ] **Step 3: `engineering.md`.** How the project was built:
  - planning (link `docs/planning/` and `docs/plans/`)
  - TDD and the test projects
  - the branch and commit conventions (no AI attribution)
  - how to run tests (`dotnet test`)
  - how to deploy (`scripts/deploy.ps1`)
  - how versions are pinned (commit SHA in `/health`)
  - how to add a tool to the agent (the files to touch)

- [ ] **Step 4: `README.md` index.**
- [ ] **Step 5: Commit**

```bash
git add solution/docs/process
git commit -m "docs(process): add reset journey, incident runbook and engineering process"
```

---

### Task 3: SETUP.md (the `setup_document`)

**Files:**
- Create: `solution/docs/submission/SETUP.md`

The guide that lets someone else reproduce the project on **their** system. Every
command must be one we actually ran in step 4/13. Copy them from the terminal
history, never from memory.

- [ ] **Step 1: Write SETUP.md with exactly these sections:**

````markdown
# Setup: reproduce this project

Pinned commit: see `/health` on the deployment, or the submission's `commit_sha`.

## 1. Prerequisites
- .NET SDK 10.0.x (`dotnet --version`)
- Azure CLI 2.x (`az version`) with Bicep (`az bicep version`)
- Git, PowerShell 5.1+ (Windows) or PowerShell 7 (any OS)
- An Azure subscription where you are Owner (role assignments are created)
- Optional (phone): an ACS phone number your subscription is eligible for

## 2. Get the code
```powershell
git clone https://github.com/<owner>/<repo>.git
cd <repo>
git checkout <commit_sha>
cd solution
```

## 3. Build and test
```powershell
dotnet build VoiceReset.slnx -c Release
dotnet test VoiceReset.slnx -c Release
```
Expected: build succeeded, 0 warnings; all tests pass (E2E tests are skipped unless E2E_* variables are set).

## 4. Create the Azure resources
(commands from step 4: az login, az group create, az deployment group create with infra/main.bicep and parameters)

## 5. Configure
(table of setting names → where they come from; the Key Vault secrets to create, with the generation commands; no values)

## 6. Deploy
```powershell
./scripts/deploy.ps1 -ResourceGroup rg-voicereset -AgentApp <agent-app> -MocksApp <mocks-app>
```

## 7. Seed the synthetic users
(how users are defined in Mocks__Users__N__* settings and how to read their inbox passwords from Key Vault)

## 8. Verify
```powershell
Invoke-RestMethod https://<agent-app>.azurewebsites.net/health
Invoke-RestMethod https://<mocks-app>.azurewebsites.net/health
```
Expected: `status: ok` and `commit` equal to the pinned SHA on both.

## 9. Use it
- Voice page: https://<agent-app>.azurewebsites.net/ → enter the access code → Start
- Inbox: https://<mocks-app>.azurewebsites.net/inbox → log in as a synthetic user
- Reset form: opened from the link in the inbox
- Phone (if configured): call the number
- Recommended: desktop Chrome or Edge (mobile browsers pause background tabs)

## 10. What runs where (browser/backend boundaries)
(the table from architecture/trust-boundaries.md, shortened)

## 11. Isolated test configuration (restart testing)
Deploy a second copy with a different suffix (same commands, `-p suffix=<other>`); restart it with `az webapp restart` during a session; never restart the live deployment.

## 12. Known limitations
See [known-limitations.md](known-limitations.md).

## 13. Cleanup
```powershell
az group delete --name rg-voicereset --yes --no-wait
```
Also delete the soft-deleted Key Vault if you want the name back: `az keyvault purge --name <kv>`.
````

- [ ] **Step 2: Fill in sections 4, 5 and 7** from the commands actually executed (step 4 and step 13 logs). Replace every `<…>` that is a value specific to *our* deployment with a placeholder description, because SETUP.md must work for someone else. Keep `<owner>/<repo>` as real values (the repo URL is public anyway).
- [ ] **Step 3: Dry-run check.** In a fresh PowerShell window, run sections 2, 3 and 8 exactly as written and confirm the expected output. Fix any drift.
- [ ] **Step 4: Commit**

```bash
git add solution/docs/submission/SETUP.md
git commit -m "docs(submission): add setup guide"
```

---

### Task 4: Known limitations and submission notes

**Files:**
- Create: `solution/docs/submission/known-limitations.md`
- Create: `solution/docs/submission/submission-notes.md`

- [ ] **Step 1: `known-limitations.md`.** Start from this list. Keep only what is true at the final commit, and add anything new from testing:
  - Synthetic data only; mocks simulate recovery-channel access, not real identity assurance.
  - No link revocation (none in the contract): a sent link stays valid until expiry, even after cancel.
  - Spoken secrets can reach Voice Live audio processing before masking; masking is pattern-based and can miss unusual phrasings.
  - Mobile browsers may stop the microphone in background tabs; desktop recommended.
  - Single App Service instance (B1): no horizontal scale; restart drops live calls (state survives, and reconciliation completes tickets).
  - Phone channel status (built and tested / built but not provisioned / not built). Pick the true one.
  - English only (if Q-5 is the default).
  - Voice Live quota (tokens per minute) limits parallel calls; on throttling the agent says it can't help right now and escalates.
  - No human transfer; escalation means an honest ticket.
  - Fault injection is limited to delayed reset completion.
- [ ] **Step 2: `submission-notes.md`.** The draft text for the `notes` JSON field: at most about 1,500 characters, plain text, no secrets. It covers: how to reach the agent (voice page + access code shared separately; phone status), browser recommendation, the assumptions made without being able to ask (mocks built by the candidate per the contract; one namespace; region), and a pointer to SETUP.md and known-limitations.md.
- [ ] **Step 3: Commit**

```bash
git add solution/docs/submission/known-limitations.md solution/docs/submission/submission-notes.md
git commit -m "docs(submission): add known limitations and notes draft"
```

---

### Task 5: Consistency pass

- [ ] **Step 1: Docs vs code.** For each architecture doc, check the names it mentions exist:

```powershell
Select-String -Path solution/docs/architecture/*.md -Pattern '`[A-Z][A-Za-z]+(Async)?`' -AllMatches |
  ForEach-Object { $_.Matches.Value } | Sort-Object -Unique
```

For each class or method name in the output, confirm it exists with `Select-String -Path solution/src/**/*.cs -Pattern '<name>'`. Fix the docs where the code differs.
- [ ] **Step 2: No secrets or forbidden data in docs.** Run:

```powershell
Select-String -Path solution/docs/**/*.md, CLAUDE.md -Pattern '\+1(800|833|844|855|866|877|888)\d{7}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|AccountKey=|sig=|Bearer [A-Za-z0-9]'
```

Expected: no matches (a GUID match must be checked by hand: test GUIDs in examples are fine, tenant/subscription IDs are not).
- [ ] **Step 3: Checklist.** Walk through [requirements-checklist.md](../submission/requirements-checklist.md). Every unticked item is either ticked now (with evidence) or listed in known-limitations.md.
- [ ] **Step 4: Commit**

```bash
git add -A solution/docs
git commit -m "docs: consistency pass before submission"
```

---

## Self-review

- The README's setup requirements (build/test/run commands, isolated test configuration, browser/backend boundaries, configuration names without values, known limitations, cleanup) → SETUP.md sections 3, 11, 10, 5, 12, 13 ✔
- The video prompts (scaling, cost, incident response) have source docs: cost.md, runbook-incidents.md, known-limitations.md ✔
- Never documents unbuilt behaviour: Task 5 Step 3 ✔

## Questions for the owner

- **Q-14.1:** SETUP.md assumes the reader uses PowerShell. Default: yes (works on
  Windows, macOS and Linux with PowerShell 7). Alternative: add bash equivalents
  (about 30 min more).
