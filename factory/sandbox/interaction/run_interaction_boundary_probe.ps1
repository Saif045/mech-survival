[CmdletBinding()]
param(
    [string]$Label = (Get-Date -Format 'yyyyMMdd_HHmmss'),
    [switch]$EngineVerbose,
    [string]$Godot = 'D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
while (-not (Test-Path -LiteralPath (Join-Path $repo 'project.godot'))) {
    $parent = Split-Path -Parent $repo
    if (-not $parent -or $parent -eq $repo) { throw 'Cannot find repository project.godot.' }
    $repo = $parent
}
if ($Label -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid evidence label.' }
$evidence = Join-Path $repo "factory/artifacts/interaction_boundary/$Label"
if (Test-Path -LiteralPath $evidence) { throw 'Use a fresh evidence label.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$profileRoot = [IO.Path]::GetFullPath((Join-Path $evidence 'profile'))
$oldAppData = $env:APPDATA
$oldLocalAppData = $env:LOCALAPPDATA
$owned = @()
$result = 'BLOCKED'
$stage = 'preflight'
function Start-Check($name, $extra) {
    $profile = Join-Path $profileRoot $name
    $env:APPDATA = Join-Path $profile 'Roaming'
    $env:LOCALAPPDATA = Join-Path $profile 'Local'
    New-Item -ItemType Directory -Path $env:APPDATA,$env:LOCALAPPDATA | Out-Null
    $arguments = @('--headless','--path',$repo,'--log-file',"$evidence/$name.engine.log") + $extra
    if ($EngineVerbose -and $name -in @('host','client')) { $arguments = @('--verbose') + $arguments }
    Start-Process -FilePath $Godot -ArgumentList $arguments -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence/$name.stdout.log" -RedirectStandardError "$evidence/$name.stderr.log"
}
function Assert-Check($game, $name, $timeout, $marker) {
    if (-not $game.WaitForExit($timeout * 1000)) { throw "$name timed out after $timeout seconds." }
    $game.Refresh()
    if ($game.ExitCode -ne 0) { throw "$name exited with $($game.ExitCode)." }
    $errors = @(Select-String -Path "$evidence/$name.stdout.log","$evidence/$name.stderr.log" -Pattern '(^|\s)(ERROR:|SCRIPT ERROR:)|_FAIL:|ObjectDB.*leaked|resources still in use')
    if ($errors.Count) { throw "$name logged runtime errors; see preserved logs." }
    if ($marker -and -not (Select-String -Path "$evidence/$name.stdout.log" -SimpleMatch $marker)) { throw "$name missing acceptance marker." }
}
try {
    if (-not (Test-Path -LiteralPath $Godot)) { throw 'Godot unavailable.' }
    $assembly = Join-Path $repo '.godot/mono/temp/bin/Debug/GameFactory.dll'
    if (-not (Test-Path -LiteralPath $assembly)) { throw 'Build before running acceptance.' }
    if (Get-NetUDPEndpoint -LocalPort 24879 -ErrorAction SilentlyContinue) { throw 'Probe port already occupied.' }
    Get-FileHash -LiteralPath $assembly | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$evidence/build.json"
    git -C $repo diff | Set-Content "$evidence/tested-source.diff"
    # New source/fixtures are included in the frozen-input manifest as well.
    Get-ChildItem -LiteralPath "$repo/factory/gameplay/interaction","$repo/factory/sandbox/interaction" -File |
        Get-FileHash | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$evidence/inputs.json"
    $result = 'FAIL'
    $stage = 'import'
    $import = Start-Check 'import' @('--editor','--quit')
    $owned += $import
    Assert-Check $import 'import' 60 $null
    $stage = 'default'
    $default = Start-Check 'default' @('res://factory/sandbox/gas/godot_gas_interop_probe.tscn')
    $owned += $default
    Assert-Check $default 'default' 55 'player_composition_probe_passed'
    foreach ($assertion in @('default_input_switch=True','canonical_downed_dead_interaction_gate=True','interaction_pickup=True','bidirectional_revive=True','probe_passed')) {
        if (-not (Select-String -Path "$evidence/default.stdout.log" -SimpleMatch $assertion)) { throw "Default missing $assertion." }
    }
    $stage = 'custom_host'
    $hostGame = Start-Check 'host' @('res://factory/sandbox/interaction/interaction_boundary_probe.tscn','--','--interaction-host')
    $owned += $hostGame
    $deadline = (Get-Date).AddSeconds(10)
    while (-not (Select-String -Path "$evidence/host.stdout.log" -SimpleMatch 'INTERACTION_HOST_READY')) {
        if ($hostGame.HasExited -or (Get-Date) -ge $deadline) { throw 'Host did not reach listener readiness.' }
        Start-Sleep -Milliseconds 100
    }
    $stage = 'custom_rpc'
    $clientGame = Start-Check 'client' @('res://factory/sandbox/interaction/interaction_boundary_probe.tscn')
    $owned += $clientGame
    Assert-Check $hostGame 'host' 30 'INTERACTION_BOUNDARY_PASS role=host'
    Assert-Check $clientGame 'client' 10 'INTERACTION_BOUNDARY_PASS role=client'
    foreach ($assertion in @('invalid_policy_configuration','host_owner','step_0','step_1','step_2','step_3','step_4','step_5')) {
        if (-not (Select-String -Path "$evidence/host.stdout.log" -SimpleMatch "assertion=$assertion")) { throw "Missing host assertion $assertion." }
    }
    foreach ($assertion in @('host_result_replicated','client_result_replicated')) {
        if (-not (Select-String -Path "$evidence/client.stdout.log" -SimpleMatch "assertion=$assertion")) { throw "Missing client assertion $assertion." }
    }
    $result = 'PASS'
} finally {
    foreach ($game in $owned) {
        if (Get-Process -Id $game.Id -ErrorAction SilentlyContinue) {
            Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
            [void]$game.WaitForExit(5000)
        }
    }
    $remaining = @($owned | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue }).Count
    $env:APPDATA = $oldAppData
    $env:LOCALAPPDATA = $oldLocalAppData
    # Preserve disposable-profile logs/settings before deleting only this run's verified child path.
    if (Test-Path -LiteralPath $profileRoot) {
        Copy-Item -LiteralPath $profileRoot -Destination (Join-Path $evidence 'profile-evidence') -Recurse
    }
    if ($remaining -eq 0 -and $profileRoot.StartsWith([IO.Path]::GetFullPath($evidence) + [IO.Path]::DirectorySeparatorChar)) {
        if (Test-Path -LiteralPath $profileRoot) { Remove-Item -LiteralPath $profileRoot -Recurse -Force }
    }
    $profileClean = -not (Test-Path -LiteralPath $profileRoot)
    @{ result = $result; stage = $stage; cleanup_processes = $remaining; profile_removed = $profileClean;
       pids = @($owned.Id); vm = 'not used'; source_revision = (& git -C $repo rev-parse HEAD);
       godot = $Godot; port = 24879; default_timeout_seconds = 55; custom_timeout_seconds = 20 } |
        ConvertTo-Json | Set-Content "$evidence/result.json"
    Write-Output "INTERACTION_RESULT=$result cleanup_live=$remaining profile_removed=$profileClean evidence=$evidence"
    if ($remaining -ne 0 -or -not $profileClean) { throw 'Process/profile cleanup incomplete.' }
}
