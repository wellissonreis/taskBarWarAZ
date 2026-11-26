[CmdletBinding()]
param(
    [ValidateSet('StoreUpload', 'SideLoad')][string]$Mode = 'SideLoad',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$PackageVersion = '0.1.0.0',
    [string]$IdentityName = 'SideWar.Test.Placeholder',
    [string]$Publisher = 'CN=SideWar Test Placeholder',
    [string]$PublisherDisplayName = 'SideWar Test Placeholder',
    # Public .cer for an externally provisioned signing certificate. Its private key must
    # already be available in CurrentUser\My. This script never imports certificates.
    [string]$CertificatePath = '',
    [string]$SourceRevisionId = '',
    # SideLoad: pasta de distribuicao, mantida somente com a versao atual.
    [string]$DistributionPath = 'C:\dev\SideWar-Pacotes',
    # Runs the existing build.ps1/test.ps1 (Release) first; skip only for a deliberate quick repackage.
    [switch]$SkipChecks
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $root
$parts = $PackageVersion.Split('.') | ForEach-Object { [int64]$_ }
if (@($parts | Where-Object { $_ -gt 65535 }).Count -gt 0) { throw 'Cada componente de PackageVersion deve estar entre 0 e 65535.' }
if ($parts[3] -ne 0) { throw 'PackageVersion exige o quarto componente igual a zero.' }
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'IdentityName inválido.' }
if ([string]::IsNullOrWhiteSpace($Publisher) -or [string]::IsNullOrWhiteSpace($PublisherDisplayName)) { throw 'Publisher e PublisherDisplayName são obrigatórios.' }
if ($Mode -eq 'StoreUpload' -and ($IdentityName -match '(?i)placeholder|sidewar\.test' -or $Publisher -match '(?i)placeholder' -or $PublisherDisplayName -match '(?i)placeholder')) { throw 'StoreUpload requer identidade real do Partner Center, não placeholders de teste.' }
if ($Mode -eq 'SideLoad' -and ([string]::IsNullOrWhiteSpace($CertificatePath) -or -not (Test-Path -LiteralPath $CertificatePath -PathType Leaf))) { throw 'SideLoad requer -CertificatePath para certificado público externo (.cer), com chave privada em CurrentUser\My.' }

if (-not $SkipChecks) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Release
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 falhou ($LASTEXITCODE); use -SkipChecks apenas para pular de propósito." }
    & (Join-Path $PSScriptRoot 'test.ps1')
    if ($LASTEXITCODE -ne 0) { throw "test.ps1 falhou ($LASTEXITCODE)." }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'vswhere.exe ausente; instale Visual Studio/Build Tools com ferramentas de empacotamento MSIX.' }
$msbuild = (& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)
if (-not $msbuild) { throw 'MSBuild.exe não encontrado pelo vswhere.' }
$vsRoot = (& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath | Select-Object -First 1)
$bridge = Join-Path $vsRoot 'MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.targets'
if (-not (Test-Path -LiteralPath $bridge)) { throw "Windows Application Packaging Project ausente: $bridge. Instale as ferramentas MSIX do Visual Studio." }
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdk = @(Get-ChildItem -Path $sdkBin -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.19041.0' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) -and (Test-Path (Join-Path $_.FullName 'x64\signtool.exe')) } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1)
if ($sdk.Count -eq 0) { throw 'Windows SDK >= 10.0.19041 com MakeAppx e SignTool não encontrado.' }
if ($Mode -eq 'SideLoad') {
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path)
    if ($cert.Subject -ne $Publisher) { throw "Subject do certificado não corresponde ao Publisher: $($cert.Subject)" }
    $installed = Get-Item -LiteralPath ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -ErrorAction SilentlyContinue
    if (-not $installed -or -not $installed.HasPrivateKey) { throw 'Certificado com chave privada não encontrado em CurrentUser\My.' }
}
# A reserva persistente evita versões repetidas mesmo no mesmo segundo, após falhas
# de build ou quando o relógio volta. O estado fica fora do espelho sincronizado.
$stateDir = Join-Path $env:LOCALAPPDATA 'SideWar\MsixVersion'
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$statePath = Join-Path $stateDir 'last-version.txt'
$mutex = [Threading.Mutex]::new($false, 'Local\SideWar-MsixVersion')
$locked = $false
try {
    try { $locked = $mutex.WaitOne([TimeSpan]::FromMinutes(5)) }
    catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Tempo esgotado aguardando reserva de versão MSIX.' }
    $last = [int64]0
    if (Test-Path -LiteralPath $statePath) {
        $raw = [IO.File]::ReadAllText($statePath).Trim()
        if (-not [int64]::TryParse($raw, [ref]$last) -or $last -lt 1 -or $last -gt 281474976710655) {
            throw "Estado de versão MSIX inválido: $statePath"
        }
    }
    # Três componentes de 16 bits; a data é contada em segundos desde 2020,
    # partindo de 1.0.0.0. PackageVersion opcional funciona como piso.
    $seconds = [int64][Math]::Floor(([DateTime]::UtcNow - [DateTime]::new(2020, 1, 1, 0, 0, 0, [DateTimeKind]::Utc)).TotalSeconds)
    $floor = $parts[0] * 4294967296L + $parts[1] * 65536L + $parts[2]
    $next = [Math]::Max([Math]::Max(4294967296L + $seconds, $last + 1), $floor)
    if ($next -gt 281474976710655) { throw 'Limite de versões MSIX esgotado.' }
    # Gravar antes do build: uma versão reservada nunca é reutilizada.
    [IO.File]::WriteAllText($statePath, $next.ToString([Globalization.CultureInfo]::InvariantCulture))
    $major = [int64][Math]::Floor($next / 4294967296L)
    $minor = [int64][Math]::Floor(($next % 4294967296L) / 65536L)
    $build = $next % 65536L
    $PackageVersion = "$major.$minor.$build.0"
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

$projectDir = Join-Path $root 'packaging\SideWar.Package'
$output = Join-Path $root 'artifacts\msix'
New-Item -ItemType Directory -Force -Path $output | Out-Null
# Keep the generated manifest outside obj: Rebuild cleans the project's intermediate directory.
$runOutput = Join-Path $output ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runOutput | Out-Null
$manifestPath = Join-Path $runOutput 'SideWar.Generated.appxmanifest'
$manifest = [xml](Get-Content -LiteralPath (Join-Path $projectDir 'Package.appxmanifest') -Raw)
$manifest.Package.Identity.Name = $IdentityName
$manifest.Package.Identity.Publisher = $Publisher
$manifest.Package.Identity.Version = $PackageVersion
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save($manifestPath)
# Dedicated output per invocation prevents a stale artifact being mistaken for this build.
$buildMode = if ($Mode -eq 'StoreUpload') { 'StoreUpload' } else { 'SideloadOnly' }
$argsMsbuild = @((Join-Path $projectDir 'SideWar.Package.wapproj'), '/restore', '/t:Rebuild', '/p:Configuration=Release', '/p:Platform=x64', '/p:RuntimeIdentifier=win-x64', '/p:SelfContained=true', '/p:PublishSingleFile=false', '/p:AppxBundle=Never', '/p:AppxPackageSigningEnabled=false', "/p:UapAppxPackageBuildMode=$buildMode", "/p:PackageManifestPath=$manifestPath", "/p:AppxPackageDir=$runOutput\", '/p:ContinuousIntegrationBuild=true')
if ($SourceRevisionId) {
    if ($SourceRevisionId -notmatch '^[A-Za-z0-9._-]+$') { throw 'SourceRevisionId inválido.' }
    $argsMsbuild += "/p:SourceRevisionId=$SourceRevisionId"
}
Write-Host "MSBuild: $msbuild; Windows SDK: $($sdk[0].Name); modo: $Mode; versão MSIX: $PackageVersion"
& $msbuild @argsMsbuild
if ($LASTEXITCODE -ne 0) { throw "MSBuild falhou ($LASTEXITCODE)." }
$extension = if ($Mode -eq 'StoreUpload') { '*.msixupload' } else { '*.msix' }
$packages = @(Get-ChildItem -LiteralPath $runOutput -Recurse -File -Filter $extension)
if ($packages.Count -ne 1) { throw "Esperado exatamente um $extension em $runOutput; encontrados $($packages.Count)." }
$package = $packages[0]
if ($Mode -eq 'SideLoad') {
    $signtool = Join-Path $sdk[0].FullName 'x64\signtool.exe'
    & $signtool sign /fd SHA256 /s My /sha1 $cert.Thumbprint $package.FullName
    if ($LASTEXITCODE -ne 0) { throw "SignTool falhou ($LASTEXITCODE)." }
    # Self-signed test certs never chain to a trusted root, so `signtool verify /pa` is not usable here.
    # Verify the signer thumbprint instead. To INSTALL the test MSIX, import the .cer as admin:
    #   Import-Certificate -FilePath <cert>.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
    $sig = Get-AuthenticodeSignature -LiteralPath $package.FullName
    if (-not $sig.SignerCertificate -or $sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw "Assinatura ausente ou thumbprint diferente do certificado informado (status: $($sig.Status))." }
    if ($sig.Status -notin @('Valid', 'UnknownError', 'NotTrusted')) { throw "Status de assinatura inesperado: $($sig.Status)." }
    Write-Host "Assinatura verificada: thumbprint $($sig.SignerCertificate.Thumbprint), status $($sig.Status)."
}
# Saida em pasta FIXA: artifacts/msix/. A copia anterior vai para artifacts/msix/historico/<yyyyMMdd-HHmm>/.
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$previous = @(Get-ChildItem -LiteralPath $output -File -ErrorAction SilentlyContinue)
if ($previous.Count -gt 0) {
    $hist = Join-Path (Join-Path $output 'historico') $stamp
    New-Item -ItemType Directory -Force -Path $hist | Out-Null
    $previous | Move-Item -Destination $hist -Force
}
$finalName = if ($Mode -eq 'StoreUpload') { 'SideWar.msixupload' } else { "SideWar-$PackageVersion.msix" }
$final = Join-Path $output $finalName
Copy-Item -LiteralPath $package.FullName -Destination $final -Force
Remove-Item -LiteralPath $runOutput -Recurse -Force -ErrorAction SilentlyContinue
$hash = (Get-FileHash -LiteralPath $final -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash *$finalName`n", [Text.UTF8Encoding]::new($false))
Write-Host "Pacote: $final`nSHA256: $hash"

if ($Mode -eq 'SideLoad') {
    $kitSource = Join-Path $PSScriptRoot 'installer-msix'
    $appInstaller = Join-Path $output 'SideWar.appinstaller'
    & (Join-Path $kitSource 'New-AppInstaller.ps1') -MsixPath $final -OutputPath $appInstaller

    New-Item -ItemType Directory -Force -Path $DistributionPath | Out-Null
    $dist = (Resolve-Path -LiteralPath $DistributionPath).ProviderPath
    Copy-Item -LiteralPath $final -Destination (Join-Path $dist $finalName) -Force
    Copy-Item -LiteralPath $appInstaller -Destination (Join-Path $dist 'SideWar.appinstaller') -Force
    # RawData = somente o certificado publico, mesmo que o arquivo informado tenha mais conteudo.
    $cerName = 'SideWar-Certificado.cer'
    [IO.File]::WriteAllBytes((Join-Path $dist $cerName), $cert.RawData)
    # BOM para o Bloco de Notas mostrar os acentos.
    $readme = [IO.File]::ReadAllText((Join-Path $kitSource 'LEIA-ME-pacote.txt')).Replace('@@MSIX@@', $finalName)
    [IO.File]::WriteAllText((Join-Path $dist 'LEIA-ME.txt'), ($readme -replace '\r*\n', "`r`n"), [Text.UTF8Encoding]::new($true))
    # So depois de tudo gravado: remove msix antigos, o .cmd antigo, outros .cer e zips de kit.
    Get-ChildItem -LiteralPath $dist -File |
        Where-Object { $_.Name -ne $finalName -and $_.Name -ne $cerName -and ($_.Name -like 'SideWar-*.msix' -or $_.Name -like 'SideWar-*-teste.zip' -or $_.Name -eq 'Cert-Chave-Acesso.cmd' -or $_.Extension -eq '.cer') } |
        Remove-Item -Force
    Write-Host "Pasta de distribuicao: $dist"
}
