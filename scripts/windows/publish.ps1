param(
    # Versão do ZIP. RF-18.1 (Directory.Build.props) ainda não existe: fonte única pendente (ver specs/evidencias/T5.10.md).
    [string]$Version = '0.1.0',
    # Hash curto do git vai para -p:SourceRevisionId. O espelho do Windows não tem .git, então o WSL pode passá-lo.
    [string]$SourceRevisionId = ''
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# WSL interop may inherit a PATH captured before SDK installation.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
Set-Location (Join-Path $PSScriptRoot '../..')
$root = (Get-Location).Path
$pwsh = (Get-Process -Id $PID).Path
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source

if ([string]::IsNullOrWhiteSpace($SourceRevisionId)) {
    $git = Get-Command git -ErrorAction SilentlyContinue
    $rev = ''
    if ($git) {
        $rev = (& $git.Source rev-parse --short HEAD 2>$null | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { $rev = '' }
    }
    if ([string]::IsNullOrWhiteSpace($rev)) {
        Write-Warning 'Sem git: SourceRevisionId=nogit. Passe -SourceRevisionId no build de release.'
        $rev = 'nogit'
    }
    $SourceRevisionId = $rev
}

# 1. Build Release (warnings as errors) e testes sem os interativos.
& $pwsh -NoProfile -File (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release
if ($LASTEXITCODE -ne 0) { Write-Error "build.ps1 falhou ($LASTEXITCODE)"; exit $LASTEXITCODE }
& $pwsh -NoProfile -File (Join-Path $PSScriptRoot 'test.ps1')
if ($LASTEXITCODE -ne 0) { Write-Error "test.ps1 falhou ($LASTEXITCODE)"; exit $LASTEXITCODE }

# 2. dotnet publish com o perfil win-x64 (RF-18.2) e a revisão do git (RF-18.1).
$publishDir = Join-Path $root 'artifacts/publish/win-x64'
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
& $dotnet publish 'src/SideWar.App/SideWar.App.csproj' -c Release `
    -p:PublishProfile=win-x64 `
    -p:Version=$Version `
    -p:SourceRevisionId=$SourceRevisionId `
    -p:ContinuousIntegrationBuild=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish falhou ($LASTEXITCODE)"; exit $LASTEXITCODE }
# O assembly é SideWar.App (csproj fora do escopo de T5.10); renomeia para o nome de D-09.
$builtExe = Join-Path $publishDir 'SideWar.App.exe'
if (Test-Path $builtExe) { Rename-Item -LiteralPath $builtExe -NewName 'SideWar.exe' }
$exe = Join-Path $publishDir 'SideWar.exe'
if (-not (Test-Path $exe)) { throw "SideWar.exe ausente em $publishDir" }

# 3. ZIP interno (mínimo): SideWar.exe e README.txt. LICENSE.txt, THIRD-PARTY-NOTICES.txt, SHA256SUMS e assinatura são T8.x/E8.
# Sem portable.flag (RF-18.3).
$staging = Join-Path $root 'artifacts/release/staging'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Path $staging | Out-Null
Copy-Item -LiteralPath $exe -Destination $staging
$readme = @(
    "SideWar $Version (build interno de playtest, revisão $SourceRevisionId)",
    '',
    'Requisitos: Windows 11 22H2 (build 22621) ou superior, x64; GPU com driver WDDM; 150 MB de disco.',
    'Windows 10 nao e suportado oficialmente.',
    '',
    'Instalacao: extraia o ZIP (sugestao: %LOCALAPPDATA%\Programs\SideWar) e execute SideWar.exe.',
    'Dados: %LOCALAPPDATA%\SideWar.',
    'Remocao: apague a pasta do app e %LOCALAPPDATA%\SideWar. Se "iniciar com o Windows" estiver ligado,',
    'desligue-o antes (Configuracoes > Geral) ou apague HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SideWar.',
    '',
    'Build sem assinatura: o SmartScreen pode avisar ao executar.'
)
Set-Content -LiteralPath (Join-Path $staging 'README.txt') -Value $readme -Encoding UTF8

$releaseDir = Join-Path $root 'artifacts/release'
$zip = Join-Path $releaseDir "SideWar-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item -Recurse -Force $staging
Write-Host "ZIP: $zip"
Get-Item -LiteralPath $zip | Select-Object FullName, Length | Format-List
exit 0
