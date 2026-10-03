# Step 15: Initial Submission Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a valid, private `submission.json` (stage `initial`) that points to a pinned commit matching the live deployment, and hand it to the owner to send.

**Architecture:** The code is merged and pushed to the public fork. The deployment is verified against the commit SHA. `submission.json` is created in `solution/` (gitignored), validated against `contracts/submission.schema.json` with a draft 2020-12 validator, and sent privately by the owner. Claude never sends messages on the owner's behalf.

**Tech Stack:** git, GitHub CLI, Node.js (`npx ajv-cli`), PowerShell.

**When:** Monday, after step 14. About 30 minutes.

---

### Task 1: Freeze and pin the commit

- [ ] **Step 1: Confirm a clean tree and passing tests**

```powershell
cd solution
git status --short
dotnet test VoiceReset.slnx -c Release
```

Expected: no output from `git status --short`; all tests pass.

- [ ] **Step 2: Merge the work branch into `main` and push** (Q-15.1)

`main` is checked out in the main folder, not in the worktree, so run this **in the
main checkout** (`C:\work - local\voice-ai-interview-challenge`):

```powershell
git merge --no-ff worktree-challenge-planning -m "Merge voice reset solution"
git push origin main
$sha = git rev-parse HEAD
$sha
```

Expected: a 40-character SHA. **The repository is public:** before pushing, re-run the secret scan from step 14 Task 5 Step 2.

- [ ] **Step 3: Deploy exactly that SHA** (if anything changed since the last deploy)

```powershell
./scripts/deploy.ps1 -ResourceGroup rg-voicereset -AgentApp <agent-app> -MocksApp <mocks-app>
```

- [ ] **Step 4: Verify the deployment matches**

```powershell
(Invoke-RestMethod https://<agent-app>.azurewebsites.net/health).commit
(Invoke-RestMethod https://<mocks-app>.azurewebsites.net/health).commit
```

Expected: both equal `$sha`.

---

### Task 2: Create and validate `submission.json`

**Files:**
- Create: `solution/submission.json` (gitignored, so it is **never committed**)

- [ ] **Step 1: Check it is ignored**

```powershell
git check-ignore -v solution/submission.json
```

Expected: `solution/.gitignore:2:submission.json  solution/submission.json`.

- [ ] **Step 2: Write the file** (values filled in by the owner where marked)

```json
{
  "schema_version": "1.0",
  "stage": "initial",
  "submission_id": "<owner chooses, e.g. gs-voicereset-2026-10>",
  "phone_e164": "<the ACS toll-free number in E.164, see Q-15.2>",
  "reset_base_url": "https://<agent-app>.azurewebsites.net/reset/",
  "repository_url": "https://github.com/GeorgeSindilar-Dev/voice-ai-interview-challenge",
  "commit_sha": "<$sha>",
  "azure_architecture": "<≤500 chars, see Step 3>",
  "setup_document": "solution/docs/submission/SETUP.md",
  "video_url": null,
  "notes": "<from docs/submission/submission-notes.md>"
}
```

- [ ] **Step 3: The `azure_architecture` text** (adjust to what was actually built; must be ≤500 characters):

```text
Azure Voice Live (Microsoft Foundry, model mode, <model>) with tool calls executed by an ASP.NET Core (.NET 10) backend on App Service Linux that enforces a recovery state machine. Channels: browser voice page over WebSocket and ACS inbound toll-free calls with bidirectional media streaming. Mock issuer/ticket/inbox as a separate App Service app. State in Azure Table Storage, transcripts (masked) in Blob Storage, secrets in Key Vault, managed identities, Application Insights.
```

Check the length:

```powershell
(Get-Content solution/submission.json -Raw | ConvertFrom-Json).azure_architecture.Length
```

Expected: ≤ 500.

- [ ] **Step 4: Validate against the schema** (draft 2020-12, with formats)

```powershell
npx --yes ajv-cli@5 validate --spec=draft2020 -c ajv-formats -s contracts/submission.schema.json -d solution/submission.json --strict=false
```

Run it from the repository root. Expected: `solution/submission.json valid`.

- [ ] **Step 5: Manual checks the schema can't do** (from the mock contract's "Submission validation boundary"):
  - `reset_base_url` opens the reset form, and without a token shows "Open the link from your recovery inbox".
  - `repository_url` opens, with no credentials in it.
  - `setup_document` exists at the pinned commit: `git cat-file -e $sha:solution/docs/submission/SETUP.md`. No output means it exists.
  - No placeholders left: `Select-String -Path solution/submission.json -Pattern '<|REPLACE|example'`. Expected: no matches.

---

### Task 3: Hand-off to the owner

- [ ] **Step 1: Prepare the private bundle** that the owner sends through the interviewer's private channel (never GitHub issues):
  1. `submission.json`
  2. Separately (a different message, if possible): the voice page access code and the synthetic users' inbox logins. The owner reads them from Key Vault:

     ```powershell
     az keyvault secret show --vault-name <kv> --name AccessCode --query value -o tsv
     az keyvault secret show --vault-name <kv> --name MockUser0InboxPassword --query value -o tsv
     ```

     Secret names must match step 4. Claude does **not** print these values into the chat or into files.
- [ ] **Step 2: The owner sends it.** Claude does not send messages on the owner's behalf.
- [ ] **Step 3: Record the submission.** Add to `solution/docs/submission/submission-notes.md` a line "Initial submission: commit `<sha>`, date", then commit and push.

```bash
git add solution/docs/submission/submission-notes.md
git commit -m "docs(submission): record initial submission commit"
git push origin main
```

- [ ] **Step 4: Freeze the deployment.** No redeploys until feedback arrives. The assessed deployment must stay on `$sha`. Keep it running, and keep the budget alert on.

---

## Self-review

- Every field in the README contract table is covered in Task 2 ✔
- "No extra JSON fields" is enforced by the schema's `additionalProperties: false`, validated in Step 4 ✔
- Never publish phone endpoints/credentials: the file is gitignored (Step 1); secrets go separately (Task 3) ✔

## Questions for the owner

- **Q-15.1: Branching.** Default: merge `worktree-challenge-planning` into `main`
  and pin a commit on `main`. Alternative: keep a separate `submission` branch (the
  SHA is valid either way, but `main` is what reviewers open first).
- **Q-15.2: `phone_e164` if there is no phone number.** The schema *requires* a
  US toll-free number, and the README says never to use placeholders. Default: if we
  have the ACS trial number, use it (it must match the toll-free pattern; trial
  numbers are toll-free). If we have **no** number, the JSON can't be valid. We'd
  submit it with the field explained in `notes`, mention it in the message, and
  state that the browser voice page is the agreed alternative. Please confirm.
- **Q-15.3: `submission_id`.** Pick an identifier you'll reuse for the walkthrough
  stage. Default: `gs-voicereset-2026-10`.
- **Q-15.4: How do you send it to the interviewer** (email or a portal)? This only
  affects the hand-off step, which you do yourself.
