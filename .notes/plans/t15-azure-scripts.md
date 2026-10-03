# T15 Azure Setup and Deploy Scripts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two PowerShell scripts: `setup-azure.ps1` creates the Azure resources and configures the web app (secrets included), `deploy.ps1` ships the committed code and proves the right commit is live. Written now, not run (no Azure subscription access yet).

**Architecture:** Plain `az` CLI calls, no Bicep. Names are fixed except for a `Suffix` that makes the global ones unique. Secrets are generated in the script and written to the app settings through a temporary JSON file, so they are never on a command line and never printed. The web app uses a system-assigned managed identity with three role assignments; there are no keys in settings.

**Tech Stack:** PowerShell 5.1 or 7 (the scripts avoid 7-only syntax), Azure CLI, `dotnet` 10 SDK, git.

**Files:**
- Create: `solution/scripts/setup-azure.ps1`
- Create: `solution/scripts/deploy.ps1`

**Names created** (with `Suffix` = `abc123` as an example):

| Resource | Name |
|---|---|
| Resource group | `rg-voicereset` |
| Storage account (containers `state`, `transcripts`) | `stvoiceresetabc123` |
| AI Services (Foundry) account, custom subdomain | `ai-voicereset-abc123` |
| Log Analytics workspace / Application Insights | `log-voicereset-abc123` / `appi-voicereset-abc123` |
| App Service plan (B1, Linux) / web app (`DOTNETCORE:10.0`) | `plan-voicereset-abc123` / `app-voicereset-abc123` |

**App settings written** (nested keys use `__`; matches the options classes in `00-contracts.md`): `APPLICATIONINSIGHTS_CONNECTION_STRING`, `Issuer__BaseUrl`, `Issuer__ServiceCredential`, `Mock__ResetBaseUrl`, `Mock__ServiceCredential`, `Mock__Users__<i>__{Username,DisplayName,RequiresUnlock,InboxPassword,InitialPassword}`, `Storage__BlobEndpoint`, `VoiceLive__Endpoint`, `Access__Code`. `VoiceLive__Model`, `VoiceLive__Voice` and `Limits__MaxCallSeconds` keep their defaults from `appsettings.json`.

---

### Task 1: `setup-azure.ps1`

- [ ] **Step 1: Create `solution/scripts/setup-azure.ps1`** with exactly this content

```powershell
<#
.SYNOPSIS
  Creates the Azure resources for the voice password reset agent and configures the web app.
.DESCRIPTION
  Run once after `az login`. Secrets are generated here and stored only in the web app's
  app settings; they are never printed. Running it again keeps existing resources and
  existing secrets (it does not rotate them).
.EXAMPLE
  ./scripts/setup-azure.ps1 -SubscriptionId <id> -Suffix abc123
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SubscriptionId,
    # Makes the globally unique names unique: 3-8 lowercase letters or digits.
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9]{3,8}$')] [string] $Suffix,
    [string] $Location = 'swedencentral'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:AZURE_EXTENSION_USE_DYNAMIC_INSTALL = 'yes_without_prompt'   # installs the application-insights extension

$resourceGroup = 'rg-voicereset'
$storageName = "stvoicereset$Suffix"
$aiName = "ai-voicereset-$Suffix"          # also the custom subdomain of the endpoint
$workspaceName = "log-voicereset-$Suffix"
$insightsName = "appi-voicereset-$Suffix"
$planName = "plan-voicereset-$Suffix"
$appName = "app-voicereset-$Suffix"

# Synthetic demo accounts for the mock issuer. Only the passwords are generated.
$mockUsers = @(
    @{ Username = 'alex.demo'; DisplayName = 'Alex Demo'; RequiresUnlock = 'false' },
    @{ Username = 'blake.demo'; DisplayName = 'Blake Demo'; RequiresUnlock = 'true' },
    @{ Username = 'casey.demo'; DisplayName = 'Casey Demo'; RequiresUnlock = 'false' }
)

function Start-Az {
    # Runs az; throws on a non-zero exit code. Never put secrets in the arguments.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # Windows PowerShell treats native stderr as an error otherwise
    try {
        $output = & az @args --only-show-errors
        if ($LASTEXITCODE -ne 0) { throw "az $($args[0]) $($args[1]) failed with exit code $LASTEXITCODE." }
        return $output
    }
    finally { $ErrorActionPreference = $previous }
}

function Invoke-Az { Start-Az @args '-o' 'none' | Out-Null }

function Get-AzValue { return ((Start-Az @args '-o' 'tsv') -join '').Trim() }

function Get-RandomBytes([int] $Count) {
    $buffer = New-Object byte[] $Count
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return , $buffer
}

function New-Secret([int] $Bytes = 24) {
    return [Convert]::ToBase64String((Get-RandomBytes $Bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function New-AccessCode {
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'   # 32 characters, no look-alikes (so a byte maps evenly)
    return -join ((Get-RandomBytes 12) | ForEach-Object { $alphabet[$_ % 32] })
}

Write-Host "Subscription and resource group ($resourceGroup, $Location)"
Invoke-Az account set --subscription $SubscriptionId
Invoke-Az group create --name $resourceGroup --location $Location

Write-Host "Application Insights ($insightsName)"
Invoke-Az monitor log-analytics workspace create --resource-group $resourceGroup --workspace-name $workspaceName --location $Location
$workspaceId = Get-AzValue monitor log-analytics workspace show --resource-group $resourceGroup --workspace-name $workspaceName --query id
Invoke-Az monitor app-insights component create --app $insightsName --resource-group $resourceGroup --location $Location --kind web --application-type web --workspace $workspaceId
$insightsConnection = Get-AzValue monitor app-insights component show --app $insightsName --resource-group $resourceGroup --query connectionString

Write-Host "Storage account ($storageName): containers state and transcripts"
Invoke-Az storage account create --name $storageName --resource-group $resourceGroup --location $Location --sku Standard_LRS --kind StorageV2 --allow-blob-public-access false --min-tls-version TLS1_2
foreach ($container in 'state', 'transcripts') {
    Invoke-Az storage container-rm create --storage-account $storageName --resource-group $resourceGroup --name $container
}
$storageId = Get-AzValue storage account show --name $storageName --resource-group $resourceGroup --query id

Write-Host "AI Services account for Voice Live ($aiName)"
Invoke-Az cognitiveservices account create --name $aiName --resource-group $resourceGroup --location $Location --kind AIServices --sku S0 --custom-domain $aiName --yes
$aiId = Get-AzValue cognitiveservices account show --name $aiName --resource-group $resourceGroup --query id

Write-Host "App Service plan B1 Linux ($planName) and web app ($appName)"
Invoke-Az appservice plan create --name $planName --resource-group $resourceGroup --location $Location --sku B1 --is-linux
Invoke-Az webapp create --name $appName --resource-group $resourceGroup --plan $planName --runtime 'DOTNETCORE:10.0'
Invoke-Az webapp update --name $appName --resource-group $resourceGroup --https-only true
Invoke-Az webapp config set --name $appName --resource-group $resourceGroup --web-sockets-enabled true --always-on true --http20-enabled true --min-tls-version 1.2 --ftps-state Disabled
$principalId = Get-AzValue webapp identity assign --name $appName --resource-group $resourceGroup --query principalId
$hostName = Get-AzValue webapp show --name $appName --resource-group $resourceGroup --query defaultHostName

Write-Host 'Role assignments for the web app identity (Voice Live and Blob Storage, no keys)'
$roles = @(
    @{ Role = 'Cognitive Services User'; Scope = $aiId },
    @{ Role = 'Azure AI User'; Scope = $aiId },
    @{ Role = 'Storage Blob Data Contributor'; Scope = $storageId }
)
foreach ($item in $roles) {
    Invoke-Az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal --role $item.Role --scope $item.Scope
}

Write-Host 'App settings'
$settings = [ordered]@{
    'APPLICATIONINSIGHTS_CONNECTION_STRING' = $insightsConnection
    'Issuer__BaseUrl'                       = "https://$hostName/mock/"
    'Mock__ResetBaseUrl'                    = "https://$hostName/reset/"
    'Storage__BlobEndpoint'                 = "https://$storageName.blob.core.windows.net"
    'VoiceLive__Endpoint'                   = "https://$aiName.services.ai.azure.com/"
}
$secretsExist = [bool] (Get-AzValue webapp config appsettings list --name $appName --resource-group $resourceGroup --query "[?name=='Access__Code'].name")
if ($secretsExist) {
    Write-Host '  Secrets already exist: kept as they are.'
}
else {
    $serviceCredential = New-Secret 32
    $settings['Access__Code'] = New-AccessCode
    $settings['Mock__ServiceCredential'] = $serviceCredential
    $settings['Issuer__ServiceCredential'] = $serviceCredential
    for ($i = 0; $i -lt $mockUsers.Count; $i++) {
        $prefix = "Mock__Users__${i}__"
        $settings["${prefix}Username"] = $mockUsers[$i].Username
        $settings["${prefix}DisplayName"] = $mockUsers[$i].DisplayName
        $settings["${prefix}RequiresUnlock"] = $mockUsers[$i].RequiresUnlock
        $settings["${prefix}InboxPassword"] = New-Secret 12
        $settings["${prefix}InitialPassword"] = New-Secret 12
    }
}

# The settings go in through a temporary JSON file so no secret is on a command line or in the output.
$payload = foreach ($name in $settings.Keys) { [ordered]@{ name = $name; value = $settings[$name]; slotSetting = $false } }
$file = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName())
try {
    [IO.File]::WriteAllText($file, (ConvertTo-Json -InputObject @($payload) -Depth 3))
    Invoke-Az webapp config appsettings set --name $appName --resource-group $resourceGroup --settings "@$file"
}
finally { Remove-Item -Path $file -Force -ErrorAction SilentlyContinue }

Write-Host ''
Write-Host 'Done. Resources:'
Write-Host "  Resource group   $resourceGroup"
Write-Host "  Web app          $appName  (https://$hostName)"
Write-Host "  App Service plan $planName"
Write-Host "  AI Services      $aiName"
Write-Host "  Storage account  $storageName"
Write-Host "  Log Analytics    $workspaceName"
Write-Host "  App Insights     $insightsName"
Write-Host 'Secrets are only in the web app settings. To read the access code:'
Write-Host "  az webapp config appsettings list -g $resourceGroup -n $appName --query ""[?name=='Access__Code'].value"" -o tsv"
Write-Host "Next: ./scripts/deploy.ps1 -Suffix $Suffix"
```

How it works, briefly:
- `Start-Az` runs `az` and throws on a non-zero exit code. `Invoke-Az` adds `-o none` (nothing printed, so no resource JSON or secret is echoed); `Get-AzValue` adds `-o tsv` and returns the trimmed text.
- Storage containers are created with `container-rm` (an ARM call), so the person running the script does not need a data-plane role.
- The role assignments use `--assignee-principal-type ServicePrincipal` so they succeed right after the identity is created.
- Re-running keeps existing resources (`az ... create` is idempotent) and keeps existing secrets: if `Access__Code` is already an app setting, no secret is regenerated.

- [ ] **Step 2: Parse check (no Azure needed)**

Run (from `solution/`):
```powershell
$errors = $null; $tokens = $null
[void][System.Management.Automation.Language.Parser]::ParseFile("$PWD\scripts\setup-azure.ps1", [ref]$tokens, [ref]$errors)
$errors.Count
```
Expected: `0`. Any message means a syntax error: fix it.

- [ ] **Step 3: Dry run with a fake `az` (optional, cheap)**

In a PowerShell session define `function global:az { $global:LASTEXITCODE = 0; if (($args -join ' ') -match '-o tsv') { 'x' } }` and run `./scripts/setup-azure.ps1 -SubscriptionId s -Suffix abc123`. Expected: the summary is printed, no error, no secret value in the output. Then `Remove-Item function:az`.

- [ ] **Step 4: Commit**

```bash
git add solution/scripts/setup-azure.ps1
git commit -m "build: add Azure setup script"
```

---

### Task 2: `deploy.ps1`

- [ ] **Step 1: Create `solution/scripts/deploy.ps1`** with exactly this content

```powershell
<#
.SYNOPSIS
  Tests, publishes and deploys the committed code to the web app, then waits until
  /health reports the commit that was deployed.
.EXAMPLE
  ./scripts/deploy.ps1 -Suffix abc123
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9]{3,8}$')] [string] $Suffix,
    [int] $TimeoutMinutes = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$resourceGroup = 'rg-voicereset'
$appName = "app-voicereset-$Suffix"
$solution = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('voicereset-' + [guid]::NewGuid().ToString('N'))

function Assert-Success([string] $What) {
    if ($LASTEXITCODE -ne 0) { throw "$What failed with exit code $LASTEXITCODE." }
}

function Invoke-Az {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # Windows PowerShell treats native stderr as an error otherwise
    try {
        $output = & az @args --only-show-errors
        Assert-Success "az $($args[0]) $($args[1])"
        return $output
    }
    finally { $ErrorActionPreference = $previous }
}

Push-Location $solution
try {
    Write-Host '1/6 Checking that the working tree is clean'
    $changes = git status --porcelain -- .
    Assert-Success 'git status'
    if ($changes) { throw 'solution/ has uncommitted changes. Commit them first: /health must show the deployed commit.' }
    $commit = (git rev-parse HEAD).Trim()
    Assert-Success 'git rev-parse'

    Write-Host '2/6 Running the tests'
    dotnet test --project tests/VoiceReset.Tests -c Release
    Assert-Success 'dotnet test'

    Write-Host '3/6 Publishing'
    dotnet publish src/VoiceReset -c Release -o (Join-Path $work 'publish')
    Assert-Success 'dotnet publish'

    Write-Host '4/6 Creating the zip package'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $work 'app.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $work 'publish'), $zip)

    Write-Host "5/6 Deploying to $appName"
    Invoke-Az webapp deploy --resource-group $resourceGroup --name $appName --src-path $zip --type zip --clean true | Out-Null
    $hostName = ((Invoke-Az webapp show --name $appName --resource-group $resourceGroup --query defaultHostName -o tsv) -join '').Trim()

    Write-Host "6/6 Waiting for /health to report commit $($commit.Substring(0, 9))"
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $live = $null
    while ((Get-Date) -lt $deadline) {
        try {
            $live = (Invoke-RestMethod -Uri "https://$hostName/health" -TimeoutSec 10).commit
            if ($live -eq $commit) { break }
        }
        catch { $live = $null }   # the app is restarting
        Start-Sleep -Seconds 5
    }
    if ($live -ne $commit) { throw "/health did not report commit $commit within $TimeoutMinutes minutes (last seen: $live)." }

    Write-Host "Deployed commit $($commit.Substring(0, 9)): https://$hostName/"
}
finally {
    Pop-Location
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
}
```

Notes:
- The commit shown by `/health` comes from the SDK (`InformationalVersion` = `1.0.0+<full SHA>`), so a publish from a clean tree at `HEAD` reports exactly `git rev-parse HEAD`. That is why the clean-tree check comes first.
- `ZipFile.CreateFromDirectory` writes forward-slash entry names (Windows PowerShell's `Compress-Archive` does not, which breaks Linux App Service).
- `--clean true` removes files of the previous deployment.

- [ ] **Step 2: Parse check**

Same command as in Task 1, with `deploy.ps1`. Expected: `0`.

- [ ] **Step 3: Dry run with fakes (optional)**

Define global functions `git`, `dotnet`, `az` and `Invoke-RestMethod` that set `$global:LASTEXITCODE = 0` and return plausible values (`git rev-parse` a fixed SHA; `dotnet publish` creates the `-o` directory with one file; `az ... tsv` a host name; `Invoke-RestMethod` an object with `commit` = that SHA). Run `./scripts/deploy.ps1 -Suffix abc123`. Expected: the six step lines and `Deployed commit ...`. Remove the fake functions afterwards.

- [ ] **Step 4: Commit**

```bash
git add solution/scripts/deploy.ps1
git commit -m "build: add deploy script"
```

---

## Questions

1. Demo accounts: the script creates three synthetic users (`alex.demo`, `blake.demo` with unlock required, `casey.demo`). T3/T4 should seed the same usernames in `appsettings.Development.json` (fake passwords) so local and Azure behave alike. Is this list right, and how do the testers learn the inbox passwords? Today: from the app settings (portal, or `az webapp config appsettings list`).
2. Role names: the plan assigns `Azure AI User` as asked; the research note says Microsoft may now call it `Foundry User`. If `az role assignment create` rejects the name, use the new name (or role id `53ca6127-db72-4b80-b1b0-d745d6d5456d`, unverified). Also unverified: the `az webapp create --runtime` value `DOTNETCORE:10.0` (check `az webapp list-runtimes --os-type linux`).
3. The AI Services key stays enabled (local auth is not disabled); the app only uses the managed identity. Acceptable for this scope?
4. Soft-deleted AI Services accounts keep their name for days after `az group delete`; a re-run with the same `Suffix` needs a purge. Mentioned in the T16 cleanup section.

## Additions to contracts

- Scripts live in `solution/scripts/`. Fixed resource group `rg-voicereset`; web app `app-voicereset-<Suffix>`; `deploy.ps1 -Suffix <Suffix>`.
- Production settings supply the mock users as `Mock__Users__<i>__...`; `MockOptions.Users` must therefore bind from configuration (no hard-coded users in code).
- `VoiceLive__Endpoint` is the HTTPS base URI `https://<ai-name>.services.ai.azure.com/` (what `VoiceLiveClient` expects).
