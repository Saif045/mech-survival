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
$evidence = Join-Path $repo "factory/artifacts/round_composition/$Label"
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
    if ($EngineVerbose -and $name -match '-(host|client)$') { $arguments = @('--verbose') + $arguments }
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
    if (Get-NetUDPEndpoint -LocalPort 24881 -ErrorAction SilentlyContinue) { throw 'Probe port already occupied.' }
    Get-FileHash -LiteralPath $assembly | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$evidence/build.json"
    git -C $repo diff | Set-Content "$evidence/tested-source.diff"
    # New source/fixtures are included in the frozen-input manifest as well.
    Get-ChildItem -LiteralPath "$repo/factory/shell","$repo/factory/sandbox/shell/round_composition" -File |
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
    foreach ($assertion in @('carry_inventory_equipment_transitions=True','auto_drop=True','stored_equipped_cleanup=True')) {
        if (-not (Select-String -Path "$evidence/default.stdout.log" -SimpleMatch $assertion)) { throw "Default missing $assertion." }
    }
    foreach ($check in @(
        @{ Name = 'default-rounds'; Scene = 'res://factory/sandbox/shell/round_reuse_probe.tscn'; Marker = 'ROUND_REUSE_PASS round=' },
        @{ Name = 'default-spawn-data'; Scene = 'res://factory/sandbox/shell/custom_player_lifecycle_probe.tscn'; Marker = 'CUSTOM_PLAYER_LIFECYCLE_PASS round=' }
    )) {
        $stage = $check.Name
        $game = Start-Check $stage @($check.Scene)
        $owned += $game
        Assert-Check $game $stage 30 $check.Marker
        if (@(Select-String -Path "$evidence/$stage.stdout.log" -SimpleMatch $check.Marker).Count -ne 3) { throw "$stage expected three passed rounds." }
    }
    foreach ($mode in @('custom','empty')) {
        $stage = "$mode-host"
        $extra = @('res://factory/sandbox/shell/round_composition/probe.tscn','--','--composition-host')
        if ($mode -eq 'empty') { $extra += '--empty-content' }
        $hostName = "$mode-host"
        $clientName = "$mode-client"
        $hostGame = Start-Check $hostName $extra
        $owned += $hostGame
        $deadline = (Get-Date).AddSeconds(10)
        while (-not (Select-String -Path "$evidence/$hostName.stdout.log" -SimpleMatch 'COMPOSITION_HOST_READY')) {
            if ($hostGame.HasExited -or (Get-Date) -ge $deadline) { throw 'Host listener readiness failed.' }
            Start-Sleep -Milliseconds 100
        }
        $stage = "$mode-rpc"
        $extra = @('res://factory/sandbox/shell/round_composition/probe.tscn')
        if ($mode -eq 'empty') { $extra += @('--','--empty-content') }
        $clientGame = Start-Check $clientName $extra
        $owned += $clientGame
        Assert-Check $hostGame $hostName 35 'ROUND_COMPOSITION_PASS role=host'
        Assert-Check $clientGame $clientName 10 'ROUND_COMPOSITION_PASS role=client'
        if (-not (Select-String -Path "$evidence/$hostName.stdout.log" -SimpleMatch 'assertion=configuration_and_explicit_empty')) { throw 'Missing fail-closed configuration checks.' }
        foreach ($round in 1..2) {
            if (-not (Select-String -Path "$evidence/$hostName.stdout.log" -SimpleMatch "COMPOSITION_PASS round=$round")) { throw "$mode missing host round $round." }
            if (-not (Select-String -Path "$evidence/$clientName.stdout.log" -SimpleMatch "COMPOSITION_CLIENT_OBSERVED round=$round")) { throw "$mode missing client round $round." }
        }
        if ($mode -eq 'custom' -and -not (Select-String -Path "$evidence/$hostName.stdout.log" -SimpleMatch 'assertion=terminal_stop_local_cleanup')) { throw 'Missing terminal Stop cleanup check.' }
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
       godot = $Godot; port = 24881; default_timeout_seconds = 55; custom_timeout_seconds = 30 } |
        ConvertTo-Json | Set-Content "$evidence/result.json"
    Write-Output "ROUND_COMPOSITION_RESULT=$result cleanup_live=$remaining profile_removed=$profileClean evidence=$evidence"
    if ($remaining -ne 0 -or -not $profileClean) { throw 'Process/profile cleanup incomplete.' }
}
