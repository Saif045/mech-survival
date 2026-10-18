[CmdletBinding()]
param(
    [string]$Label = (Get-Date -Format 'yyyyMMdd_HHmmss'),
    [string]$Godot = 'D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
while (-not (Test-Path -LiteralPath (Join-Path $repo 'project.godot'))) {
    $parent = Split-Path -Parent $repo
    if (-not $parent -or $parent -eq $repo) { throw 'Cannot find repository project.godot.' }
    $repo = $parent
}
if ($Label -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Label must contain only letters, numbers, underscores or hyphens.' }
$evidence = Join-Path $repo "factory/artifacts/online_gameplay_routing/$Label"
if (Test-Path -LiteralPath $evidence) { throw "Use a new label; evidence already exists: $evidence" }
New-Item -ItemType Directory -Path $evidence | Out-Null
$profileRoot = [IO.Path]::GetFullPath((Join-Path $evidence 'profile'))
$oldAppData = $env:APPDATA
$oldLocalAppData = $env:LOCALAPPDATA
$owned = @()
$result = 'BLOCKED'
$stage = 'preflight'
try {
    if (-not (Test-Path -LiteralPath $Godot)) { throw 'Godot executable is unavailable.' }
    $assembly = Join-Path $repo '.godot/mono/temp/bin/Debug/GameFactory.dll'
    if (-not (Test-Path -LiteralPath $assembly)) { throw 'Build GameFactory before running this probe.' }
    Get-FileHash -LiteralPath $assembly | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$evidence/build.json"
    git -C $repo diff -- factory/shell/GameShell.cs factory/shell/main_menu.gd | Set-Content "$evidence/tested-runtime.diff"
    $env:APPDATA = Join-Path $profileRoot 'Roaming'
    $env:LOCALAPPDATA = Join-Path $profileRoot 'Local'
    New-Item -ItemType Directory -Path $env:APPDATA,$env:LOCALAPPDATA | Out-Null
    $result = 'FAIL'
    # Import precedes the real SceneLoader routes. No Steam sessions or VM
    # processes are started. Each scene wait is bounded to 5 seconds in C#.
    foreach ($check in @(
        @{ Name = 'import'; Args = @('--editor', '--quit'); Timeout = 60 },
        @{ Name = 'routing'; Args = @('res://factory/sandbox/shell/online_gameplay_routing_probe.tscn', '--', '+connect_lobby', '303'); Timeout = 30 }
    )) {
        $stage = $check.Name
        $arguments = @('--headless', '--path', $repo, '--log-file', "$evidence/$stage.engine.log") + $check.Args
        $game = Start-Process -FilePath $Godot -ArgumentList $arguments -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence/$stage.stdout.log" -RedirectStandardError "$evidence/$stage.stderr.log"
        $owned += $game
        if (-not $game.WaitForExit($check.Timeout * 1000)) { throw "$stage timed out after $($check.Timeout) seconds." }
        $game.Refresh()
        if ($game.ExitCode -ne 0) { throw "$stage exited with $($game.ExitCode)." }
        $errors = @(Select-String -Path "$evidence/$stage.stdout.log","$evidence/$stage.stderr.log" -Pattern '(^|\s)(ERROR:|SCRIPT ERROR:)|ROUTING_FAIL:')
        if ($errors.Count -gt 0) { throw "$stage logged errors; inspect preserved logs." }
    }
    foreach ($route in @('default_and_fallback','host','direct_join','invite_join','command_line_join','invite_pending_rejection')) {
        $matches = @(Select-String -Path "$evidence/routing.stdout.log" -Pattern "^ROUTING_PASS route=$route( |$)")
        if ($matches.Count -ne 1) { throw "Expected exactly one pass for $route." }
    }
    if (-not (Select-String -Path "$evidence/routing.stdout.log" -SimpleMatch 'ROUTING_CLEANUP settings_restored=true loads=4')) { throw 'Runtime settings/loader cleanup did not complete.' }
    $result = 'PASS'
} finally {
    foreach ($game in $owned) {
        if (Get-Process -Id $game.Id -ErrorAction SilentlyContinue) {
            Stop-Process -Id $game.Id -Force
            [void]$game.WaitForExit(5000)
        }
    }
    $remaining = @($owned | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue }).Count
    $env:APPDATA = $oldAppData
    $env:LOCALAPPDATA = $oldLocalAppData
    # Verify the absolute recursive-delete target stays under this run's evidence.
    if ($remaining -eq 0 -and $profileRoot.StartsWith([IO.Path]::GetFullPath($evidence) + [IO.Path]::DirectorySeparatorChar)) {
        if (Test-Path -LiteralPath $profileRoot) { Remove-Item -LiteralPath $profileRoot -Recurse -Force }
    }
    $profileClean = -not (Test-Path -LiteralPath $profileRoot)
    @{ result = $result; stage = $stage; cleanup_processes = $remaining; profile_removed = $profileClean;
       pids = @($owned.Id); vm = 'not used'; source_revision = (& git -C $repo rev-parse HEAD) } |
        ConvertTo-Json | Set-Content "$evidence/result.json"
    Write-Output "ROUTING_RESULT=$result cleanup_live=$remaining profile_removed=$profileClean evidence=$evidence"
    if ($remaining -ne 0 -or -not $profileClean) { throw 'Process/profile cleanup did not complete.' }
}
