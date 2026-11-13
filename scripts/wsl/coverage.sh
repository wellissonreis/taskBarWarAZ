#!/usr/bin/env bash
# Cobertura do Core (SDD-17 RF-17.8). Coletor "Code Coverage" (Microsoft.CodeCoverage, já referenciado pelo Test.Sdk);
# o "XPlat Code Coverage" (coverlet) não está disponível neste repositório. Saída: artifacts/coverage/core/*.cobertura.xml.
# Sob instrumentação, ArchitectureTests.References_PortableAssemblies falha (o instrumentador adiciona referência ao Core):
# é esperado e não é regressão; o teste passa em scripts/wsl/test.sh.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"
out="$root/artifacts/coverage/core"
rm -rf "$out"
dotnet test tests/SideWar.Core.Tests -c Release --collect "Code Coverage;Format=Cobertura" --results-directory "$out" || true
python3 -I - "$out" <<'PY'
import sys, glob, xml.etree.ElementTree as ET
f = glob.glob(sys.argv[1] + "/*/*.cobertura.xml")[0]
hw = [0, 0]; gen = [0, 0]
for p in ET.parse(f).getroot().iter("package"):
    if p.attrib["name"] != "SideWar.Core": continue
    for c in p.iter("class"):
        t = gen if "/obj/" in c.attrib["filename"] else hw
        for l in c.iter("line"):
            t[1] += 1; t[0] += int(l.attrib["hits"]) > 0
print(f"Core escrito à mão: {100*hw[0]/hw[1]:.2f}% ({hw[0]}/{hw[1]} linhas)")
print(f"Core incl. JSON source-generated: {100*(hw[0]+gen[0])/(hw[1]+gen[1]):.2f}%")
PY
