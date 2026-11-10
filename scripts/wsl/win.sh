#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
script="${1:?Usage: win.sh script.ps1 [args]}"
shift
[[ "$script" != */* && "$script" != *\\* && -f "scripts/windows/$script" ]] || { echo 'Unknown Windows script' >&2; exit 2; }
"$root/scripts/wsl/sync-to-windows.sh"
pwsh="${SIDEWAR_PWSH:-}"
if [[ -z "$pwsh" ]]; then
    pwsh="$(command -v pwsh.exe || true)"
fi
if [[ -z "$pwsh" ]]; then
    for candidate in /mnt/c/Program\ Files/PowerShell/7/pwsh.exe /mnt/c/Users/*/AppData/Local/Microsoft/WindowsApps/pwsh.exe; do
        if [[ -f "$candidate" ]]; then pwsh="$candidate"; break; fi
    done
fi
[[ -n "$pwsh" ]] || { echo 'pwsh.exe not found; set SIDEWAR_PWSH to its WSL path' >&2; exit 127; }
mirror="${SIDEWAR_WIN_MIRROR:-/mnt/c/dev/sidewar}"
windows_script="$(wslpath -w "$mirror/scripts/windows/$script")"
exec "$pwsh" -NoProfile -File "$windows_script" "$@"
