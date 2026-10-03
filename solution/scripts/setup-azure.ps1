<#
.SYNOPSIS
  Creates the Azure resources for the voice password reset agent and configures the web app.
.DESCRIPTION
  Run once after `az login`. Secrets are generated here and stored only in the web app's
  app settings; they are never printed. Running it again keeps existing resources and
  existing secrets (it does not rotate them).
  The signed-in account needs Owner on the subscription (or Contributor plus User Access
  Administrator): creating the resource group, registering resource providers and creating
  role assignments all need subscription scope.
.EXAMPLE
  ./scripts/setup-azure.ps1 -SubscriptionId <id> -Suffix abc123
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SubscriptionId,
    # Makes the globally unique names unique: 3-8 lowercase letters or digits.
    [Parameter(Mandatory)] [ValidatePattern('^[a-z0-9]{3,8}$')] [string] $Suffix,
    [string] $Location = 'swedencentral',
    # Checked against `az webapp list-runtimes --os linux` before the web app is created.
    [string] $Runtime = 'DOTNETCORE:10.0'
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

# Synthetic demo accounts for the mock issuer (same usernames as appsettings.Development.json).
# Only the passwords are generated.
$mockUsers = @(
    @{ Username = 'alex.morgan'; DisplayName = 'Alex Morgan'; RequiresUnlock = 'false' },
    @{ Username = 'jamie.lee'; DisplayName = 'Jamie Lee'; RequiresUnlock = 'false' },
    @{ Username = 'sam.taylor'; DisplayName = 'Sam Taylor'; RequiresUnlock = 'true' }
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

# Nothing printed, so no resource JSON or secret is echoed.
function Invoke-Az { Start-Az @args '-o' 'none' | Out-Null }

# The output as trimmed text (one value).
function Get-AzValue { return ((Start-Az @args '-o' 'tsv') -join '').Trim() }

# The output as an array of lines (zero or more values).
function Get-AzLines { return @(Start-Az @args '-o' 'tsv' | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() }) }

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
    return -join ((Get-RandomBytes 12) | ForEach-Object { $alphabet[$_ % 32] })   # 12 characters: the app requires at least 12
}

function Resolve-RoleName([string[]] $Candidates) {
    # Microsoft renamed "Azure AI User" to "Foundry User": use whichever exists in this tenant.
    foreach ($name in $Candidates) {
        if (@(Get-AzLines role definition list --name $name --query '[].roleName').Count -gt 0) { return $name }
    }
    throw ('None of these roles exists: ' + ($Candidates -join ', ') + '. List the AI roles with: az role definition list --query "[?contains(roleName,''AI'')].roleName"')
}

function Grant-Role([string] $PrincipalId, [string] $Role, [string] $Scope) {
    if (@(Get-AzLines role assignment list --assignee $PrincipalId --role $Role --scope $Scope --query '[].id').Count -gt 0) {
        Write-Host "  ${Role}: already assigned."
        return
    }
    Invoke-Az role assignment create --assignee-object-id $PrincipalId --assignee-principal-type ServicePrincipal --role $Role --scope $Scope
    Write-Host "  ${Role}: assigned."
}

Write-Host "Subscription and resource group ($resourceGroup, $Location)"
Invoke-Az account set --subscription $SubscriptionId
foreach ($namespace in 'Microsoft.Web', 'Microsoft.Storage', 'Microsoft.CognitiveServices', 'Microsoft.OperationalInsights', 'Microsoft.Insights') {
    Invoke-Az provider register --namespace $namespace --wait   # a new subscription may not have them registered
}
Invoke-Az group create --name $resourceGroup --location $Location

Write-Host "Application Insights ($insightsName)"
Invoke-Az monitor log-analytics workspace create --resource-group $resourceGroup --workspace-name $workspaceName --location $Location
$workspaceId = Get-AzValue monitor log-analytics workspace show --resource-group $resourceGroup --workspace-name $workspaceName --query id
Invoke-Az monitor app-insights component create --app $insightsName --resource-group $resourceGroup --location $Location --kind web --application-type web --workspace $workspaceId
$insightsConnection = Get-AzValue monitor app-insights component show --app $insightsName --resource-group $resourceGroup --query connectionString

Write-Host "Storage account ($storageName): containers state and transcripts"
Invoke-Az storage account create --name $storageName --resource-group $resourceGroup --location $Location --sku Standard_LRS --kind StorageV2 --allow-blob-public-access false --min-tls-version TLS1_2
foreach ($container in 'state', 'transcripts') {
    # container-rm is an ARM call: no data-plane role is needed (the app also creates them if missing).
    Invoke-Az storage container-rm create --storage-account $storageName --resource-group $resourceGroup --name $container
}
$storageId = Get-AzValue storage account show --name $storageName --resource-group $resourceGroup --query id

Write-Host "AI Services account for Voice Live ($aiName)"
Invoke-Az cognitiveservices account create --name $aiName --resource-group $resourceGroup --location $Location --kind AIServices --sku S0 --custom-domain $aiName --yes
$aiId = Get-AzValue cognitiveservices account show --name $aiName --resource-group $resourceGroup --query id

Write-Host "App Service plan B1 Linux ($planName) and web app ($appName)"
Invoke-Az appservice plan create --name $planName --resource-group $resourceGroup --location $Location --sku B1 --is-linux
$appExists = @(Get-AzLines webapp list --resource-group $resourceGroup --query "[?name=='$appName'].name").Count -gt 0
if (-not $appExists) {
    try { $runtimes = @(Get-AzLines webapp list-runtimes --os linux | ForEach-Object { $_ -split '\s+' }) }
    catch { $runtimes = @(); Write-Warning 'Could not list the Linux runtimes: the runtime name is not checked.' }
    if ($runtimes.Count -gt 0 -and $runtimes -notcontains $Runtime) {
        throw "Runtime '$Runtime' is not offered for Linux. Available .NET runtimes: $(($runtimes | Where-Object { $_ -match 'DOTNET' }) -join ', '). Pass one with -Runtime."
    }
    Invoke-Az webapp create --name $appName --resource-group $resourceGroup --plan $planName --runtime $Runtime
}
Invoke-Az webapp update --name $appName --resource-group $resourceGroup --https-only true
Invoke-Az webapp config set --name $appName --resource-group $resourceGroup --web-sockets-enabled true --always-on true --http20-enabled true --min-tls-version 1.2 --ftps-state Disabled
$principalId = Get-AzValue webapp identity assign --name $appName --resource-group $resourceGroup --query principalId
$hostName = Get-AzValue webapp show --name $appName --resource-group $resourceGroup --query defaultHostName

Write-Host 'Role assignments for the web app identity (Voice Live and Blob Storage, no keys)'
Grant-Role $principalId 'Cognitive Services User' $aiId
Grant-Role $principalId (Resolve-RoleName 'Azure AI User', 'Foundry User') $aiId
Grant-Role $principalId 'Storage Blob Data Contributor' $storageId

Write-Host 'App settings'
$origin = "https://$hostName"
$settings = [ordered]@{
    'APPLICATIONINSIGHTS_CONNECTION_STRING' = $insightsConnection
    'Access__AllowedOrigin'                 = $origin
    'Issuer__BaseUrl'                       = "$origin/mock/"
    'Mock__ResetBaseUrl'                    = "$origin/reset/"
    'Storage__BlobEndpoint'                 = "https://$storageName.blob.core.windows.net"
    'VoiceLive__Endpoint'                   = "https://$aiName.services.ai.azure.com/"
}
for ($i = 0; $i -lt $mockUsers.Count; $i++) {
    $settings["Mock__Users__${i}__Username"] = $mockUsers[$i].Username
    $settings["Mock__Users__${i}__DisplayName"] = $mockUsers[$i].DisplayName
    $settings["Mock__Users__${i}__RequiresUnlock"] = $mockUsers[$i].RequiresUnlock
}

# Secrets are generated only when missing, so running the script again does not rotate them.
$existing = @(Get-AzLines webapp config appsettings list --name $appName --resource-group $resourceGroup --query '[].name')
$kept = 0
function Set-SecretIfMissing([string] $Name, [scriptblock] $Generate) {
    if ($existing -contains $Name) { $script:kept++; return }
    $settings[$Name] = & $Generate
}
Set-SecretIfMissing 'Access__Code' { New-AccessCode }
# The agent and the mock issuer share one service credential: both settings are written together.
if (($existing -contains 'Mock__ServiceCredential') -and ($existing -contains 'Issuer__ServiceCredential')) { $kept += 2 }
else {
    $serviceCredential = New-Secret 32
    $settings['Mock__ServiceCredential'] = $serviceCredential
    $settings['Issuer__ServiceCredential'] = $serviceCredential
}
for ($i = 0; $i -lt $mockUsers.Count; $i++) {
    Set-SecretIfMissing "Mock__Users__${i}__InboxPassword" { New-Secret 12 }
    Set-SecretIfMissing "Mock__Users__${i}__InitialPassword" { New-Secret 12 }
}
Write-Host "  $($settings.Count) settings to write, $kept existing secrets kept."

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
Write-Host "  Web app          $appName  ($origin)"
Write-Host "  App Service plan $planName"
Write-Host "  AI Services      $aiName"
Write-Host "  Storage account  $storageName"
Write-Host "  Log Analytics    $workspaceName"
Write-Host "  App Insights     $insightsName"
Write-Host ''
Write-Host 'Secrets are only in the web app settings (nothing was printed). To read the access code,'
Write-Host 'the demo usernames and their inbox passwords:'
Write-Host "  az webapp config appsettings list -g $resourceGroup -n $appName --query ""[?name=='Access__Code' || ends_with(name,'__Username') || ends_with(name,'__InboxPassword')].{name:name,value:value}"" -o table"
Write-Host "Pages: $origin/ (agent), $origin/mock/inbox/login (inbox)"
Write-Host "Next: ./scripts/deploy.ps1 -Suffix $Suffix"
