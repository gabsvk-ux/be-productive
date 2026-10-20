#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Development helper: builds and installs (or reinstalls) Be Productive on this PC.

.DESCRIPTION
    Publishes into C:\Program Files\BeProductive:
      Service\  the background service, installed as the BeProductive Windows service
      App\      the desktop app, started for every user at sign-in
      Cli\      the bp command-line tool
    Reinstalling stops everything first. A clean service stop undoes everything it applied.

.PARAMETER Uninstall
    Stop and remove everything instead. The data in C:\ProgramData\BeProductive is kept.

.EXAMPLE
    .\scripts\install.ps1
    .\scripts\install.ps1 -Uninstall
#>
[CmdletBinding()]
param([switch] $Uninstall)

$ErrorActionPreference = 'Stop'
$serviceName = 'BeProductive'
$repoRoot = Split-Path -Parent $PSScriptRoot
$installDirectory = Join-Path $env:ProgramFiles 'BeProductive'
$runKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'

function Publish([string] $project, [string] $target) {
    dotnet publish (Join-Path $repoRoot "src\$project") -c Release -r win-x64 --self-contained false -o (Join-Path $installDirectory $target)
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish of $project failed." }
}

# Stop what's running.
Get-Process -Name 'BeProductive' -ErrorAction SilentlyContinue | Stop-Process -Force
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
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
Start-Service -Name $serviceName

$appBinary = Join-Path $installDirectory 'App\BeProductive.exe'
Set-ItemProperty -Path $runKey -Name $serviceName -Value "`"$appBinary`""
# Started through Explorer so the app runs as you, not elevated like this script.
Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$appBinary`""

Write-Host "Installed to $installDirectory and started the service and the app."
Write-Host "The app starts at every sign-in. The CLI is $(Join-Path $installDirectory 'Cli\bp.exe')."
Write-Host 'Service warnings and errors go to the Windows Event Log (Windows Logs > Application, source BeProductive.Service).'
