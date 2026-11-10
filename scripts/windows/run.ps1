param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs = @()
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# WSL interop may inherit a PATH captured before SDK installation.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
Set-Location (Join-Path $PSScriptRoot '../..')
if ($AppArgs.Count -gt 0 -and $AppArgs[0] -eq '--') { $AppArgs = $AppArgs | Select-Object -Skip 1 }
& dotnet.exe run --project src/SideWar.App -c $Configuration -- @AppArgs
exit $LASTEXITCODE
