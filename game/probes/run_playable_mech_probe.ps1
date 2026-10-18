[CmdletBinding()]
param(
    [ValidateSet('Import','Offline','Multiplayer')][string]$Mode = 'Multiplayer',
    [string]$RunId = ('playable_' + (Get-Date -Format 'yyyyMMdd_HHmmss')),
    [string]$Godot = 'D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if ($RunId -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunId must be a simple directory label.' }
$evidence = Join-Path $repo "game/artifacts/playable_mech/$RunId"
if (Test-Path -LiteralPath $evidence) { throw 'Use a unique run ID; existing evidence is immutable.' }
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$owned = @()
$result = 'BLOCKED'
$reason = ''
$stage = 'preflight'
$previousAppData = $env:APPDATA
$profile = [IO.Path]::GetFullPath((Join-Path $repo ('.tmp-playable-profile-' + $RunId)))
$sourcePaths = @(& git -C $repo ls-files --cached --others --exclude-standard)
$assembly = Join-Path $repo '.godot/mono/temp/bin/Debug/GameFactory.dll'
$before = @()

function Get-FrozenState {
    foreach ($relative in $sourcePaths) {
        $absolute = Join-Path $repo $relative
        if (Test-Path -LiteralPath $absolute -PathType Leaf) {
            [pscustomobject]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash }
        }
    }
    [pscustomobject]@{ path = '.godot/mono/temp/bin/Debug/GameFactory.dll'; sha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash }
}

# DEFINE: import <=60s; offline/two-peer <=90s; host-ready <=10s.
# Assertions: configured normal shell and PlayerScene; lifecycle/spawn positions;
# complete loadouts and native GAS/resource initialization; split authority/cameras;
# owner-isolated bidirectional Netfox motion and queued-edge physical jumps;
# remote state/presentation convergence and nineteen client guards per body;
# two rounds with fresh parts/resources/monotonic objects and Netfox teardown.
# Ten controlled 24-tick Mobility measurements: pristine, partial damage,
# one leg, native +0.5 modifier, damage with modifier retained, capability gate
# with positive modifier value, zero and restored setup. Resources/jump unchanged.
# Participants are test-owned local Godot processes only. No VM or Steam session.
@{ mode = $Mode; run_id = $RunId; godot = $Godot; scene = 'res://game/probes/playable_mech_probe.tscn';
    build_sha = (& git -C $repo rev-parse HEAD); source_status = @(& git -C $repo status --short);
    timeout_seconds = 90; transport = 'local ENet (or offline)'; vm = 'not used'; steam = 'no Steam session requested';
    profile = $profile; profile_policy = 'Disposable APPDATA, restored and deleted after each run. Request command-level approval if sandbox access is insufficient.' } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$evidence/contract.json"

function Start-Probe([string]$Role, [string[]]$Extra) {
    $arguments = @('--headless','--path',$repo,'--log-file',"$evidence/$Role.engine.log")
    if ($Mode -eq 'Import') { $arguments += @('--editor','--quit') }
    else { $arguments += @('res://game/probes/playable_mech_probe.tscn','--') + $Extra }
    $process = Start-Process -FilePath $Godot -ArgumentList $arguments -WorkingDirectory $repo -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$evidence/$Role.stdout.log" -RedirectStandardError "$evidence/$Role.stderr.log"
    return $process
}
try {
    if (-not (Test-Path -LiteralPath $Godot)) { throw 'Godot executable is missing.' }
    if ($Mode -eq 'Import') {
        $editors = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'Godot%'" | Where-Object {
            $_.CommandLine -match '(?:^|\s)--editor(?:\s|$)' -and
            $_.CommandLine.Replace('/', '\').IndexOf($repo, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
        if ($editors.Count) { throw 'Close the Godot editor for this checkout before Import: concurrent editor imports can lock the GodotSteam temporary DLL copy.' }
    }
    if (-not (Test-Path -LiteralPath "$repo/.godot/mono/temp/bin/Debug/GameFactory.dll")) { throw 'Build the project before launching.' }
    if ($Mode -eq 'Multiplayer' -and (Get-NetUDPEndpoint -LocalPort 24874 -ErrorAction SilentlyContinue)) { throw 'Probe port is occupied; no existing process will be killed.' }
    if (-not $profile.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Profile is outside the repository.' }
    if (Test-Path -LiteralPath $profile) { throw 'Disposable profile already exists.' }
    $before = @(Get-FrozenState)
    $before | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$evidence/frozen-before.json"
    New-Item -ItemType Directory -Path $profile | Out-Null
    $env:APPDATA = $profile
    $stage = 'launch'; $result = 'FAIL'
    if ($Mode -eq 'Import') { $owned += Start-Probe 'import' @() }
    elseif ($Mode -eq 'Offline') { $owned += Start-Probe 'offline' @('--playable-offline') }
    else {
        $owned += Start-Probe 'host' @('--playable-host')
        $deadline = (Get-Date).AddSeconds(10)
        do {
            if ($owned[0].HasExited) { throw 'Host exited before readiness.' }
            $ready = Select-String -LiteralPath "$evidence/host.stdout.log" -Pattern 'PLAYABLE_READY role=server' -Quiet
            if ($ready) { break }
            Start-Sleep -Milliseconds 100
        } while ((Get-Date) -lt $deadline)
        if (-not $ready) { throw 'Host readiness timed out.' }
        $stage = 'host_ready'
        $owned += Start-Probe 'client' @('--playable-client')
    }
    $timeout = if ($Mode -eq 'Import') { 60000 } else { 90000 }
    foreach ($process in $owned) {
        if (-not $process.WaitForExit($timeout)) { throw "Process $($process.Id) exceeded bounded timeout." }
        $process.Refresh()
        if ($process.ExitCode -ne 0) { throw "Process $($process.Id) exited with $($process.ExitCode)." }
    }
    $stage = 'assertions'
    $errors = @(Get-ChildItem -LiteralPath $evidence -Filter '*.log' | Select-String -Pattern 'PLAYABLE_FAIL|SCRIPT ERROR|^ERROR:')
    if ($errors.Count) { throw "Engine/runtime errors: $($errors.Count). Inspect preserved logs." }
    if ($Mode -eq 'Offline') {
        if (-not (Select-String -LiteralPath "$evidence/offline.stdout.log" -Pattern 'PLAYABLE_OFFLINE_PASS' -Quiet)) { throw 'Offline playable assertions did not complete.' }
    }
    if ($Mode -eq 'Multiplayer') {
        if (-not (Select-String -LiteralPath "$evidence/host.stdout.log" -Pattern 'PLAYABLE_SERVER_PASS' -Quiet)) { throw 'Server playable assertions did not complete.' }
        if (-not (Select-String -LiteralPath "$evidence/client.stdout.log" -Pattern 'PLAYABLE_CLIENT_PASS' -Quiet)) { throw 'Client playable assertions did not complete.' }
        $observed = @(Select-String -LiteralPath "$evidence/client.stdout.log" -Pattern 'PLAYABLE_OBSERVED stage=')
        if ($observed.Count -ne 7) { throw 'Expected seven canonical/converged snapshots with client guards.' }
    }
    if ($Mode -ne 'Import') {
        if (-not (Select-String -LiteralPath "$evidence/$(if ($Mode -eq 'Offline') { 'offline' } else { 'host' }).stdout.log" -Pattern 'MOBILITY_ACCEPTANCE_PASS cases=10' -Quiet)) { throw 'Mobility acceptance did not complete.' }
        if ($Mode -eq 'Multiplayer') {
            $mobility = @(Select-String -LiteralPath "$evidence/client.stdout.log" -Pattern '^MOBILITY_OBSERVED stage=')
            $ownerMotion = @(Select-String -LiteralPath "$evidence/client.stdout.log" -Pattern '^MOBILITY_MOVEMENT_PASS case=')
            if ($mobility.Count -ne 20 -or $ownerMotion.Count -ne 10) { throw 'Expected twenty canonical Mobility observations and ten client-owner measurements.' }
        }
        $cleanupMarkers = @(Get-ChildItem -LiteralPath $evidence -Filter '*.stdout.log' | Select-String -Pattern 'PLAYABLE_CLEANUP objects=0')
        if ($cleanupMarkers.Count -ne $owned.Count) { throw 'World cleanup assertions are missing.' }
    }
    $stage = 'complete'; $result = 'PASS'
} catch {
    $reason = $_.Exception.Message
    Write-Warning $reason
} finally {
    foreach ($process in $owned) {
        try {
            if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(5000) | Out-Null }
        } catch { $result = 'FAIL'; $reason += " Cleanup of PID $($process.Id) failed: $($_.Exception.Message)" }
    }
    $env:APPDATA = $previousAppData
    $remaining = @($owned | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue }).Count
    if ($remaining -gt 0) { $result = 'FAIL'; $reason += ' Test process cleanup failed.' }
    $portReleased = $null
    if ($Mode -eq 'Multiplayer') {
        $portReleased = @(Get-NetUDPEndpoint -LocalPort 24874 -ErrorAction SilentlyContinue).Count -eq 0
        if (-not $portReleased) { $result = 'FAIL'; $reason += ' Test port remains occupied.' }
    }
    $profileRemoved = $true
    try {
        if (Test-Path -LiteralPath $profile) {
            $resolvedProfile = (Resolve-Path -LiteralPath $profile).Path
            if ($resolvedProfile -ne $profile -or -not $resolvedProfile.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Profile cleanup path verification failed.' }
            Remove-Item -LiteralPath $resolvedProfile -Recurse -Force
        }
    } catch { $result = 'FAIL'; $reason += " Profile cleanup failed: $($_.Exception.Message)"; $profileRemoved = $false }
    $unchanged = $null
    if ($before.Count) {
        try {
            $after = @(Get-FrozenState)
            $after | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$evidence/frozen-after.json"
            $changes = @(Compare-Object $before $after -Property path,sha256)
            $unchanged = $changes.Count -eq 0
            if (-not $unchanged) { $result = 'FAIL'; $reason += ' Frozen source/build changed.' }
        } catch { $result = 'FAIL'; $reason += " Frozen-state verification failed: $($_.Exception.Message)" }
    }
    @{ result = $result; deepest_stage = $stage; reason = $reason; cleanup_processes = $remaining;
        pids = @($owned.Id); vm_cleanup = 'not applicable: no VM processes launched'; artifacts = $evidence;
        source_build_unchanged = $unchanged; profile_removed = $profileRemoved;
        appdata_restored = ($env:APPDATA -eq $previousAppData); port_released = $portReleased } |
        ConvertTo-Json | Set-Content -LiteralPath "$evidence/result.json"
    Write-Output "PLAYABLE_PROBE_RESULT=$result mode=$Mode cleanup_live=$remaining evidence=$evidence"
}
if ($result -ne 'PASS') { exit 1 }
