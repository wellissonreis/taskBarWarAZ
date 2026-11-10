#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
echo 'Linux benchmarks are indicative only; E0 has no benchmark scenarios.'
mkdir -p artifacts/bench/linux
dotnet run -c Release --project bench/SideWar.Benchmarks -- --filter "${1:-*}" --artifacts "$root/artifacts/bench/linux"
