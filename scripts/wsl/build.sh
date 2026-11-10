#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
configuration="${1:-Release}"
[[ "$configuration" == Debug || "$configuration" == Release ]] || { echo 'Expected Debug or Release' >&2; exit 2; }
extra=()
[[ "$configuration" != Release ]] || extra+=(-p:ContinuousIntegrationBuild=true)
dotnet build SideWar.slnx -c "$configuration" -warnaserror "${extra[@]}"
