param([switch]$IncludeInteractive)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# WSL interop may inherit a PATH captured before SDK installation.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
Set-Location (Join-Path $PSScriptRoot '../..')
$projects = @('Core.Tests', 'Runtime.Tests', 'Persistence.Tests', 'Rendering.Tests')
if ($IncludeInteractive) { $projects += @('Platform.Windows.Tests', 'App.SmokeTests') }
foreach ($project in $projects) {
    $extra = @()
    if (-not $IncludeInteractive) { $extra += @('--filter', 'Env!=WindowsInteractive') }
    & dotnet.exe test "tests/SideWar.$project" -c Release -p:ContinuousIntegrationBuild=true --logger "trx;LogFileName=$project.trx" --results-directory artifacts/test-results/windows @extra
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
exit 0
