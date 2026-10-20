#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Undoes everything Be Productive has changed on this PC. The last resort when the app won't let go.

.DESCRIPTION
    1. Disables the BeProductive service (so it can't restart and re-lock) and stops it.
       A clean stop already undoes its own changes.
    2. Undoes whatever is still listed in the change journal, newest first:
       registry values go back to what they were, keys the app created are deleted if empty,
       and the app's section is removed from the hosts file.
    3. Removes the app's hosts section even if the journal is missing or damaged
       (it's found by its marker comments, so nothing else in the file is touched).
    4. Flushes the DNS cache.

    Keep a copy of this script somewhere outside the PC, such as a USB stick.

.PARAMETER DataDirectory
    Where the app keeps its state. Defaults to C:\ProgramData\BeProductive.

.PARAMETER KeepServiceEnabled
    Stop the service but don't disable it, so it starts again at the next boot.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\restore.ps1
#>
[CmdletBinding()]
param(
    [string] $DataDirectory = (Join-Path $env:ProgramData 'BeProductive'),
    [switch] $KeepServiceEnabled
)

$ErrorActionPreference = 'Stop'
$serviceName = 'BeProductive'
$journalPath = Join-Path $DataDirectory 'journal.json'
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
# Must match HostsSection.cs.
$beginMarker = '# BEGIN BeProductive (managed automatically, do not edit)'
$endMarker = '# END BeProductive'
$failures = 0

function Write-Step([string] $message) { Write-Host "-> $message" -ForegroundColor Cyan }

function Remove-HostsSection {
    if (-not (Test-Path -LiteralPath $hostsPath)) { return }

    $content = [IO.File]::ReadAllText($hostsPath)
    $newline = if ($content.Contains("`n") -and -not $content.Contains("`r`n")) { "`n" } else { "`r`n" }
    $lines = $content -replace "`r`n", "`n" -split "`n"
    if ($lines[-1] -eq '') { $lines = @($lines | Select-Object -First ($lines.Count - 1)) }

    $kept = New-Object System.Collections.Generic.List[string]
    $inside = $false
    foreach ($line in $lines) {
        if (-not $inside -and $line.Trim() -eq $beginMarker) { $inside = $true; continue }
        if ($inside) {
            if ($line.Trim() -eq $endMarker) { $inside = $false; continue }
            # If the end marker was deleted, stop at the first line that isn't one of ours.
            if ($line.StartsWith('0.0.0.0 ')) { continue }
            $inside = $false
        }
        $kept.Add($line)
    }

    $restored = if ($kept.Count -eq 0) { '' } else { ($kept -join $newline) + $newline }
    if ($restored -ne $content) {
        [IO.File]::WriteAllText($hostsPath, $restored, (New-Object System.Text.UTF8Encoding $false))
        Write-Host '   Removed the Be Productive section from the hosts file.'
    }
}

function Undo-Entry($entry) {
    $path = "Registry::$($entry.keyPath)"
    switch ($entry.type) {
        'registryValue' {
            if ($entry.originalExists) {
                if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }
                if ($entry.originalKind -eq 'DWord') {
                    New-ItemProperty -LiteralPath $path -Name $entry.name -PropertyType DWord -Value ([int] $entry.originalData) -Force | Out-Null
                } else {
                    New-ItemProperty -LiteralPath $path -Name $entry.name -PropertyType String -Value ([string] $entry.originalData) -Force | Out-Null
                }
                Write-Host "   Put back $($entry.keyPath)\$($entry.name)"
            } elseif (Test-Path -LiteralPath $path) {
                Remove-ItemProperty -LiteralPath $path -Name $entry.name -ErrorAction SilentlyContinue
                Write-Host "   Removed $($entry.keyPath)\$($entry.name)"
            }
        }
        'registryKeyCreated' {
            $key = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
            if ($key -and $key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0) {
                Remove-Item -LiteralPath $path
                Write-Host "   Removed key $($entry.keyPath)"
            }
        }
        'hostsSection' { Remove-HostsSection }
        default { throw "Unknown journal entry type '$($entry.type)'." }
    }
}

# 1. The service
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if (-not $KeepServiceEnabled) {
        Write-Step 'Disabling the service so it stays off after a reboot'
        Set-Service -Name $serviceName -StartupType Disabled
    }
    if ($service.Status -ne 'Stopped') {
        Write-Step 'Stopping the service'
        try {
            Stop-Service -Name $serviceName -Force
        } catch {
            Write-Warning "Couldn't stop the service: $($_.Exception.Message). Undoing its changes anyway."
            $failures++
        }
    }
} else {
    Write-Step 'The service isn''t installed'
}

# 2. The journal
if (Test-Path -LiteralPath $journalPath) {
    Write-Step "Undoing changes listed in $journalPath"
    try {
        $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
        $entries = @($journal.entries)
        [array]::Reverse($entries)
        foreach ($entry in $entries) {
            try {
                Undo-Entry $entry
            } catch {
                Write-Warning "Couldn't undo $($entry.type) $($entry.keyPath) $($entry.name): $($_.Exception.Message)"
                $failures++
            }
        }
        if ($failures -eq 0) { Remove-Item -LiteralPath $journalPath }
    } catch {
        Write-Warning "The journal couldn't be read: $($_.Exception.Message). Registry changes can't be undone automatically; see the README."
        $failures++
    }
} else {
    Write-Step 'No change journal: the service had nothing applied, or already undid it'
}

# 3. The hosts file, journal or not
Write-Step 'Checking the hosts file'
Remove-HostsSection

# 4. The policies, as a backstop if the journal was lost. Deleting a policy value just removes the
#    restriction; it does not change any real setting, so this is always safe even with no journal.
Write-Step 'Clearing Be Productive policy values (backstop)'
$chrome = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Google\Chrome'
foreach ($name in 'DnsOverHttpsMode', 'IncognitoModeAvailability', 'DeveloperToolsAvailability') {
    if (Test-Path -LiteralPath $chrome) { Remove-ItemProperty -LiteralPath $chrome -Name $name -ErrorAction SilentlyContinue }
}
# Admin-tool policies live in the signed-in user's hive. Clear them for every loaded user hive.
foreach ($hive in Get-ChildItem -LiteralPath 'Registry::HKEY_USERS' -ErrorAction SilentlyContinue) {
    $system = "Registry::$($hive.Name)\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    if (Test-Path -LiteralPath $system) {
        foreach ($name in 'DisableTaskMgr', 'DisableRegistryTools', 'DisableCMD') {
            Remove-ItemProperty -LiteralPath $system -Name $name -ErrorAction SilentlyContinue
        }
    }
}

# 5. DNS
Write-Step 'Flushing the DNS cache'
& (Join-Path $env:SystemRoot 'System32\ipconfig.exe') /flushdns | Out-Null

Write-Host ''
if ($failures -eq 0) {
    Write-Host 'Done. Everything Be Productive changed has been undone.' -ForegroundColor Green
} else {
    Write-Host "Finished with $failures problem(s); see the warnings above." -ForegroundColor Yellow
}
if ($service -and -not $KeepServiceEnabled) {
    Write-Host "The service is disabled. To turn it back on: Set-Service $serviceName -StartupType Automatic; Start-Service $serviceName"
}
exit $failures
