#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
mirror="${SIDEWAR_WIN_MIRROR:-/mnt/c/dev/sidewar}"
[[ "$mirror" == /mnt/[a-z]/* && "$mirror" != /mnt/[a-z]/ ]] || { echo 'Mirror must be a directory beneath a mounted Windows drive' >&2; exit 2; }
mkdir -p "$mirror"
rsync -a --delete --exclude .git --exclude bin --exclude obj --exclude artifacts "$root/" "$mirror/"
