[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsixDir,
    [Parameter(Mandatory)][string]$CertificatePath,
    [string]$IdentityName = 'SideWar.Test.Placeholder'
)
# Monta artifacts/msix/SideWar-teste.zip. Inclui SOMENTE o .cer publico; nunca pfx/chave privada.
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
    # Cert publico embutido (base64 do .cer) no proprio .cmd; o zip tem exatamente 6 arquivos.
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $CertificatePath).Path))
    $tpl = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Cert-Chave-Acesso.template.cmd'))
    [IO.File]::WriteAllText((Join-Path $stage 'Cert-Chave-Acesso.cmd'), $tpl.Replace('@@CERT@@', $b64), [Text.UTF8Encoding]::new($false))
    # Desinstalador: nome do pacote + thumbprint do mesmo cert publico.
    $thumb = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path).Thumbprint
    $un = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Desinstalar-SideWar.template.cmd'))
    [IO.File]::WriteAllText((Join-Path $stage 'Desinstalar-SideWar.cmd'), $un.Replace('@@PKG@@', $IdentityName).Replace('@@THUMB@@', $thumb), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LEIA-ME.txt') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\..\..\docs\patch-notes\2026-11-24.md') -Destination (Join-Path $stage 'PATCH-NOTES.txt')
    $zip = Join-Path $MsixDir 'SideWar-teste.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host "Kit de testadores: $zip"
} finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
