# Instalador/desinstalador simples do SideWar. Sem admin, sem rede, sem telemetria.
# Só mexe em: %LOCALAPPDATA%\Programs\SideWar, atalho do Menu Iniciar e (opcional) atalho da Inicialização.
param(
    [switch]$Uninstall,
    # -Startup yes|no|ask (ask pergunta; yes/no servem para teste não interativo).
    [ValidateSet('ask', 'yes', 'no')][string]$Startup = 'ask',
    # -RemoveData yes|no|ask (apenas na desinstalação).
    [ValidateSet('ask', 'yes', 'no')][string]$RemoveData = 'ask',
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\SideWar'),
    [string]$StartMenuDir = [Environment]::GetFolderPath('Programs'),
    [string]$StartupDir = [Environment]::GetFolderPath('Startup'),
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$menuLink = Join-Path $StartMenuDir 'SideWar.lnk'
$startupLink = Join-Path $StartupDir 'SideWar.lnk'
$dataDir = Join-Path $env:LOCALAPPDATA 'SideWar'

function Ask([string]$question, [string]$mode) {
    if ($mode -eq 'yes') { return $true }
    if ($mode -eq 'no') { return $false }
    $a = Read-Host "$question [s/N]"
    return $a -match '^(s|sim|y|yes)$'
}
function Stop-SideWar {
    Get-Process -Name 'SideWar' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { $_.CloseMainWindow() | Out-Null; if (-not $_.WaitForExit(3000)) { $_.Kill() } }
}
function New-Link([string]$path, [string]$target) {
    $sh = New-Object -ComObject WScript.Shell
    $l = $sh.CreateShortcut($path)
    $l.TargetPath = $target
    $l.WorkingDirectory = Split-Path $target
    $l.Description = 'SideWar'
    $l.Save()
}
function Finish { if (-not $NoPause) { Read-Host 'Pressione Enter para fechar' | Out-Null } }

if ($Uninstall) {
    Write-Host 'Desinstalando o SideWar...'
    Stop-SideWar
    Remove-Item -LiteralPath $menuLink, $startupLink -Force -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $dataDir) -and (Ask "Apagar também seus dados e progresso em $dataDir ?" $RemoveData)) {
        Remove-Item -LiteralPath $dataDir -Recurse -Force
        Write-Host 'Dados apagados.'
    }
    if (Test-Path -LiteralPath $InstallDir) {
        # A pasta contém este script em execução: apaga logo depois que ele terminar.
        $cmd = "ping -n 3 127.0.0.1 >nul & rmdir /s /q `"$InstallDir`""
        Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $cmd -WindowStyle Hidden
    }
    Write-Host 'SideWar removido.'
    Finish
    exit 0
}

if (-not (Test-Path -LiteralPath (Join-Path $here 'SideWar.exe'))) { throw 'SideWar.exe não encontrado ao lado do instalador. Extraia o ZIP inteiro primeiro.' }
Write-Host "Instalando o SideWar em $InstallDir (sem precisar de administrador)..."
Stop-SideWar
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
foreach ($f in 'SideWar.exe', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'README.txt', 'LEIA-ME-AMIGOS.txt', 'Install.ps1', 'Desinstalar.cmd') {
    $src = Join-Path $here $f
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination $InstallDir -Force }
}
New-Item -ItemType Directory -Path $StartMenuDir -Force | Out-Null
New-Link $menuLink (Join-Path $InstallDir 'SideWar.exe')
Write-Host 'Atalho criado no Menu Iniciar.'
if (Ask 'Iniciar o SideWar junto com o Windows?' $Startup) {
    New-Item -ItemType Directory -Path $StartupDir -Force | Out-Null
    New-Link $startupLink (Join-Path $InstallDir 'SideWar.exe')
    Write-Host 'Atalho criado na Inicialização.'
}
Write-Host ''
Write-Host 'Pronto! Abra o SideWar pelo Menu Iniciar. Para remover: execute Desinstalar.cmd (na pasta de instalação).'
Finish
