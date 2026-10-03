# Build tasks (controller notes; pasted into subagent prompts)

## SHARED CONTEXT (paste into every implementer/reviewer prompt)

Repository worktree root: `C:\work - local\voice-ai-interview-challenge\.claude\worktrees\challenge-planning`
(Windows; use the Bash tool with POSIX paths like `/c/work - local/...` or PowerShell). Branch: `worktree-challenge-planning`.
All code lives in `solution/`. Do NOT modify root files (README.md, docs/, contracts/, submission.example.json, .gitignore).
Never touch `.notes/` except reading the reference files named in a task.

We are building an inbound voice-assisted password reset agent (a coding challenge spec in `README.md` and
`docs/mock-contract.md` at the repo root — read the relevant parts). LEAN scope: do exactly what the task says,
simple, readable, not production-grade. Project rules are in `CLAUDE.md` at the root — read it first and follow it
(simplicity first; idiomatic modern C#; Minimal APIs grouped by feature; options validated at startup; results not
exceptions for expected failures; TimeProvider where time matters; no mocking libraries; secrets never in logs).

Tech: .NET 10 (SDK 10.0.302 installed; there is NO newer SDK, don't try to install anything), C# 14, ASP.NET Core,
xUnit v3 on Microsoft Testing Platform. Run tests with:
`dotnet test --project tests/VoiceReset.Tests` (from `solution/`), filter with `--filter-class "<Namespace.Class>"`.
Build: `dotnet build VoiceReset.slnx -c Release` must have 0 warnings (warnings are errors in Release).
No Azure resources exist yet: nothing may require Azure to build or to pass tests.

Solution layout (fixed names):
```
solution/
  global.json  Directory.Build.props  Directory.Packages.props  .editorconfig  VoiceReset.slnx
  src/VoiceReset/            (one ASP.NET Core web app; namespaces VoiceReset.<Feature>)
    Program.cs
    Health/  Storage/  Mock/  Recovery/  Voice/  Access/  Transcripts/
    Pages/Mock/Inbox/ (Razor Pages: /mock/inbox/login, /mock/inbox)
    wwwroot/ (index.html agent page, js/, css/, reset/ form, favicon)
  tests/VoiceReset.Tests/    (xUnit v3; folders mirror src features)
```

Configuration keys (appsettings.json holds non-secret defaults; secrets come from App Service settings in Azure;
`appsettings.Development.json` holds obviously-fake local dev values so the app runs locally):
- `Mock:ServiceCredential`, `Mock:ResetBaseUrl` (e.g. https://host/reset/),
  `Mock:Users:N:Username|DisplayName|InboxPassword|InitialPassword|RequiresUnlock`
- `Issuer:BaseUrl` (e.g. https://host/mock/), `Issuer:ServiceCredential` (same value as Mock:ServiceCredential)
- `Storage:BlobEndpoint` (empty → in-memory store)
- `VoiceLive:Endpoint`, `VoiceLive:Model` (default gpt-4.1-mini), `VoiceLive:Voice` (default en-US-Ava:DragonHDLatestNeural)
- `Access:Code`
- `Limits:MaxCallSeconds` (default 600)
- env `APPLICATIONINSIGHTS_CONNECTION_STRING` (when set → Azure Monitor OpenTelemetry)

Commit rules: commit with plain messages like `feat(mock): add recovery endpoints`. NO Co-Authored-By,
NO "Generated with", no mention of AI/Claude anywhere (commits or code comments). Stage only files you changed
under `solution/` (never `.notes/`).

## TASKS
T1 skeleton+health · T2 json store · T3 mock issuer (recoveries/verify/link/status) · T4 mock reset+policy+tickets ·
T5 mock inbox pages · T6 recovery workflow+issuer client+sessions · T7 startup check · T8 tools/prompt/dispatcher ·
T9 access gate · T10 voice session+browser channel+ws endpoint · T11 agent page (html/js) · T12 reset form ·
T13 transcripts · T14 logging/AppInsights/security headers · T15 azure scripts · T16 docs
