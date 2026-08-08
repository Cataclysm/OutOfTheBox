#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs OutOfTheBox as a Windows Service, per tasks.md 17.4 / design.md's Packaging decisions.

.DESCRIPTION
    Creates a dedicated, least-privileged local service account; creates the install directory
    (disposable - upgrade.ps1 replaces its contents wholesale) and the data directory (config +
    SQLite file, never touched by upgrade.ps1); copies the published output; writes the
    production appsettings.json into the data directory; registers the Windows Service with SCM
    crash-recovery configured; opens the required firewall rule; and starts the service.

    Run this against the output of:
        dotnet publish src/OutOfTheBox.Host -p:PublishProfile=win-x64 -o <SourcePath>

.PARAMETER SourcePath
    Path to an already-published win-x64 self-contained output directory (see .DESCRIPTION).

.PARAMETER RepoRootDirectory
    The directory repos live under - what OutOfTheBox:RootDirectory confines every command,
    clone, and artifact transfer to. Created if it doesn't already exist.

.PARAMETER AllowedRemoteAddresses
    IP addresses (or ranges) allowed to reach the service's port - the sbx sandbox's IP(s) for the
    command API, and the operator's own IP(s) for the dashboard (they share one port; see
    INSTALL.md's Network & transport section for why this can't be split by port instead).

.PARAMETER CertificatePath / CertificatePassword
    An existing .pfx to bind Kestrel to. If omitted, a self-signed certificate is generated via
    `dotnet dev-certs` - acceptable for v1 per design.md (see INSTALL.md for how the sbx-side
    client must then pin/trust it, since it isn't from a publicly trusted CA).

.PARAMETER BearerToken
    The shared credential callers must present. If omitted, a cryptographically random 256-bit
    token is generated and printed once - copy it before closing the console, it is not
    recoverable from the written config file's plaintext form alone without re-reading the file.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourcePath,

    [Parameter(Mandatory)]
    [string]$RepoRootDirectory,

    [string]$InstallDirectory = 'C:\Program Files\OutOfTheBox',
    [string]$DataDirectory = (Join-Path $env:ProgramData 'OutOfTheBox'),
    [string]$ServiceName = 'OutOfTheBox',
    [string]$ServiceAccountName = 'svc-outofthebox',
    [int]$Port = 5443,
    [string[]]$AllowedRemoteAddresses = @('Any'),
    [string]$BearerToken,
    [int]$DefaultExecutionTimeoutSeconds = 600,
    [int]$MaximumExecutionTimeoutSeconds = 3600,
    [int]$OutputCapBytes = 5242880,
    [string]$CertificatePath,
    [securestring]$CertificatePassword
)

$ErrorActionPreference = 'Stop'

function Assert-CommandExists {
    param([string]$Name, [string]$Reason)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH. $Reason"
    }
}

Write-Host "== OutOfTheBox install =="

# --- Pre-flight checks -------------------------------------------------------------------------
if (-not (Test-Path (Join-Path $SourcePath 'OutOfTheBox.Host.exe'))) {
    throw "OutOfTheBox.Host.exe not found under '$SourcePath' - publish first: dotnet publish src/OutOfTheBox.Host -p:PublishProfile=win-x64 -o <SourcePath>"
}
$chartJsPath = Join-Path $SourcePath 'wwwroot\_content\OutOfTheBox.Presentation\js\vendor\chart.umd.min.js'
if (-not (Test-Path $chartJsPath)) {
    throw "Vendored chart.js not found at '$chartJsPath' - the publish output looks incomplete (wwwroot wasn't composed in)."
}
Assert-CommandExists -Name 'git' -Reason "git must be installed and on PATH before installing - it's a required host prerequisite alongside the OS itself, same as the existing implicit dotnet.exe assumption for the service account's own PATH (verified below once the account exists)."

# --- Service account ----------------------------------------------------------------------------
Write-Host "Creating service account '$ServiceAccountName'..."
$accountPassword = -join ((1..32) | ForEach-Object { [char](Get-Random -Minimum 33 -Maximum 126) })
$securePassword = ConvertTo-SecureString $accountPassword -AsPlainText -Force

if (Get-LocalUser -Name $ServiceAccountName -ErrorAction SilentlyContinue) {
    Write-Warning "Local user '$ServiceAccountName' already exists - reusing it, but NOT resetting its password (a prior install/reinstall likely already configured it)."
}
else {
    New-LocalUser -Name $ServiceAccountName -Password $securePassword -PasswordNeverExpires -UserMayNotChangePassword `
        -Description 'Dedicated least-privilege account for the OutOfTheBox service - not local admin, not shared with any other service.' | Out-Null
}

# "Performance Monitor Users" membership is required for PerformanceCounter access to the
# Processor category (per design.md's resource-monitoring risk callout) - granted explicitly
# rather than defaulting to a broader account/group.
Add-LocalGroupMember -Group 'Performance Monitor Users' -Member $ServiceAccountName -ErrorAction SilentlyContinue

$serviceCredential = New-Object System.Management.Automation.PSCredential(".\$ServiceAccountName", $securePassword)

# --- Install + data directories -------------------------------------------------------------
Write-Host "Creating install directory '$InstallDirectory' and data directory '$DataDirectory'..."
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $DataDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $RepoRootDirectory -Force | Out-Null

Write-Host "Copying published output into '$InstallDirectory'..."
Copy-Item -Path (Join-Path $SourcePath '*') -Destination $InstallDirectory -Recurse -Force

# The service account needs to write the SQLite file (and its -wal/-shm sidecars) and read its own
# config in the data directory, and read/write repos under the configured root - it needs no
# rights on the (disposable, upgrade-replaceable) install directory beyond executing the exe,
# which every account can already do by default.
Write-Host "Granting '$ServiceAccountName' rights on the data and repo-root directories..."
icacls $DataDirectory /grant "${ServiceAccountName}:(OI)(CI)M" | Out-Null
icacls $RepoRootDirectory /grant "${ServiceAccountName}:(OI)(CI)M" | Out-Null

# --- Certificate -----------------------------------------------------------------------------
if (-not $CertificatePath) {
    Write-Host "No certificate supplied - generating a self-signed one via dotnet dev-certs (see INSTALL.md for how the sbx-side client must pin it, since it isn't from a publicly trusted CA)..."
    $CertificatePath = Join-Path $DataDirectory 'outofthebox.pfx'
    $certPasswordPlain = -join ((1..24) | ForEach-Object { [char](Get-Random -Minimum 33 -Maximum 126) })
    $CertificatePassword = ConvertTo-SecureString $certPasswordPlain -AsPlainText -Force
    & dotnet dev-certs https -ep $CertificatePath -p $certPasswordPlain | Out-Null
}
if (-not $CertificatePassword) {
    throw 'CertificatePassword is required when CertificatePath is supplied explicitly.'
}
$certPasswordForConfig = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto([System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword))

# --- Bearer token ------------------------------------------------------------------------------
if (-not $BearerToken) {
    $tokenBytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($tokenBytes)
    $BearerToken = [Convert]::ToBase64String($tokenBytes)
    Write-Host "Generated bearer token (copy this now - it will not be printed again): $BearerToken" -ForegroundColor Yellow
}

# --- Production config (data directory - see Program.cs's OUTOFTHEBOX_DATA_DIR handling) -----
Write-Host "Writing production config to '$DataDirectory\appsettings.json'..."
$config = [ordered]@{
    Kestrel     = [ordered]@{
        Endpoints = [ordered]@{
            Https = [ordered]@{
                Url         = "https://0.0.0.0:$Port"
                Certificate = [ordered]@{
                    Path     = $CertificatePath
                    Password = $certPasswordForConfig
                }
            }
        }
    }
    OutOfTheBox = [ordered]@{
        RootDirectory                       = $RepoRootDirectory
        BearerToken                         = $BearerToken
        DefaultExecutionTimeoutSeconds      = $DefaultExecutionTimeoutSeconds
        MaximumExecutionTimeoutSeconds      = $MaximumExecutionTimeoutSeconds
        OutputCapBytes                      = $OutputCapBytes
        SqliteFilePath                      = (Join-Path $DataDirectory 'outofthebox.db')
        RepositoryStatsSamplerIntervalSeconds = 60
        ResourceSamplerIntervalSeconds      = 3
    }
}
$config | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $DataDirectory 'appsettings.json') -Encoding UTF8

# --- Windows Service -----------------------------------------------------------------------------
$exePath = Join-Path $InstallDirectory 'OutOfTheBox.Host.exe'
Write-Host "Creating Windows Service '$ServiceName'..."
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Warning "Service '$ServiceName' already exists - stopping and removing it first (this is an install, not an upgrade; use upgrade.ps1 to update an existing install in place)."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# Creating the service with a specific `obj=` account is what causes SCM to grant that account the
# "Log on as a service" right automatically - no separate secedit/ntrights step needed.
& sc.exe create $ServiceName binPath= "`"$exePath`"" obj= ".\$ServiceAccountName" password= "$accountPassword" start= auto DisplayName= "OutOfTheBox" | Out-Null

# SCM does not restart a crashed service by default - this has to be configured explicitly
# (per design.md's Packaging decision).
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name 'Environment' `
    -Value @("OUTOFTHEBOX_DATA_DIR=$DataDirectory") -Type MultiString

# --- Firewall --------------------------------------------------------------------------------
Write-Host "Opening firewall rule for port $Port..."
Remove-NetFirewallRule -DisplayName 'OutOfTheBox' -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName 'OutOfTheBox' -Direction Inbound -Protocol TCP -LocalPort $Port `
    -RemoteAddress $AllowedRemoteAddresses -Action Allow | Out-Null

# --- Start -----------------------------------------------------------------------------------
Write-Host "Starting service..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2
$service = Get-Service -Name $ServiceName
if ($service.Status -ne 'Running') {
    throw "Service did not reach the Running state (status: $($service.Status)) - check the Windows Event Log (Application) for OutOfTheBox startup errors."
}

# --- Post-install verification -----------------------------------------------------------------
# git on the service account's own PATH - a pre-flight check for the account, not just the
# operator's interactive session, since a service process inherits the account's environment, not
# necessarily the installer's.
$gitCheck = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c where git' -Credential $serviceCredential `
    -LoadUserProfile -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\oob-git-check.txt" -RedirectStandardError "$env:TEMP\oob-git-check-err.txt"
if ($gitCheck.ExitCode -ne 0) {
    Write-Warning "git.exe was not resolvable on PATH for '$ServiceAccountName' - the service will fail every git run/clone until this is fixed (e.g. ensure git's install directory is on the machine-wide PATH, not just the current interactive user's)."
}
Remove-Item "$env:TEMP\oob-git-check.txt", "$env:TEMP\oob-git-check-err.txt" -ErrorAction SilentlyContinue

Write-Host "== Install complete. Service '$ServiceName' is running on https://0.0.0.0:$Port =="
