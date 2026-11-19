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
    # Cert publico embutido (base64 do .cer) no proprio .cmd; o zip tem exatamente 3 arquivos.
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $CertificatePath).Path))
    $tpl = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Cert-Chave-Acesso.template.cmd'))
    [IO.File]::WriteAllText((Join-Path $stage 'Cert-Chave-Acesso.cmd'), $tpl.Replace('@@CERT@@', $b64), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LEIA-ME.txt') -Destination $stage
    $zip = Join-Path $MsixDir 'SideWar-teste.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
    Write-Host "Kit de testadores: $zip"
} finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
