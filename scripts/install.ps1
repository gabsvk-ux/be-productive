#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Builds and installs (or reinstalls/uninstalls) Be Productive on this PC.

.DESCRIPTION
    Publishes into C:\Program Files\BeProductive:
      Service\  the background service, installed as the BeProductive Windows service
      App\      the desktop app, started for every user at sign-in
      Cli\      the bp command-line tool

    Step-6 hardening applied on install:
      * Recovery actions: Windows restarts the service a few seconds after it is killed.
      * A service security descriptor that denies Stop/Pause/Change-config to administrators, so the
        service can't be switched off with a casual "Stop-Service". Administrators keep WRITE_DAC, so
        scripts\restore.ps1 (and a determined admin) can reset it first — this is friction, not a wall.
    The watchdog in the service relaunches the app if it is killed while the laptop is locked.

.PARAMETER Uninstall
    Remove everything. Requires a recovery code (via -Code) when recovery codes are set up, unless none
    are. Your data in C:\ProgramData\BeProductive is kept. For a no-code emergency removal use Safe Mode
    plus scripts\restore.ps1 (which disables the service) — that undoes blocking without uninstalling.

.PARAMETER Code
    A recovery code, used only with -Uninstall.

.EXAMPLE
    .\scripts\install.ps1
    .\scripts\install.ps1 -Uninstall -Code XXXX-XXXX-XXXX-XXXX
#>
[CmdletBinding()]
param(
    [switch] $Uninstall,
    [string] $Code
)

$ErrorActionPreference = 'Stop'
$serviceName = 'BeProductive'
$repoRoot = Split-Path -Parent $PSScriptRoot
$installDirectory = Join-Path $env:ProgramFiles 'BeProductive'
$runKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
$bpExe = Join-Path $installDirectory 'Cli\bp.exe'

# A permissive service DACL (administrators get full control): the default we harden away from and reset to.
$openSddl = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)'
# The same, but Stop (WP), Pause/Continue (DT) and Change-config (DC) are denied to administrators.
# Administrators keep DELETE and WRITE_DAC, so the descriptor can be reset to undo this.
$lockedSddl = 'D:(D;;WPDTDC;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)'

function Publish([string] $project, [string] $target) {
    dotnet publish (Join-Path $repoRoot "src\$project") -c Release -r win-x64 --self-contained false -o (Join-Path $installDirectory $target)
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish of $project failed." }
}

if ($Uninstall) {
    # Require a recovery code to uninstall, when codes are set up. The installed CLI decides and consumes one.
    if (Test-Path -LiteralPath $bpExe) {
        & $bpExe uninstall-authorize $Code
        if ($LASTEXITCODE -ne 0) {
            Write-Host 'Uninstall not authorized. Supply a valid recovery code with -Code, or use Safe Mode + restore.ps1 to just stop blocking.' -ForegroundColor Yellow
            exit $LASTEXITCODE
        }
    } else {
        Write-Warning 'The CLI is missing, so a recovery code could not be checked; uninstalling anyway.'
    }
}

# Stop what's running. Reset the service descriptor first, or a hardened install would deny the stop.
Get-Process -Name 'BeProductive' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    sc.exe sdset $serviceName $openSddl | Out-Null
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
    sc.exe delete $serviceName | Out-Null
    Write-Host "Removed the existing $serviceName service."
}
Remove-ItemProperty -Path $runKey -Name $serviceName -ErrorAction SilentlyContinue
if ($Uninstall) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'Uninstalled. Your data in C:\ProgramData\BeProductive was kept.'
    return
}

Publish 'BeProductive.Service' 'Service'
Publish 'BeProductive.App' 'App'
Publish 'BeProductive.Cli' 'Cli'

$serviceBinary = Join-Path $installDirectory 'Service\BeProductive.Service.exe'
New-Service -Name $serviceName -BinaryPathName "`"$serviceBinary`"" -DisplayName 'Be Productive' `
    -Description 'Keeps focus sessions focused.' -StartupType Automatic | Out-Null

# Harden: restart on kill, and deny a casual stop.
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
sc.exe sdset $serviceName $lockedSddl | Out-Null

Start-Service -Name $serviceName

$appBinary = Join-Path $installDirectory 'App\BeProductive.exe'
Set-ItemProperty -Path $runKey -Name $serviceName -Value "`"$appBinary`""
# Started through Explorer so the app runs as you, not elevated like this script.
Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$appBinary`""

Write-Host "Installed to $installDirectory and started the service and the app."
Write-Host "The app starts at every sign-in and is relaunched by the watchdog if closed while locked."
Write-Host "The service restarts if killed and resists a casual stop. To remove: .\scripts\install.ps1 -Uninstall -Code <recovery code>."
Write-Host "Emergency undo without a code: Safe Mode, then scripts\restore.ps1. The CLI is $bpExe."
Write-Host 'Service warnings and errors go to the Windows Event Log (Windows Logs > Application, source BeProductive.Service).'
