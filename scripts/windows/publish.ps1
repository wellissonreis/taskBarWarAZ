param(
    # Versão do ZIP; vazio = <Version> de Directory.Build.props (RF-18.1, fonte única).
    [string]$Version = '',
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

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = ([xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($Version)) { throw 'Version ausente em Directory.Build.props' }
}

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

# 3. Pasta do pacote (RF-18.3): SideWar.exe + LICENSE.txt + THIRD-PARTY-NOTICES.txt + README.txt da raiz.
# Sem .pdb (libSkiaSharp.pdb fica fora) e sem portable.flag.
$releaseDir = Join-Path $root 'artifacts/release'
$staging = Join-Path $releaseDir 'staging'
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -LiteralPath $exe -Destination $staging
foreach ($f in 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt', 'README.txt') {
    $src = Join-Path $root $f
    if (-not (Test-Path -LiteralPath $src)) { throw "$f ausente na raiz do repositório" }
    Copy-Item -LiteralPath $src -Destination $staging
}
$pdbs = @(Get-ChildItem -LiteralPath $staging -Recurse -Filter '*.pdb')
if ($pdbs.Count -gt 0) { throw 'pasta do pacote contém .pdb' }

# 4. ZIP nomeado com a versão (entradas na raiz do ZIP).
$zip = Join-Path $releaseDir "SideWar-$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

# 5. SHA256SUMS.txt (formato sha256sum: "<hash> *<arquivo>"), cobre o ZIP e os arquivos do pacote.
$sums = New-Object System.Collections.Generic.List[string]
$sums.Add(('{0} *{1}' -f (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $zip -Leaf)))
foreach ($f in Get-ChildItem -LiteralPath $staging -File | Sort-Object Name) {
    $sums.Add(('{0} *{1}' -f (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $f.Name))
}
$sumsPath = Join-Path $releaseDir 'SHA256SUMS.txt'
[System.IO.File]::WriteAllText($sumsPath, (($sums -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
Remove-Item -Recurse -Force $staging

# 6. Tamanho do ZIP vs RNF-18.1 (meta de 90 MB, informativa).
$zipLen = (Get-Item -LiteralPath $zip).Length
$limit = 90MB
$status = if ($zipLen -le $limit) { 'dentro da meta' } else { 'ACIMA da meta' }
Write-Host ("ZIP: {0}" -f $zip)
Write-Host ("Tamanho: {0} bytes ({1:N1} MB); RNF-18.1 <= 90 MB: {2}" -f $zipLen, ($zipLen / 1MB), $status)
Write-Host ("SHA256SUMS: {0}" -f $sumsPath)
Get-Content -LiteralPath $sumsPath | ForEach-Object { Write-Host $_ }

# 7. Verificação mínima: extrai e roda só --version (sem UI, CreateNoWindow). O --bench interativo é T8.4.
$verify = Join-Path ([IO.Path]::GetTempPath()) ("sidewar-verify-" + [Guid]::NewGuid().ToString('N'))
try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $verify)
    $psi = New-Object System.Diagnostics.ProcessStartInfo (Join-Path $verify 'SideWar.exe')
    $psi.Arguments = '--version'
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $out = $p.StandardOutput.ReadToEnd().Trim()
    if (-not $p.WaitForExit(60000)) { $p.Kill(); throw 'SideWar.exe --version excedeu 60 s' }
    if ($p.ExitCode -ne 0) { throw "SideWar.exe --version saiu com $($p.ExitCode)" }
    if (-not $out.StartsWith($Version)) { throw "--version retornou '$out', esperado prefixo $Version" }
    Write-Host "--version: $out"
}
finally {
    if (Test-Path $verify) { Remove-Item -Recurse -Force $verify -ErrorAction SilentlyContinue }
}
exit 0
