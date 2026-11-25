[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsixDir,
    [string]$BaseUri = 'https://taskbarwaraz.blob.core.windows.net/downloads/'
)
# Envia o msix e o .appinstaller para o Azure Blob se SIDEWAR_BLOB_SAS existir.
# O token NUNCA e gravado nem impresso: nada aqui pode escrever a URL com a query.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'
$packages = @(Get-ChildItem -LiteralPath $MsixDir -File -Filter 'SideWar-*.msix')
if ($packages.Count -ne 1) { throw "Esperado exatamente um SideWar-*.msix em $MsixDir; encontrados $($packages.Count)." }
$msix = $packages[0]
$appInstaller = Get-Item -LiteralPath (Join-Path $MsixDir 'SideWar.appinstaller')

$sas = "$env:SIDEWAR_BLOB_SAS".Trim().Trim('"').TrimStart('?')
if (-not $sas) {
    Write-Host ''
    Write-Host 'SIDEWAR_BLOB_SAS nao definida: envio pulado. Para publicar manualmente (Portal do Azure,'
    Write-Host 'conta taskbarwaraz, container downloads, botao Carregar, sobrescrever se ja existir):'
    Write-Host "  1o) $($msix.FullName)"
    Write-Host '      Content-Type: application/msix'
    Write-Host "  2o) $($appInstaller.FullName)   (sempre por ultimo)"
    Write-Host '      Content-Type: application/appinstaller ; Cache-Control: no-cache'
    Write-Host 'Para o envio automatico, veja docs/atualizacao-automatica.md.'
    return
}

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Send-Blob([IO.FileInfo]$File, [string]$ContentType, [hashtable]$Extra) {
    $headers = @{ 'x-ms-blob-type' = 'BlockBlob' }
    foreach ($key in $Extra.Keys) { $headers[$key] = $Extra[$key] }
    Write-Host "Enviando $($File.Name) ($([Math]::Round($File.Length / 1MB, 1)) MB)..."
    try {
        Invoke-WebRequest -Uri ($BaseUri + $File.Name + '?' + $sas) -Method Put -InFile $File.FullName -ContentType $ContentType -Headers $headers -UseBasicParsing | Out-Null
    } catch {
        # So o status HTTP: a mensagem original pode conter a URL com o token.
        $status = 'sem resposta HTTP (rede ou token mal formado)'
        $e = $_.Exception
        if ($e.PSObject.Properties['Response'] -and $e.Response) { $status = "HTTP $([int]$e.Response.StatusCode)" }
        throw "Falha ao enviar $($File.Name): $status. Confira a validade e as permissoes do SAS."
    }
    Write-Host "Enviado: $($BaseUri + $File.Name)"
}

# O appinstaller vai por ULTIMO: so aponta para o msix depois que ele ja esta no ar.
Send-Blob $msix 'application/msix' @{}
Send-Blob $appInstaller 'application/appinstaller' @{ 'Cache-Control' = 'no-cache'; 'x-ms-blob-cache-control' = 'no-cache' }
Write-Host "Publicado: $($BaseUri)SideWar.appinstaller"
