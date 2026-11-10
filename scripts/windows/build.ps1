param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# WSL interop may inherit a PATH captured before SDK installation.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
Set-Location (Join-Path $PSScriptRoot '../..')
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
Write-Host "Windows executable: $dotnet"
Write-Host "Repository: $((Get-Location).Path)"
Write-Host "OS: $([Environment]::OSVersion.VersionString)"
& $dotnet --info
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$sdk = & $dotnet --version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ([version]$sdk -lt [version]'10.0.400') { throw 'SDK 10.0.400 or newer required' }
$extra = @()
if ($Configuration -eq 'Release') { $extra += '-p:ContinuousIntegrationBuild=true' }
& $dotnet build SideWar.slnx -c $Configuration -warnaserror @extra
exit $LASTEXITCODE
