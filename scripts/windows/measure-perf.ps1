param([Parameter(Mandatory = $true)][string]$Scenario, [ValidateRange(1, 86400)][int]$Seconds = 60)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# WSL interop may inherit a PATH captured before SDK installation.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
Set-Location (Join-Path $PSScriptRoot '../..')
throw 'E0 skeleton: performance measurement belongs to E1 (SDD-16/17); no metrics collected.'
