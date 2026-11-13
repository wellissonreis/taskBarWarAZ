#!/usr/bin/env bash
# SDD-17 RF-17.7: compara artifacts/determinism/linux.json com windows.json (gerados pelo DeterminismTests).
# Uso: compare-determinism.sh [linux.json windows.json]
# Sem argumentos: linux = artifacts/determinism/linux.json; windows = espelho ($SIDEWAR_WIN_MIRROR/artifacts/determinism/windows.json).
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
mirror="${SIDEWAR_WIN_MIRROR:-/mnt/c/dev/sidewar}"
linux="${1:-$root/artifacts/determinism/linux.json}"
windows="${2:-$mirror/artifacts/determinism/windows.json}"
for f in "$linux" "$windows"; do
    [[ -s "$f" ]] || { echo "Missing or empty determinism file: $f" >&2; exit 2; }
done
norm() { tr -d ' \r' < "$1" | grep -E '^"[^"]+":"[0-9]+",?$' | sed 's/,$//' | sort; }
a="$(norm "$linux")"; b="$(norm "$windows")"
[[ -n "$a" && -n "$b" ]] || { echo 'No hashes parsed from input' >&2; exit 2; }
if [[ "$a" == "$b" ]]; then
    echo "OK: determinism identical Linux x Windows"
    echo "$a"
    exit 0
fi
echo 'DIVERGENCE between Linux and Windows hashes:' >&2
diff <(echo "$a") <(echo "$b") >&2 || true
exit 1
