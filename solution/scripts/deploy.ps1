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
    [int] $TimeoutMinutes = 5,
    # Only for a quick live check; run the tests and deploy again afterwards.
    [switch] $SkipTests
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
    $commit = ("$(git rev-parse HEAD)").Trim()
    Assert-Success 'git rev-parse'

    if ($SkipTests) {
        Write-Host '2/6 Skipping the tests (-SkipTests)'
    }
    else {
        Write-Host '2/6 Running the tests'
        dotnet test --project tests/VoiceReset.Tests -c Release
        Assert-Success 'dotnet test'
    }

    Write-Host '3/6 Publishing'
    dotnet publish src/VoiceReset -c Release -o (Join-Path $work 'publish')
    Assert-Success 'dotnet publish'

    Write-Host '4/6 Creating the zip package'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $work 'app.zip'
    # Entry names use '/': Windows PowerShell 5.1 would write '\' with CreateFromDirectory, which breaks on Linux.
    $publishDir = (Resolve-Path (Join-Path $work 'publish')).Path.TrimEnd('\', '/')
    $archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
    try {
        foreach ($file in Get-ChildItem -Path $publishDir -Recurse -File) {
            $relative = $file.FullName.Substring($publishDir.Length + 1).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative)
        }
    }
    finally { $archive.Dispose() }

    Write-Host "5/6 Deploying to $appName"
    Invoke-Az webapp deploy --resource-group $resourceGroup --name $appName --src-path $zip --type zip --clean true -o none | Out-Null
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
