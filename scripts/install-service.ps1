#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Development helper: publishes the service and installs (or reinstalls) it as a Windows service.

.PARAMETER Uninstall
    Stop and remove the service instead.

.EXAMPLE
    .\scripts\install-service.ps1
    .\scripts\install-service.ps1 -Uninstall
#>
[CmdletBinding()]
param([switch] $Uninstall)

$ErrorActionPreference = 'Stop'
$serviceName = 'BeProductive'
$repoRoot = Split-Path -Parent $PSScriptRoot
$installDirectory = Join-Path $env:ProgramFiles 'BeProductive'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    # A clean stop undoes everything the service applied.
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
    sc.exe delete $serviceName | Out-Null
    Write-Host "Removed the existing $serviceName service."
}
if ($Uninstall) { return }

dotnet publish (Join-Path $repoRoot 'src\BeProductive.Service') -c Release -r win-x64 --self-contained false -o $installDirectory
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

$binary = Join-Path $installDirectory 'BeProductive.Service.exe'
New-Service -Name $serviceName -BinaryPathName "`"$binary`"" -DisplayName 'Be Productive' `
    -Description 'Keeps focus sessions focused.' -StartupType Automatic | Out-Null
Start-Service -Name $serviceName

Write-Host "Installed and started $serviceName from $installDirectory."
Write-Host 'Warnings and errors go to the Windows Event Log (Windows Logs > Application, source BeProductive.Service).'
