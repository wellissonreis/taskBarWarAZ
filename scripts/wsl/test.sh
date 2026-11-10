#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
for project in Core Runtime Persistence Rendering; do
    dotnet test "tests/SideWar.$project.Tests" -c Release -p:ContinuousIntegrationBuild=true --logger "trx;LogFileName=$project.trx" --results-directory "$root/artifacts/test-results/linux"
done
