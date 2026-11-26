[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsixDir,
    [Parameter(Mandatory)][string]$CertificatePath,
    [string]$IdentityName = 'SideWar.Test.Placeholder'
)
# Monta o kit versionado em artifacts/msix/. Inclui SOMENTE o .cer publico; nunca pfx/chave privada.
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetExtension($CertificatePath) -ne '.cer') { throw 'O kit aceita apenas o certificado publico (.cer).' }
$packages = @(Get-ChildItem -LiteralPath $MsixDir -File -Filter 'SideWar-*.msix')
if ($packages.Count -ne 1) { throw "Esperado exatamente um SideWar-*.msix em $MsixDir; encontrados $($packages.Count)." }
$msix = $packages[0]
$stage = Join-Path $env:TEMP ('sidewar-kit-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item -LiteralPath $msix.FullName -Destination (Join-Path $stage $msix.Name)
    # SideWar.appinstaller (atualizacao automatica): fica em artifacts/msix para envio e tambem vai no zip.
    $appInstaller = Join-Path $MsixDir 'SideWar.appinstaller'
    & (Join-Path $PSScriptRoot 'New-AppInstaller.ps1') -MsixPath $msix.FullName -OutputPath $appInstaller
    Copy-Item -LiteralPath $appInstaller -Destination $stage
    # Certificado publico (.cer) para o testador instalar; o zip tem exatamente 6 arquivos:
    # msix, appinstaller, .cer, Desinstalar-SideWar.cmd, LEIA-ME.txt e PATCH-NOTES.txt.
    Copy-Item -LiteralPath $CertificatePath -Destination (Join-Path $stage 'SideWar-Certificado.cer')
    # Desinstalador: nome do pacote + thumbprint do mesmo cert publico.
    $thumb = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path).Thumbprint
    $un = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Desinstalar-SideWar.template.cmd'))
    [IO.File]::WriteAllText((Join-Path $stage 'Desinstalar-SideWar.cmd'), $un.Replace('@@PKG@@', $IdentityName).Replace('@@THUMB@@', $thumb), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LEIA-ME.txt') -Destination $stage
    # Nota mais recente: o nome AAAA-MM-DD.md ordena por data.
    $notes = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\..\..\docs\patch-notes') -File -Filter '????-??-??.md' |
        Sort-Object Name -Descending | Select-Object -First 1
    if (-not $notes) { throw 'Nenhuma nota em docs/patch-notes.' }
    Copy-Item -LiteralPath $notes.FullName -Destination (Join-Path $stage 'PATCH-NOTES.txt')
    $zip = Join-Path $MsixDir ($msix.BaseName + '-teste.zip')
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host "Kit de testadores: $zip"
} finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
