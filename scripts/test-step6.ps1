#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Checks the step-6 tamper-resistance features on an installed, live Be Productive.

.DESCRIPTION
    Run this in the test VM after installing (scripts\install.ps1) with Enforcement:DryRun = false.
    Take a VM snapshot first. Your in-session escape is the panic file; keep it enabled while testing.
    You need a PowerShell window that the lock won't close. Admin tools are blocked whenever the laptop is
    locked, so run this EITHER on a free day (set that weekday's minimum to 0 in settings.json, or after
    you've finished the day's focus) OR from inside a focus block in a preset that allows terminals (plan
    "programming" in the app first, then open PowerShell). With -Code the script keeps you in a
    terminal-allowing block for the watchdog test and ends it afterwards.

    It runs, and reports PASS / FAIL / SKIP for:
      2. Service self-protection  - a casual Stop-Service is denied; a killed service restarts.
      3. Uninstall needs a code   - uninstall-authorize refuses with no code and a wrong code
                                     (non-destructive; it does NOT uninstall).
      1. Watchdog                 - only with -Code: starts a focus block in a terminal-allowing
                                     preset, kills the app, checks it is relaunched and a tamper
                                     attempt is recorded, then ends the block with the code.

    Test 4 (scripts\restore.ps1 recovers a hardened install) is deliberately left to run by hand.

.PARAMETER Code
    A recovery code. Needed only for the watchdog test; it is consumed and marks today failed (fine in a VM).
    Without it, the watchdog test is skipped and nothing is consumed or locked.

.PARAMETER Preset
    A preset that allows terminals, so this PowerShell window survives the focus lock. Default: programming.

.EXAMPLE
    .\scripts\test-step6.ps1
    .\scripts\test-step6.ps1 -Code XXXX-XXXX-XXXX-XXXX
#>
[CmdletBinding()]
param(
    [string] $Code,
    [string] $Preset = 'programming'
)

$ErrorActionPreference = 'Continue'
$serviceName = 'BeProductive'
$bp = Join-Path $env:ProgramFiles 'BeProductive\Cli\bp.exe'
$results = [ordered]@{}

function Say([string] $m, [string] $c = 'Gray') { Write-Host $m -ForegroundColor $c }
function Record([string] $name, [bool] $ok, [string] $detail) {
    $results[$name] = @{ Ok = $ok; Detail = $detail }
    $tag = if ($ok) { 'PASS' } else { 'FAIL' }
    Say ("  [{0}] {1} - {2}" -f $tag, $name, $detail) ($(if ($ok) { 'Green' } else { 'Red' }))
}
function Skip([string] $name, [string] $detail) {
    $results[$name] = @{ Ok = $null; Detail = $detail }
    Say ("  [SKIP] {0} - {1}" -f $name, $detail) 'Yellow'
}
function ParseTamper([string] $statsText) {
    if ($statsText -match '(\d+)\s+tamper attempt') { return [int] $Matches[1] }
    return 0
}

if (-not (Test-Path -LiteralPath $bp)) {
    Say "Can't find the CLI at $bp. Install first with scripts\install.ps1." 'Red'
    exit 1
}

# Preconditions
$status = & $bp status 2>&1 | Out-String
if ($status -notmatch 'Enforcement:\s*Live') {
    Say 'Enforcement is not Live. Set Enforcement:DryRun = false in settings.json and restart the service, then rerun.' 'Yellow'
    Say $status
    exit 1
}
Say 'Enforcement is Live. Running step-6 checks...' 'Cyan'
Say ''

# ---- Test 2: service self-protection ------------------------------------------------------------
Say 'Test 2: service self-protection'
$stopOutput = (& sc.exe stop $serviceName 2>&1 | Out-String)
$denied = ($stopOutput -match 'Access is denied') -or ($stopOutput -match 'FAILED 5') -or ((Get-Service $serviceName).Status -eq 'Running')
Record 'Casual stop is denied' $denied $stopOutput.Trim()

# Kill the service process; a recovery action should restart it.
Get-Process -Name 'BeProductive.Service' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$restarted = $false
foreach ($i in 1..15) {
    Start-Sleep -Seconds 1
    if ((Get-Service -Name $serviceName -ErrorAction SilentlyContinue).Status -eq 'Running') { $restarted = $true; break }
}
Record 'Killed service restarts itself' $restarted "service status after kill: $((Get-Service $serviceName).Status)"
Say ''

# ---- Test 3: uninstall needs a code (non-destructive) -------------------------------------------
Say 'Test 3: uninstall authorization (does not uninstall)'
& $bp uninstall-authorize | Out-Null
$noCodeExit = $LASTEXITCODE
& $bp uninstall-authorize '0000-0000-0000-0000' | Out-Null
$wrongExit = $LASTEXITCODE
# Exit 2 = a code is required (codes are set up); exit 0 = no codes set up (so nothing to test).
if ($noCodeExit -eq 0) {
    Skip 'Uninstall needs a code' 'No recovery codes are set up (run: bp codes init), so uninstall is unguarded.'
} else {
    Record 'Uninstall refused without a code' ($noCodeExit -eq 2) "exit code $noCodeExit (expected 2)"
    Record 'Uninstall refused with a wrong code' ($wrongExit -eq 1) "exit code $wrongExit (expected 1)"
}
Say ''

# ---- Test 1: watchdog (needs a live lock; opt-in) ----------------------------------------------
Say 'Test 1: watchdog'
if (-not $Code) {
    Skip 'Watchdog relaunch + tamper' 'No -Code given, so no focus block is started. Pass a recovery code to run this test.'
} else {
    $before = ParseTamper (& $bp stats 2>&1 | Out-String)
    $plan = (& $bp plan $Preset 100 25 2>&1 | Out-String)
    Start-Sleep -Seconds 2

    if ((Get-Process -Name 'BeProductive' -ErrorAction SilentlyContinue)) {
        Get-Process -Name 'BeProductive' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        $back = $false
        foreach ($i in 1..10) {
            Start-Sleep -Seconds 1
            if (Get-Process -Name 'BeProductive' -ErrorAction SilentlyContinue) { $back = $true; break }
        }
        Record 'App relaunched after being killed' $back 'checked for BeProductive.exe after killing it'

        Start-Sleep -Seconds 2
        $after = ParseTamper (& $bp stats 2>&1 | Out-String)
        Record 'Tamper attempt recorded in stats' ($after -gt $before) "tamper attempts: $before -> $after"
    } else {
        Skip 'Watchdog relaunch + tamper' "The app wasn't running to kill. Plan output: $($plan.Trim())"
    }

    # End the focus block cleanly with the code, so the VM isn't left locked.
    Say '  Ending the focus block with the recovery code...'
    & $bp exit $Code | Out-Null
}
Say ''

# Best effort: if a test left the service stopped (self-protection not applied), start it again.
if ((Get-Service -Name $serviceName -ErrorAction SilentlyContinue).Status -eq 'Stopped') {
    Start-Service -Name $serviceName -ErrorAction SilentlyContinue
}

# ---- Summary -----------------------------------------------------------------------------------
$fail = @($results.Values | Where-Object { $_.Ok -eq $false }).Count
$pass = @($results.Values | Where-Object { $_.Ok -eq $true }).Count
$skip = @($results.Values | Where-Object { $null -eq $_.Ok }).Count
Say "Summary: $pass passed, $fail failed, $skip skipped." ($(if ($fail -eq 0) { 'Green' } else { 'Red' }))
Say 'Still to run by hand: Test 4 - scripts\restore.ps1 recovers this hardened install (do it with a snapshot).' 'Cyan'
exit $fail
