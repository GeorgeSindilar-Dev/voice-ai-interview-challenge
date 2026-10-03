# Start here (morning review)

Good morning. Here is what was done overnight, and what I need from you.

## 1. Read these, in this order (about 45 minutes)

1. **[00-questions-for-you.md](00-questions-for-you.md)**: your decisions. The 🔴 ones
   block the start. **Bold** ones need a real look. The rest have safe defaults: just
   write "OK".
2. **[00-overview.md](00-overview.md)**: the architecture and every shared name, in
   one place.
3. Skim the step plans you're curious about (table below). They're long because
   they contain the complete code and tests; you don't need to read them line by line.

## 2. The three most important overnight findings

1. **The phone number is (almost certainly) not possible anymore.** Microsoft
   announced the retirement of Azure Communication Services in September 2026.
   Tenants creating their first ACS resource since then can't get phone numbers,
   trial or paid. So the **browser voice page is the channel**, and the phone is
   documented as designed-but-not-deployed, with the reason. The interviewer already
   told you the phone isn't mandatory. See Q-6.
2. **No API keys are needed at all.** Voice Live can be locked to Microsoft Entra ID.
   Your `az login` plus the apps' managed identities do everything. Secrets (the
   access code, the inbox passwords) are generated straight into Key Vault and
   never pass through the chat. See Q-0 and Q-4.2.
3. **The design is verified, not just written.** The research agents compiled the
   Voice Live / ACS / transcript code against the real SDK packages. The step 7
   (voice agent) and step 10 (transcripts) plans were built task by task in a
   scratch folder: 0 warnings, all tests passing. The voice page was even opened
   in a real browser against a fake Voice Live (console clean), which found and
   fixed one real bug.

## 3. What exists now

| Folder / file | What |
|---|---|
| [../research/](../research/) | 4 research reports: guardrails (52 attack scenarios, controls C1–C16), .NET best practices (+ ready config files), transcripts (masking design, 37 tested rules), SDK reference (verified Voice Live / ACS APIs) |
| [../../../CLAUDE.md](../../../CLAUDE.md) | Updated with the research rules (rules 1, 3, 4) |
| [../../TODO.md](../../TODO.md) | All 3 research items ticked |
| This folder | Overview, questions, and one plan per roadmap step |

| Step | Plan | Size | Code verified overnight? |
|---|---|---|---|
| 3 Architecture docs | [step-03](step-03-architecture-docs.md) | small | n/a (docs) |
| 4 Azure + solution skeleton | [step-04](step-04-azure-setup.md) | medium (complete Bicep) | config files built by the .NET research |
| 5 Mock services | [step-05](step-05-mock-services.md) | large (23 tasks) | no, first build at execution |
| 6 Backend core | [step-06](step-06-backend-core.md) | large (23 tasks) | no, first build at execution |
| 7 Voice agent + voice page | [step-07](step-07-voice-agent.md) | large (17 tasks) | **yes** (against step 6 stand-ins) |
| 8 Reset form | [step-08](step-08-reset-form.md) | large (10 tasks) | no |
| 9 Phone (conditional) | [step-09](step-09-phone.md) | small | n/a |
| 10 Transcripts | [step-10](step-10-transcripts.md) | medium (11 tasks) | **yes** (59 tests) |
| 11 Reliability | [step-11](step-11-reliability.md) | medium | no |
| 12 Testing (guardrail conversations, browser, manual) | [step-12](step-12-testing.md) | medium | no |
| 13 Deploy + version | [step-13](step-13-deploy.md) | small | no |
| 14 Documentation | [step-14](step-14-documentation.md) | medium | n/a |
| 15 Submit | [step-15](step-15-submit.md) | small | n/a |
| 16 Video | [step-16](step-16-video.md) | small | n/a |

## 4. Proposed order for Saturday

1. You: answer the 🔴 questions, create the subscription, run `az login`.
2. Me: install the Azure CLI + SDK update (with your OK), then step 4 (Azure +
   skeleton). While Azure deploys, step 5 (mocks) starts.
3. Steps 5 and 6 in parallel where possible, then deploy the mocks early.
4. Step 7 → **first real conversation** in the browser (Saturday evening, if all goes well).
5. Sunday: steps 8, 10, 11, 12, 13. Monday: 14, 15.

The cut line if time runs short is in [00-overview.md §10](00-overview.md).

## 5. Nothing is committed

Everything is uncommitted in the worktree (`worktree-challenge-planning`), so you can
review it first. When you're happy, I'll make the first commit with only your
name.
