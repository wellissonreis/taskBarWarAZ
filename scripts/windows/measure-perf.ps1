<#
.SYNOPSIS
  Mede desempenho dos cenarios --bench do SideWar (SDD-16 RF-16.3): machine.json, CSV por execucao e summary.json.
.PARAMETER Scenario
  Nome de um cenario implementado (e1-synthetic, hidden, idle-static, baseline-wpf) ou 'e1' para os quatro.
.PARAMETER Seconds
  Duracao de cada execucao do App (inclui o aquecimento). Deve ser maior que -WarmupSeconds.
.PARAMETER Repeats
  Numero de repeticoes (N) de cada cenario.
.PARAMETER DwmRestSeconds
  Duracao da medicao do DWM em repouso (spec: 60; 0 = nao medir, delta do DWM fica 'nao medido').
#>
param(
    [Parameter(Mandatory = $true)][string]$Scenario,
    [ValidateRange(2, 86400)][int]$Seconds = 60,
    [ValidateRange(1, 100)][int]$Repeats = 1,
    [ValidateRange(0, 3600)][int]$DwmRestSeconds = 60,
    [ValidateRange(0, 600)][int]$WarmupSeconds = 10,
    [string]$Exe,
    [string]$Out,
    [switch]$NoInput,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# WSL interop may inherit a PATH captured before SDK installation.
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') + ';' + $env:Path
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location $repo
$inv = [Globalization.CultureInfo]::InvariantCulture
if ($Seconds -le $WarmupSeconds) { throw "-Seconds ($Seconds) must be greater than -WarmupSeconds ($WarmupSeconds)." }

$implemented = @('e1-synthetic', 'hidden', 'idle-static', 'baseline-wpf')
$scenarios = if ($Scenario -eq 'e1') { $implemented } else { @($Scenario) }
foreach ($s in $scenarios) { if ($s -notin $implemented) { throw "Unknown scenario '$s'. Available: $($implemented -join ', '), e1." } }

if (-not $Exe) {
    $Exe = Join-Path $repo "src/SideWar.App/bin/$Configuration/net10.0-windows/SideWar.App.exe"
    if (-not (Test-Path $Exe)) {
        $found = Get-ChildItem (Join-Path $repo "src/SideWar.App/bin/$Configuration") -Filter SideWar.App.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { $Exe = $found.FullName }
    }
}
if (-not (Test-Path $Exe)) { throw "App not found: $Exe. Run scripts/windows/build.ps1 -Configuration $Configuration or pass -Exe." }
$Exe = (Resolve-Path $Exe).Path
if (-not $Out) { $Out = Join-Path $repo ('artifacts/perf/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Scenario) }
New-Item -ItemType Directory -Force -Path $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$cores = [Environment]::ProcessorCount

# ---------- helpers ----------
function Get-Percentile([double[]]$Values, [double]$P) {
    if ($null -eq $Values -or $Values.Count -eq 0) { return $null }
    $sorted = $Values | Sort-Object
    $rank = [Math]::Max(1, [Math]::Ceiling($P / 100.0 * $sorted.Count))
    return [double]$sorted[[int]$rank - 1]
}
function Get-Stats($Values) {
    $v = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
    if ($v.Count -eq 0) { return $null }
    $avg = ($v | Measure-Object -Average).Average
    $max = ($v | Measure-Object -Maximum).Maximum
    return [ordered]@{ mean = [Math]::Round($avg, 4); p50 = [Math]::Round((Get-Percentile $v 50), 4); p95 = [Math]::Round((Get-Percentile $v 95), 4); max = [Math]::Round($max, 4); n = $v.Count }
}
$script:gpuAvailable = $true
function Read-Gpu {
    # GPU Engine via CIM (nomes de classe/propriedade em ingles: independe do idioma do Windows, ao contrario de Get-Counter).
    # Soma '...pid_<pid>_...' por pid. Retorna $null quando indisponivel (registrar "indisponivel").
    if (-not $script:gpuAvailable) { return $null }
    try {
        $rows = Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine -ErrorAction Stop
        $map = @{}
        foreach ($r in $rows) {
            if ($r.Name -match 'pid_(\d+)_') {
                $p = [int]$Matches[1]
                if (-not $map.ContainsKey($p)) { $map[$p] = 0.0 }
                $map[$p] += [double]$r.UtilizationPercentage
            }
        }
        return $map
    } catch {
        $script:gpuAvailable = $false
        Write-Warning "GPU Engine counters unavailable: $($_.Exception.Message)"
        return $null
    }
}
function Get-DwmProcess { Get-Process -Name dwm -ErrorAction SilentlyContinue | Select-Object -First 1 }
# dwm.exe e protegido: TotalProcessorTime nega acesso sem elevacao; usa o contador bruto via CIM (ticks de 100 ns).
function Read-DwmRaw([int]$ProcId) {
    try {
        $r = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$ProcId" -ErrorAction Stop
        if ($r) { return [pscustomobject]@{ ticks = [double]$r.PercentProcessorTime; ts = [double]$r.Timestamp_Sys100NS } }
    } catch { }
    return $null
}

# Amostra a cada 1 s: CPU (% da maquina toda) do processo $Proc e do dwm.exe, GPU por pid.
function Invoke-Sampling([System.Diagnostics.Process]$Proc, [int]$Duration) {
    $rows = New-Object System.Collections.Generic.List[object]
    $dwm = Get-DwmProcess
    $prevT = [Diagnostics.Stopwatch]::StartNew()
    $prevProc = if ($Proc) { $Proc.TotalProcessorTime.TotalSeconds } else { 0.0 }
    $prevDwm = if ($dwm) { Read-DwmRaw $dwm.Id } else { $null }
    $total = [Diagnostics.Stopwatch]::StartNew()
    $n = 0
    while ($total.Elapsed.TotalSeconds -lt $Duration) {
        $n++
        $left = $n - $total.Elapsed.TotalSeconds
        if ($left -gt 0) { Start-Sleep -Milliseconds ([int]($left * 1000)) }
        $gpu = Read-Gpu
        $dt = $prevT.Elapsed.TotalSeconds; $prevT.Restart()
        $procCpu = $null; $procPid = 0
        if ($Proc) {
            try {
                $Proc.Refresh()
                if ($Proc.HasExited) { break }
                $cur = $Proc.TotalProcessorTime.TotalSeconds
                $procCpu = 100.0 * ($cur - $prevProc) / ($dt * $cores); $prevProc = $cur; $procPid = $Proc.Id
            } catch { break }
        }
        $dwmCpu = $null
        if ($dwm -and $prevDwm) {
            $cur = Read-DwmRaw $dwm.Id
            if ($cur -and $cur.ts -gt $prevDwm.ts) { $dwmCpu = 100.0 * ($cur.ticks - $prevDwm.ticks) / ($cur.ts - $prevDwm.ts) / $cores; $prevDwm = $cur }
        }
        $gpuProc = $null; $gpuDwm = $null
        if ($null -ne $gpu) {
            $gpuProc = if ($procPid -and $gpu.ContainsKey($procPid)) { $gpu[$procPid] } elseif ($procPid) { 0.0 } else { $null }
            $gpuDwm = if ($dwm -and $gpu.ContainsKey($dwm.Id)) { $gpu[$dwm.Id] } else { 0.0 }
        }
        $rows.Add([pscustomobject]@{ t = [Math]::Round($total.Elapsed.TotalSeconds, 2); cpuProc = $procCpu; cpuDwm = $dwmCpu; gpuProc = $gpuProc; gpuDwm = $gpuDwm })
    }
    return , $rows.ToArray()
}

# ---------- 1. machine.json (coletado, nao declarado: D-05 pendente) ----------
function Get-MachineInfo {
    $cpu = @(Get-CimInstance Win32_Processor)
    $gpus = @(Get-CimInstance Win32_VideoController)
    $os = Get-CimInstance Win32_OperatingSystem
    $cs = Get-CimInstance Win32_ComputerSystem
    $reg = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $ubr = if ($reg.PSObject.Properties['UBR']) { $reg.UBR } else { $null }
    $monitors = @()
    $dpi = $null
    try {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
        foreach ($s in [Windows.Forms.Screen]::AllScreens) {
            $monitors += [ordered]@{ device = $s.DeviceName; primary = $s.Primary; bounds = "$($s.Bounds.Width)x$($s.Bounds.Height)@$($s.Bounds.X),$($s.Bounds.Y)"; workArea = "$($s.WorkingArea.Width)x$($s.WorkingArea.Height)" }
        }
    } catch { }
    $diag = $null
    try { $raw = & $Exe --diag 2>$null; if ($LASTEXITCODE -eq 0) { $diag = ($raw -join "`n") | ConvertFrom-Json } } catch { }
    try {
        $dpi = (Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -ErrorAction Stop).AppliedDPI
    } catch { }
    $refresh = @($gpus | ForEach-Object { $_.CurrentRefreshRate } | Where-Object { $_ })
    [ordered]@{
        collectedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        source = 'collected-automatically (D-05 not answered: not declared by the user)'
        computerName = $null
        cpu = @($cpu | ForEach-Object { [ordered]@{ name = $_.Name.Trim(); cores = $_.NumberOfCores; logicalProcessors = $_.NumberOfLogicalProcessors; maxClockMHz = $_.MaxClockSpeed } })
        logicalCores = $cores
        ramGB = [Math]::Round($cs.TotalPhysicalMemory / 1GB, 2)
        gpu = @($gpus | ForEach-Object { [ordered]@{ name = $_.Name; driverVersion = $_.DriverVersion; adapterRamMB = if ($_.AdapterRAM) { [Math]::Round($_.AdapterRAM / 1MB) } else { $null }; refreshHz = $_.CurrentRefreshRate; currentResolution = "$($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution)" } })
        windows = [ordered]@{ caption = $os.Caption; version = $os.Version; buildNumber = $os.BuildNumber; ubr = $ubr; displayVersion = if ($reg.PSObject.Properties['DisplayVersion']) { $reg.DisplayVersion } else { $null } }
        monitors = $monitors
        appliedDpi = $dpi
        appDiag = $diag
        powerPlan = ((& powercfg.exe /getactivescheme 2>$null) -join ' ').Trim()
        powerShell = $PSVersionTable.PSVersion.ToString()
        exe = $Exe
        exeVersion = try { ((& $Exe --version 2>$null) -join ' ').Trim() } catch { $null }
    }
}
Write-Host '== machine.json'
$machine = Get-MachineInfo
$machine | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $Out 'machine.json') -Encoding utf8

# ---------- 2. DWM em repouso ----------
$dwmRest = $null
if ($DwmRestSeconds -gt 0) {
    Write-Host "== DWM em repouso ($DwmRestSeconds s, overlay nao iniciado)"
    $restRows = Invoke-Sampling $null $DwmRestSeconds
    $dwmRest = [ordered]@{ seconds = $DwmRestSeconds; cpuDwm = Get-Stats ($restRows | ForEach-Object cpuDwm); gpuDwm = Get-Stats ($restRows | ForEach-Object gpuDwm) }
    $restRows | Export-Csv -Path (Join-Path $Out 'dwm-rest.csv') -Encoding utf8
}

# ---------- 3. execucoes ----------
function Invoke-Run([string]$Name, [int]$Index) {
    $tag = "$Name-r$Index"
    $csv = Join-Path $Out "bench-$tag.csv"
    $log = Join-Path $Out "bench-$tag.log"
    $argList = @('--bench', $Name, '--seconds', $Seconds, '--out', $csv)
    if ($NoInput) { $argList += '--no-input' }
    Write-Host "== $tag : $Seconds s (aquecimento descartado: $WarmupSeconds s)"
    $proc = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err" -WindowStyle Hidden
    $samples = @()
    $exit = $null
    try {
        $samples = Invoke-Sampling $proc ($Seconds + 5)
        if (-not $proc.WaitForExit(30000)) { throw "App did not exit within 30 s after the run ($tag)." }
        $exit = $proc.ExitCode
    } finally {
        # Sempre encerra o App (T1.13): nenhum processo SideWar.App pode sobrar.
        try { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } } catch { }
    }
    if ($exit -ne 0) { throw "App exit code $exit in $tag (see $log)." }
    $samples | Export-Csv -Path (Join-Path $Out "samples-$tag.csv") -Encoding utf8
    $bench = @(Import-Csv -Path $csv | Where-Object { [double]::Parse($_.t, $inv) -gt $WarmupSeconds })
    $cpuRows = @($samples | Where-Object { $_.t -gt $WarmupSeconds })
    return [pscustomobject]@{ tag = $tag; exit = $exit; bench = $bench; cpu = $cpuRows }
}

function Num($rows, [string]$col) { @($rows | ForEach-Object { $v = $_.$col; if ($null -ne $v -and "$v" -ne '') { [double]::Parse("$v", $inv) } }) }

$results = [ordered]@{}
try {
    foreach ($s in $scenarios) {
        $runs = @(); for ($i = 1; $i -le $Repeats; $i++) { $runs += Invoke-Run $s $i }
        $allBench = @($runs | ForEach-Object { $_.bench })
        $allCpu = @($runs | ForEach-Object { $_.cpu })
        $dwmMean = (Get-Stats ($allCpu | ForEach-Object cpuDwm))
        $dwmGpuMean = (Get-Stats ($allCpu | ForEach-Object gpuDwm))
        $res = [ordered]@{
            runs = $runs.Count; secondsPerRun = $Seconds; warmupDiscarded = $WarmupSeconds
            samplesAfterWarmup = $allBench.Count
            cpuProcPct = Get-Stats ($allCpu | ForEach-Object cpuProc)
            cpuDwmPct = $dwmMean
            gpuProcPct = Get-Stats ($allCpu | ForEach-Object gpuProc)
            gpuDwmPct = $dwmGpuMean
            dwmDelta = $null
            fps = Get-Stats (Num $allBench 'fps')
            wakesPerSec = Get-Stats (Num $allBench 'wakes')
            composeP50Ms = Get-Stats (Num $allBench 'composeP50')
            composeP95Ms = Get-Stats (Num $allBench 'composeP95')
            presentP50Ms = Get-Stats (Num $allBench 'presentP50')
            presentP95Ms = Get-Stats (Num $allBench 'presentP95')
            privateMB = Get-Stats (Num $allBench 'privateMB')
            workingSetMB = Get-Stats (Num $allBench 'workingSetMB')
            handles = Get-Stats (Num $allBench 'handles')
            uiaRetainedMB = Get-Stats (Num $allBench 'uiaRetainedMB')
        }
        if ($dwmRest -and $dwmMean -and $dwmRest.cpuDwm) {
            $res.dwmDelta = [ordered]@{
                cpuPct = [Math]::Round($dwmMean.mean - $dwmRest.cpuDwm.mean, 4)
                gpuPct = if ($dwmGpuMean -and $dwmRest.gpuDwm) { [Math]::Round($dwmGpuMean.mean - $dwmRest.gpuDwm.mean, 4) } else { $null }
            }
        }
        $results[$s] = $res
    }
} finally {
    # Rede de seguranca: nenhum SideWar.App deste exe deve sobrar.
    Get-Process -Name 'SideWar.App' -ErrorAction SilentlyContinue | Where-Object { try { $_.Path -eq $Exe } catch { $false } } | Stop-Process -Force -ErrorAction SilentlyContinue
}

# ---------- 4. summary.json: metas de RF-16.1 ----------
$short = $Seconds -lt 300
function New-Target([string]$Id, [string]$Scenario_, [string]$Desc, $Checks, [string]$Note) {
    $r = $results[$Scenario_]
    if (-not $r) { return [ordered]@{ id = $Id; scenario = $Scenario_; status = 'não medido'; reason = "cenario '$Scenario_' nao executado nesta rodada"; verdict = $null } }
    $evals = @(); $missing = $false; $fail = $false
    foreach ($c in $Checks) {
        if ($null -eq $c.value) { $missing = $true; $evals += [ordered]@{ metric = $c.metric; value = $null; limit = $c.limit; ok = $null }; continue }
        $ok = if ($null -eq $c.limit) { $null } else { [double]$c.value -le [double]$c.limit }
        if ($ok -eq $false) { $fail = $true }
        $evals += [ordered]@{ metric = $c.metric; value = $c.value; limit = $c.limit; ok = $ok }
    }
    $status = if ($missing -and $evals.Count -eq 0) { 'não medido' } else { 'medido' }
    $verdict = if ($null -eq ($evals | Where-Object { $null -ne $_.limit } | Select-Object -First 1)) { 'registrado' } elseif ($fail) { 'reprovado' } elseif ($missing) { 'parcial' } else { 'aprovado' }
    $o = [ordered]@{ id = $Id; scenario = $Scenario_; description = $Desc; status = $status; verdict = $verdict; shortSample = $short; checks = $evals }
    if ($Note) { $o.note = $Note }
    return $o
}
function M($stat, [string]$k) { if ($stat) { $stat[$k] } else { $null } }
$targets = @()
$bw = $results['baseline-wpf']; $hid = $results['hidden']; $ids = $results['idle-static']; $syn = $results['e1-synthetic']
if ($bw) { $targets += New-Target 'P-00' 'baseline-wpf' 'Private bytes / Working set do app vazio (registrar; base de P-06)' @(
        @{ metric = 'privateMB.mean'; value = (M $bw.privateMB 'mean'); limit = $null }, @{ metric = 'workingSetMB.mean'; value = (M $bw.workingSetMB 'mean'); limit = $null }) '' }
else { $targets += [ordered]@{ id = 'P-00'; scenario = 'baseline-wpf'; status = 'não medido'; reason = 'cenario nao executado nesta rodada' } }
if ($hid) { $targets += New-Target 'P-01' 'hidden' 'CPU proc <= 0,1% · wakes/s <= 1 · frames 0' @(
        @{ metric = 'cpuProcPct.mean'; value = (M $hid.cpuProcPct 'mean'); limit = 0.1 }, @{ metric = 'wakesPerSec.mean'; value = (M $hid.wakesPerSec 'mean'); limit = 1 }, @{ metric = 'fps.max'; value = (M $hid.fps 'max'); limit = 0 }) 'Meta definida para 5 min.' }
else { $targets += [ordered]@{ id = 'P-01'; scenario = 'hidden'; status = 'não medido'; reason = 'cenario nao executado nesta rodada' } }
if ($ids) { $targets += New-Target 'P-02' 'idle-static' 'CPU proc <= 0,2% · frames/s <= 0,1' @(
        @{ metric = 'cpuProcPct.mean'; value = (M $ids.cpuProcPct 'mean'); limit = 0.2 }, @{ metric = 'fps.mean'; value = (M $ids.fps 'mean'); limit = 0.1 }) 'Meta definida para 5 min.' }
else { $targets += [ordered]@{ id = 'P-02'; scenario = 'idle-static'; status = 'não medido'; reason = 'cenario nao executado nesta rodada' } }
$note11 = 'Sobre e1-synthetic (quadro de teste, proxy do presente-onda-pesada em sprites; faixa de 96 DIP a 150% a confirmar na T1.15).'
if ($syn) {
    $targets += New-Target 'P-11' 'e1-synthetic' 'Composicao do frame p50 <= 2,5 ms / p95 <= 4 ms' @(
        @{ metric = 'composeP50Ms.p50'; value = (M $syn.composeP50Ms 'p50'); limit = 2.5 }, @{ metric = 'composeP95Ms.p95'; value = (M $syn.composeP95Ms 'p95'); limit = 4 }) $note11
    $targets += New-Target 'P-12' 'e1-synthetic' 'UpdateLayeredWindowIndirect p50 <= 1 ms / p95 <= 2 ms' @(
        @{ metric = 'presentP50Ms.p50'; value = (M $syn.presentP50Ms 'p50'); limit = 1 }, @{ metric = 'presentP95Ms.p95'; value = (M $syn.presentP95Ms 'p95'); limit = 2 }) $note11
} else {
    $targets += [ordered]@{ id = 'P-11'; scenario = 'e1-synthetic'; status = 'não medido'; reason = 'cenario nao executado nesta rodada' }
    $targets += [ordered]@{ id = 'P-12'; scenario = 'e1-synthetic'; status = 'não medido'; reason = 'cenario nao executado nesta rodada' }
}
$targets += [ordered]@{ id = 'P-22'; scenario = 'taskbar-uia'; status = 'não medido'; reason = 'Atualizacao da barra (UIA) depende de SDD-21 T-21.8 / --diag; uiaRetainedMB vazio no CSV; este script nao a mede.' }

$summary = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    parameters = [ordered]@{ scenarios = $scenarios; secondsPerRun = $Seconds; repeats = $Repeats; warmupSeconds = $WarmupSeconds; dwmRestSeconds = $DwmRestSeconds; noInput = [bool]$NoInput; configuration = $Configuration; exe = $Exe; logicalCores = $cores; gpuCountersAvailable = $script:gpuAvailable }
    note = 'Veredito por meta apenas informativo. A decisao go/no-go (CA-16.2) pertence a T1.15. Amostras com menos de 300 s sao curtas (shortSample).'
    machine = 'machine.json'
    dwmRest = $dwmRest
    scenarios = $results
    targets = $targets
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -Path (Join-Path $Out 'summary.json') -Encoding utf8

# ---------- 5. tabela-resumo ----------
Write-Host ''
Write-Host '== Resumo'
$targets | ForEach-Object {
    $v = if ($_.Contains('verdict') -and $_.verdict) { $_.verdict } else { '-' }
    '{0,-5} {1,-14} {2,-11} {3}' -f $_.id, $_.scenario, $_.status, $v
} | Write-Host
foreach ($k in $results.Keys) {
    $r = $results[$k]
    Write-Host ("{0,-13} cpu {1,7} %  dwm {2,7} %  fps {3,6}  wakes/s {4,5}  privMB {5,6}" -f $k, (M $r.cpuProcPct 'mean'), (M $r.cpuDwmPct 'mean'), (M $r.fps 'mean'), (M $r.wakesPerSec 'mean'), (M $r.privateMB 'mean'))
}
Write-Host "Saida: $Out"
Write-Host "  $(Join-Path $Out 'machine.json')"
Write-Host "  $(Join-Path $Out 'summary.json')"
exit 0
