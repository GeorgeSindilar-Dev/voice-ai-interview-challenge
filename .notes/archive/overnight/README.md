# Archive: overnight production-grade plans (2026-10-03)

**Not the plan we follow.** These plans and research reports describe a much more
complete, production-style system (about 30,000 lines of plans). We decided it was
over-engineered for the challenge.

The plan we actually follow is [../../plan.md](../../plan.md).

What's still useful here:
- `research/`: background for the interviewer meeting ("what I'd do for
  production"): guardrails (52 attack scenarios, controls C1–C16), .NET practices,
  transcript masking, and verified Voice Live / ACS SDK facts.
- `research/sdk-reference.md`: verified class names and code for `Azure.AI.VoiceLive`
  1.2.0 and ACS Call Automation. **Use it while implementing.**
- `plans/step-07-voice-agent.md`: verified Voice Live code (session settings, event
  loop, barge-in, browser AudioWorklet page). A good source to copy small pieces from.
