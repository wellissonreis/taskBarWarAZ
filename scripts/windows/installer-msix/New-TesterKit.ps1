[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsixDir,
    [Parameter(Mandatory)][string]$CertificatePath
)
# Monta artifacts/msix/SideWar-teste.zip. Inclui SOMENTE o .cer publico; nunca pfx/chave privada.
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetExtension($CertificatePath) -ne '.cer') { throw 'O kit aceita apenas o certificado publico (.cer).' }
$msix = Join-Path $MsixDir 'SideWar.msix'
if (-not (Test-Path -LiteralPath $msix)) { throw "SideWar.msix ausente em $MsixDir" }
$stage = Join-Path $env:TEMP ('sidewar-kit-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item -LiteralPath $msix -Destination (Join-Path $stage 'SideWar.msix')
    Copy-Item -LiteralPath $CertificatePath -Destination (Join-Path $stage 'sidewar-test.cer')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Instalar-SideWar.cmd') -Destination $stage
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LEIA-ME.txt') -Destination $stage
    $zip = Join-Path $MsixDir 'SideWar-teste.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host "Kit de testadores: $zip"
} finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
