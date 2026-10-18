Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
while (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'project.godot'))) {
    $parent = Split-Path -Parent $repoRoot
    if (-not $parent -or $parent -eq $repoRoot) { throw 'Cannot find repository project.godot.' }
    $repoRoot = $parent
}
# Load only the production writer; evaluating run.ps1 would start the harness.
$tokens = $null
$parseErrors = $null
$tree = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path (Split-Path -Parent $PSScriptRoot) 'run.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Harness has PowerShell parse errors.' }
$writer = @($tree.FindAll({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-HostConfig'
}, $false))
if ($writer.Count -ne 1) { throw 'Expected exactly one production Write-HostConfig function.' }
. ([scriptblock]::Create($writer[0].Extent.Text))

$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'factory/artifacts/host_config_checks'))
$fixture = Join-Path $fixtureRoot ([Guid]::NewGuid().ToString('N'))
$artifactDirectory = Join-Path $fixture 'evidence'
$hostConfigPath = Join-Path $fixture 'fresh/host/.runtime/host_config.json'
$outputDirectory = Join-Path $fixture 'export'
$hostExecutable = Join-Path $outputDirectory 'GameFactory.exe'
$hostGodotLogPath = Join-Path $artifactDirectory 'godot.log'
$HostTimeoutSeconds = 120
$manifest = [pscustomobject]@{ build_id = 'host_config_fixture' }
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
try {
    if (Test-Path -LiteralPath (Split-Path -Parent $hostConfigPath)) { throw 'Fixture must start without the runtime directory.' }
    foreach ($label in @('fresh', 'existing')) {
        $arguments = @('--test-run-id=host_config_fixture', "--fixture=$label")
        Write-HostConfig $arguments $manifest 'fixture-hash' 'fixture-roaming' 'fixture-local' 'fixture-stdout' 'fixture-stderr'
        if (-not (Test-Path -LiteralPath (Split-Path -Parent $hostConfigPath) -PathType Container)) { throw 'Writer did not create its parent directory.' }
        $config = Get-Content -LiteralPath $hostConfigPath -Raw | ConvertFrom-Json
        if ($config.expected_build_id -ne $manifest.build_id -or $config.expected_manifest_sha256 -ne 'fixture-hash' -or
            $config.executable -ne $hostExecutable -or $config.working_directory -ne $outputDirectory -or
            $config.arguments[1] -ne "--fixture=$label" -or $config.app_data -ne 'fixture-roaming' -or
            $config.local_app_data -ne 'fixture-local' -or $config.godot_log_path -ne $hostGodotLogPath) {
            throw 'Written host configuration does not match the launch inputs.'
        }
        if (Test-Path -LiteralPath "$hostConfigPath.tmp") { throw 'Temporary config was not consumed by the atomic move.' }
        $evidence = Join-Path $artifactDirectory 'host_config.json'
        if ((Get-FileHash -LiteralPath $hostConfigPath).Hash -ne (Get-FileHash -LiteralPath $evidence).Hash) {
            throw 'Preserved configuration differs from the launch configuration.'
        }
        Write-Host "PASS host configuration: $label directory, complete JSON, atomic replacement and evidence copy"
    }
}
finally {
    if (-not [IO.Path]::GetFullPath($fixture).StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe fixture cleanup path.'
    }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
