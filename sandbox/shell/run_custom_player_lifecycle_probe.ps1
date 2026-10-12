[CmdletBinding()]
param(
    [string]$Label = (Get-Date -Format 'yyyyMMdd_HHmmss'),
    [string]$Godot = 'D:\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = Join-Path $repo "artifacts/custom_player_lifecycle/$Label"
if (Test-Path -LiteralPath $evidence) { throw "Use a new label; evidence already exists: $evidence" }
if (-not (Test-Path -LiteralPath $Godot)) { throw 'Godot executable is unavailable.' }
New-Item -ItemType Directory -Path $evidence | Out-Null
$owned = @()
$result = 'FAIL'
$stage = 'import'
try {
    # Import registers the saved fixture UID. Then run the custom contract and
    # the existing default-player populated-round regression, in that order.
    foreach ($check in @(
        @{ Name = 'import'; Args = @('--editor', '--quit'); Timeout = 60; Marker = $null },
        @{ Name = 'custom'; Args = @('--', '--run=custom-player-lifecycle'); Timeout = 30; Marker = 'CUSTOM_PLAYER_LIFECYCLE_PASS round=' },
        @{ Name = 'default'; Args = @('--', '--run=round-reuse'); Timeout = 30; Marker = 'ROUND_REUSE_PASS round=' }
    )) {
        $stage = $check.Name
        $arguments = @('--headless', '--path', $repo, '--log-file', "$evidence/$stage.engine.log") + $check.Args
        $game = Start-Process -FilePath $Godot -ArgumentList $arguments -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput "$evidence/$stage.stdout.log" -RedirectStandardError "$evidence/$stage.stderr.log"
        $owned += $game
        if (-not $game.WaitForExit($check.Timeout * 1000)) { throw "$stage timed out after $($check.Timeout) seconds." }
        $game.Refresh()
        if ($game.ExitCode -ne 0) { throw "$stage exited with $($game.ExitCode)." }
        $errors = @(Select-String -Path "$evidence/$stage.stdout.log", "$evidence/$stage.stderr.log" -Pattern '(^|\s)(ERROR:|SCRIPT ERROR:)|_FAIL:')
        if ($errors.Count -gt 0) { throw "$stage logged runtime errors; inspect preserved logs." }
        if ($check.Marker) {
            $passes = @(Select-String -Path "$evidence/$stage.stdout.log" -SimpleMatch $check.Marker)
            if ($passes.Count -ne 3) { throw "$stage expected three passed rounds, got $($passes.Count)." }
        }
        Write-Output "CUSTOM_PLAYER_CHECK_PASS=$stage"
    }
    $result = 'PASS'
} finally {
    foreach ($game in $owned) {
        if (Get-Process -Id $game.Id -ErrorAction SilentlyContinue) { Stop-Process -Id $game.Id -Force }
    }
    $remaining = @($owned | Where-Object { Get-Process -Id $_.Id -ErrorAction SilentlyContinue }).Count
    @{ result = $result; stage = $stage; cleanup_processes = $remaining; pids = @($owned.Id);
       vm = 'not used'; source_revision = (& git -C $repo rev-parse HEAD); godot = $Godot } |
        ConvertTo-Json | Set-Content -LiteralPath "$evidence/result.json"
    Write-Output "CUSTOM_PLAYER_RESULT=$result cleanup_live=$remaining evidence=$evidence"
    if ($remaining -ne 0) { throw 'Test-owned Godot processes remain alive.' }
}
