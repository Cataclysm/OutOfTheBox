#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Upgrades an existing OutOfTheBox install in place, per tasks.md 17.5.

.DESCRIPTION
    Stops the service, verifies it actually reached Stopped, replaces the install directory's
    contents with a newer published build, starts the service, and polls /version to confirm the
    new build is actually running. The data directory (config + SQLite file) is never touched -
    that is what makes "upgrade = replace the exe" true without any config re-entry or manual data
    migration step (per design.md's Packaging decision).

    Run this against the output of:
        dotnet publish src/OutOfTheBox.Host -p:PublishProfile=win-x64 -o <SourcePath>

.PARAMETER SourcePath
    Path to the newly published win-x64 self-contained output directory to upgrade to.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourcePath,

    [string]$InstallDirectory = 'C:\Program Files\OutOfTheBox',
    [string]$ServiceName = 'OutOfTheBox',
    [int]$Port = 5443,
    [int]$StopTimeoutSeconds = 60,
    [int]$VersionPollTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

Write-Host "== OutOfTheBox upgrade =="

if (-not (Test-Path (Join-Path $SourcePath 'OutOfTheBox.Host.exe'))) {
    throw "OutOfTheBox.Host.exe not found under '$SourcePath' - publish first: dotnet publish src/OutOfTheBox.Host -p:PublishProfile=win-x64 -o <SourcePath>"
}

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    throw "Service '$ServiceName' is not installed - run install.ps1 first, not upgrade.ps1."
}

# Record the currently-reported version before stopping, purely so the post-upgrade check below
# has something concrete to contrast against beyond "it responded."
$oldVersion = $null
try {
    $oldVersion = (Invoke-RestMethod -Uri "https://localhost:$Port/version" -SkipCertificateCheck -TimeoutSec 5).version
}
catch {
    Write-Warning "Could not read the pre-upgrade version (service may already be stopped) - continuing."
}

Write-Host "Stopping service '$ServiceName'..."
Stop-Service -Name $ServiceName -Force

$stopWaited = 0
while ((Get-Service -Name $ServiceName).Status -ne 'Stopped') {
    if ($stopWaited -ge $StopTimeoutSeconds) {
        throw "Service '$ServiceName' did not reach the Stopped state within $StopTimeoutSeconds seconds - aborting upgrade without touching the install directory."
    }
    Start-Sleep -Seconds 1
    $stopWaited++
}
Write-Host "Service stopped."

# Data directory is never referenced here at all - only the install directory's contents are
# replaced, wholesale, which is what makes upgrade safe to re-run and leaves config/history
# untouched regardless of what changed between builds.
Write-Host "Replacing install directory contents at '$InstallDirectory'..."
Get-ChildItem -Path $InstallDirectory -Force | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $SourcePath '*') -Destination $InstallDirectory -Recurse -Force

Write-Host "Starting service..."
Start-Service -Name $ServiceName

$service = Get-Service -Name $ServiceName
if ($service.Status -ne 'Running') {
    throw "Service did not reach the Running state after upgrade (status: $($service.Status)) - check the Windows Event Log (Application) for OutOfTheBox startup errors. The install directory now contains the new build; re-run upgrade.ps1 once the issue is fixed, or roll back manually by re-running upgrade.ps1 with the previous build's SourcePath."
}

Write-Host "Polling /version to confirm the new build is actually running..."
$deadline = (Get-Date).AddSeconds($VersionPollTimeoutSeconds)
$newVersion = $null
while ((Get-Date) -lt $deadline) {
    try {
        $newVersion = (Invoke-RestMethod -Uri "https://localhost:$Port/version" -SkipCertificateCheck -TimeoutSec 5).version
        break
    }
    catch {
        Start-Sleep -Seconds 1
    }
}

if (-not $newVersion) {
    throw "Service is Running but /version did not respond within $VersionPollTimeoutSeconds seconds - the new build may have failed to bind Kestrel or complete startup migrations. Check the Windows Event Log."
}

Write-Host "== Upgrade complete. Version: $oldVersion -> $newVersion =="
