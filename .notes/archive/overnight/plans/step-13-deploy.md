# Step 13: Deploy and Pin the Version Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A single repeatable command that builds, tests and deploys both apps from a clean commit, plus proof that the deployment matches that commit (`/health` shows the SHA). Also a smoke test, and the second "restart-test" copy.

**Architecture:** `scripts/deploy.ps1` refuses to deploy a dirty working tree. It runs the tests, publishes each app with the git SHA embedded in `AssemblyInformationalVersion` (via `SourceRevisionId`, set in `Directory.Build.props` in step 4), zips the publish output, and runs `az webapp deploy`. `scripts/smoke-test.ps1` checks `/health` on both apps and the basic HTTP behaviour that doesn't need a voice.

**Tech Stack:** PowerShell 5.1+, .NET 10 SDK, Azure CLI.

**When:** First used on Saturday as soon as the mocks exist (deploy early, deploy often). Finalized on Sunday.

---

## File structure

| File | Responsibility |
|---|---|
| `solution/scripts/deploy.ps1` | Clean-tree check → test → publish → zip → deploy → verify the SHA |
| `solution/scripts/smoke-test.ps1` | Post-deploy checks (health, headers, access gate, reset page) |
| `solution/docs/process/engineering.md` | Gets a "Deploy" section (step 14 completes it) |

---

### Task 1: Version embedding check

The .NET SDK (8+) sets `SourceRevisionId` from git automatically (SourceLink) and
appends `+<sha>` to `AssemblyInformationalVersion`. The `/health` endpoints (steps 5
and 6) return the part after `+`.

- [ ] **Step 1: Verify locally**

```powershell
cd solution
dotnet build src/VoiceReset.Agent -c Release
$dll = "src/VoiceReset.Agent/bin/Release/net10.0/VoiceReset.Agent.dll"
[System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $dll)).ProductVersion
git rev-parse HEAD
```

Expected: the ProductVersion ends with `+<the same 40-character SHA>`.
If it doesn't, add this to `Directory.Build.props` and rebuild. Don't work around it
in the script:

```xml
<Target Name="SetSourceRevisionId" BeforeTargets="InitializeSourceControlInformation">
  <Exec Command="git rev-parse HEAD" ConsoleToMSBuild="true" StandardOutputImportance="low">
    <Output TaskParameter="ConsoleOutput" PropertyName="SourceRevisionId" />
  </Exec>
</Target>
```

---

### Task 2: Deploy script

**Files:**
- Create: `solution/scripts/deploy.ps1`

- [ ] **Step 1: Write the script**

```powershell
<#
.SYNOPSIS
  Builds, tests and deploys app-agent and app-mocks from a clean git commit.
.EXAMPLE
  ./scripts/deploy.ps1 -ResourceGroup rg-voicereset -AgentApp app-vr-agent-abc -MocksApp app-vr-mocks-abc
#>
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $AgentApp,
    [Parameter(Mandatory)] [string] $MocksApp,
    [switch] $SkipTests
)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)   # solution/

# 1. Only deploy committed code, so the SHA in /health means something.
$dirty = git status --porcelain
if ($dirty) { throw "Working tree has uncommitted changes. Commit first, then deploy." }
$sha = (git rev-parse HEAD).Trim()
Write-Host "Deploying commit $sha"

# 2. Tests
if (-not $SkipTests) {
    dotnet test VoiceReset.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed. Nothing deployed." }
}

# 3. Publish + zip + deploy each app
$out = Join-Path ([System.IO.Path]::GetTempPath()) "voicereset-$sha"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
$apps = @(
    @{ Project = 'src/VoiceReset.Mocks/VoiceReset.Mocks.csproj'; Name = $MocksApp },
    @{ Project = 'src/VoiceReset.Agent/VoiceReset.Agent.csproj'; Name = $AgentApp }
)
foreach ($app in $apps) {
    $publishDir = Join-Path $out $app.Name
    dotnet publish $app.Project -c Release -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($app.Name)." }
    $zip = "$publishDir.zip"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -Force
    az webapp deploy --resource-group $ResourceGroup --name $app.Name --src-path $zip --type zip --restart true | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Deploy failed for $($app.Name)." }
    Write-Host "Deployed $($app.Name)"
}

# 4. Verify both apps report the same commit (allow time to start)
foreach ($name in @($MocksApp, $AgentApp)) {
    $url = "https://$name.azurewebsites.net/health"
    $reported = $null
    for ($i = 0; $i -lt 30 -and $reported -ne $sha; $i++) {
        try { $reported = (Invoke-RestMethod $url -TimeoutSec 10).commit } catch { Start-Sleep -Seconds 5 }
        if ($reported -ne $sha) { Start-Sleep -Seconds 5 }
    }
    if ($reported -ne $sha) { throw "$name reports commit '$reported', expected $sha." }
    Write-Host "$name is running commit $sha"
}
Remove-Item $out -Recurse -Force
Write-Host "Done."
```

Mocks are deployed first, because the agent depends on them.

- [ ] **Step 2: Dry run on a dirty tree** (create an untracked file, run the script, expect the "uncommitted changes" error, delete the file):

```powershell
New-Item -ItemType File tmp-dirty.txt | Out-Null
./scripts/deploy.ps1 -ResourceGroup rg-voicereset -AgentApp x -MocksApp y
Remove-Item tmp-dirty.txt
```

Expected: `Working tree has uncommitted changes. Commit first, then deploy.`
- [ ] **Step 3: Commit**

```bash
git add solution/scripts/deploy.ps1
git commit -m "build: add deploy script with commit verification"
```

---

### Task 3: Smoke test script

**Files:**
- Create: `solution/scripts/smoke-test.ps1`

- [ ] **Step 1: Write the script**

```powershell
<#
.SYNOPSIS
  Post-deploy checks that need no voice and no secrets.
#>
param(
    [Parameter(Mandatory)] [string] $AgentApp,
    [Parameter(Mandatory)] [string] $MocksApp
)
$ErrorActionPreference = 'Stop'
$agent = "https://$AgentApp.azurewebsites.net"
$mocks = "https://$MocksApp.azurewebsites.net"
$failures = 0
function Check([string] $name, [scriptblock] $test) {
    try { if (& $test) { Write-Host "PASS  $name" } else { Write-Host "FAIL  $name"; $script:failures++ } }
    catch { Write-Host "FAIL  $name ($($_.Exception.Message))"; $script:failures++ }
}

Check 'agent /health ok' { (Invoke-RestMethod "$agent/health").status -eq 'ok' }
Check 'mocks /health ok' { (Invoke-RestMethod "$mocks/health").status -eq 'ok' }
Check 'same commit' { (Invoke-RestMethod "$agent/health").commit -eq (Invoke-RestMethod "$mocks/health").commit }
Check 'policy is public' { (Invoke-RestMethod "$mocks/v1/policy").rules.Count -gt 0 }
Check 'issuer rejects missing credential' {
    try { Invoke-RestMethod "$mocks/v1/recoveries" -Method Post -ContentType 'application/json' -Body '{"username":"x","request_id":"y"}'; $false }
    catch { $_.Exception.Response.StatusCode.value__ -eq 401 }
}
Check 'reset page has strict headers' {
    $r = Invoke-WebRequest "$agent/reset/" -UseBasicParsing
    ($r.Headers['Referrer-Policy'] -eq 'no-referrer') -and ($r.Headers['Content-Security-Policy'] -match "default-src 'none'")
}
Check 'voice page asks for access code' { (Invoke-WebRequest "$agent/" -UseBasicParsing).Content -match 'access-code' }
Check 'voice websocket refused without cookie' {
    try { Invoke-WebRequest "$agent/voice/ws" -UseBasicParsing | Out-Null; $false }
    catch { $_.Exception.Response.StatusCode.value__ -in 400, 401, 403 }
}
Check 'inbox requires login' {
    $r = Invoke-WebRequest "$mocks/inbox" -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue
    $r.StatusCode -in 302, 401 -or $r.Content -match 'login'
}

if ($failures -gt 0) { throw "$failures smoke check(s) failed." }
Write-Host "All smoke checks passed."
```

The element ID `access-code`, the route `/voice/ws` and `/reset/` must match steps
7 and 8. Adjust the strings here if those plans changed them.
- [ ] **Step 2: Run it after the first full deploy**

```powershell
./scripts/smoke-test.ps1 -AgentApp <agent-app> -MocksApp <mocks-app>
```

Expected: `All smoke checks passed.`
- [ ] **Step 3: Commit**

```bash
git add solution/scripts/smoke-test.ps1
git commit -m "build: add post-deploy smoke test"
```

---

### Task 4: Restart-test copy (isolated deployment)

The README: restart testing happens "only in an assessor-controlled isolated
deployment, never by killing your live cloud application". We prove restart safety
on a **second copy**.

- [ ] **Step 1: Deploy the copy** with the same Bicep and a different suffix (step 4 parameters), then deploy the same commit:

```powershell
az deployment group create -g rg-voicereset -f infra/main.bicep -p infra/main.parameters.json -p suffix=<suffix>t
./scripts/deploy.ps1 -ResourceGroup rg-voicereset -AgentApp app-vr-agent-<suffix>t -MocksApp app-vr-mocks-<suffix>t -SkipTests
```

Before deploying, check the parameter names against step 4's `main.bicep` (the copy
shares the App Service plan, so there's no extra plan cost; the Foundry resource can
be shared too, via a `useExistingFoundry` parameter if step 4 defines one).
- [ ] **Step 2: Run the restart scenario** from step 11 (Task "manual restart test") against the copy. Record the evidence in `solution/docs/process/restart-test-evidence.md`.
- [ ] **Step 3: Delete the copy's web apps** when done (keep the evidence):

```powershell
az webapp delete -g rg-voicereset -n app-vr-agent-<suffix>t
az webapp delete -g rg-voicereset -n app-vr-mocks-<suffix>t
```

- [ ] **Step 4: Commit the evidence**

```bash
git add solution/docs/process/restart-test-evidence.md
git commit -m "docs(process): add restart test evidence"
```

---

### Task 5: Freeze rule

- [ ] **Step 1:** Add to `solution/docs/process/engineering.md`, under "Deploy": "After the initial submission, the live deployment stays on the submitted commit until feedback arrives. Fixes go to a branch and are deployed only to the test copy, or after the walkthrough submission with the new SHA recorded."
- [ ] **Step 2: Commit**

```bash
git add solution/docs/process/engineering.md
git commit -m "docs(process): add deployment freeze rule"
```

---

## Self-review

- "Deployment must correspond to the pinned SHA; provide a health/version
  response": Task 1 + the script's verification loop ✔
- "Instructions to run your code there without production access": the
  restart-test copy plus SETUP.md (step 14) ✔
- Dirty-tree protection: Task 2 Step 2 ✔

## Questions for the owner

- **Q-13.1:** Should deploys **always** run the full test suite (slower, about 1–2
  min)? **Default: yes.** `-SkipTests` exists only for the test copy.
