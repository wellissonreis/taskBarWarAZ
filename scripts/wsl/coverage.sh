#!/usr/bin/env bash
# Cobertura (SDD-17 RF-17.8). Coletor "Code Coverage" (Microsoft.CodeCoverage, já referenciado pelo Test.Sdk);
# o "XPlat Code Coverage" (coverlet) não está disponível neste repositório.
# Uso: coverage.sh [core|runtime|persistence|all]  (padrão: core). Saída: artifacts/coverage/<alvo>/*.cobertura.xml.
# Código gerado (obj/*.g.cs, JSON source-generated) é reportado à parte do escrito à mão.
# Sob instrumentação, ArchitectureTests.References_PortableAssemblies falha (artefato do coletor; passa em test.sh).
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
sel="${1:-core}"
[[ "$sel" == all ]] && sel="core runtime persistence"
for t in $sel; do
  case "$t" in
    core) proj=SideWar.Core; pkg=SideWar.Core ;;
    runtime) proj=SideWar.Runtime; pkg=SideWar.Runtime ;;
    persistence) proj=SideWar.Persistence; pkg=SideWar.Persistence ;;
    *) echo "alvo desconhecido: $t" >&2; exit 2 ;;
  esac
  out="$root/artifacts/coverage/$t"
  rm -rf "$out"
  dotnet test "tests/$proj.Tests" -c Release --collect "Code Coverage;Format=Cobertura" --results-directory "$out" || true
  python3 -I - "$out" "$pkg" <<'PY'
import sys, glob, xml.etree.ElementTree as ET
f = glob.glob(sys.argv[1] + "/*/*.cobertura.xml")[0]
pkg = sys.argv[2]
hw = [0, 0]; gen = [0, 0]
for p in ET.parse(f).getroot().iter("package"):
    if p.attrib["name"] != pkg: continue
    for c in p.iter("class"):
        t = gen if "/obj/" in c.attrib["filename"] else hw
        for l in c.iter("line"):
            t[1] += 1; t[0] += int(l.attrib["hits"]) > 0
print(f"{pkg} escrito à mão: {100*hw[0]/hw[1]:.2f}% ({hw[0]}/{hw[1]} linhas)")
if gen[1]: print(f"{pkg} incl. código gerado: {100*(hw[0]+gen[0])/(hw[1]+gen[1]):.2f}% (gerado: {gen[0]}/{gen[1]})")
PY
done
