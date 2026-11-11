#!/usr/bin/env bash
# Benchmarks (SDD-17 RF-17.5). No Linux/WSL os resultados são INDICATIVOS ("indicativo (WSL)"); o oficial é o Windows.
#   bench.sh            -> BenchmarkDotNet (*) + percentis do tick (--p99)
#   bench.sh p99        -> só o harness de percentis (média, p50, p99, alocado por tick)
#   bench.sh <filtro>   -> só BenchmarkDotNet com --filter <filtro>
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
echo 'indicativo (WSL): benchmarks no Linux não valem como medição oficial (SDD-17).'
echo "load average: $(cat /proc/loadavg) (CPUs: $(nproc))"
out="$root/artifacts/bench/linux"
mkdir -p "$out"
arg="${1:-}"
if [[ "$arg" != "p99" ]]; then
    dotnet run -c Release --project bench/SideWar.Benchmarks -- --filter "${arg:-*}" --artifacts "$out"
fi
if [[ -z "$arg" || "$arg" == "p99" ]]; then
    dotnet run -c Release --project bench/SideWar.Benchmarks -- --p99 --artifacts "$out"
fi
