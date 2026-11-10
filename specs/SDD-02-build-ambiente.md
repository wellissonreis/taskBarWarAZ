# SDD-02: Build, ambiente WSL/Windows e estratégia de execução

**Status:** Rascunho para revisão · **Etapa:** E0 · **Depende de:** SDD-01 · **Bloqueia:** E1 em diante (E3 depende só de E0)

## 1. Identificador e título

SDD-02: Ambiente de desenvolvimento, build e testes entre WSL e Windows.

## 2. Objetivo

Garantir um fluxo reproduzível para compilar e testar no WSL o que for portável, e para compilar, executar e validar no Windows o que depende de WPF e Win32, sem presumir que algo funciona no Linux.

## 3. Contexto técnico

- O repositório vive no WSL2 (`/home/wellissonreis/repos/taskBarWarAZ`).
- O .NET SDK 10.0.402 existe no Linux (ANALISE §8). O SDK no Windows **não foi verificado** e precisa ser instalado e confirmado pelo usuário.
- WPF e Win32 só executam no Windows. Compilar `net10.0-windows` no Linux exige `EnableWindowsTargeting=true` (ADR-012).
- Executar binários a partir de `\\wsl.localhost\...` é lento e quebra observadores de arquivo. Por isso, a execução no Windows usa um espelho em disco NTFS.

## 4. Escopo e exclusões

**Escopo:** `global.json`, scripts `scripts/wsl/*.sh` e `scripts/windows/*.ps1`, sincronização WSL→Windows, matriz "onde roda cada teste", política de evidências e build reproduzível.

**Exclusões:** CI em nuvem (pode ser adicionada depois por ADR), publicação (SDD-18) e instalação de SDKs (feita pelo usuário).

## 5. Requisitos funcionais

### RF-02.1: Pré-requisitos (documentados em `README.md` na raiz)

- **WSL:** .NET SDK 10.0.x e `rsync`.
- **Windows 11 (22H2 ou superior):** .NET SDK 10.0.x (`dotnet.exe` no PATH do Windows) e PowerShell 7 (`pwsh.exe`).
- **Opcionais no Windows:** Windows Performance Toolkit (WPR/WPA) e PerfView, para análise de desempenho (SDD-16).

### RF-02.2: `global.json`

```json
{ "sdk": { "version": "10.0.400", "rollForward": "latestFeature", "allowPrerelease": false } }
```

### RF-02.3: Scripts no WSL (`scripts/wsl/`)

| Script | Função | Saída esperada |
|---|---|---|
| `build.sh [Debug\|Release]` | `dotnet build SideWar.slnx -c $1 -warnaserror` | Exit 0. Compila **todos** os projetos, inclusive os Windows (somente compilação) |
| `test.sh` | `dotnet test` em `tests/SideWar.{Core,Runtime,Persistence,Rendering}.Tests` com `--logger trx` e resultados em `artifacts/test-results/linux/` | Exit 0 |
| `bench.sh [filtro]` | `dotnet run -c Release --project bench/SideWar.Benchmarks -- --filter $1` com saída em `artifacts/bench/linux/` | Os números são **indicativos** e não valem como meta (SDD-16) |
| `sync-to-windows.sh` | `rsync -a --delete --exclude bin --exclude obj --exclude artifacts ./ /mnt/c/dev/sidewar/` | Espelho atualizado |
| `win.sh <script.ps1> [args]` | Executa `sync-to-windows.sh` e depois `pwsh.exe -NoProfile -File "C:\dev\sidewar\scripts\windows\<script.ps1>" args`, via interop do WSL | Repassa o exit code do Windows |

O caminho do espelho é configurável pela variável `SIDEWAR_WIN_MIRROR`, com padrão `/mnt/c/dev/sidewar`.

### RF-02.4: Scripts no Windows (`scripts/windows/`)

| Script | Função |
|---|---|
| `build.ps1 [-Configuration Release]` | `dotnet build SideWar.slnx -c $Configuration -warnaserror` |
| `test.ps1 [-IncludeInteractive]` | Testes portáveis e, com `-IncludeInteractive`, também `Platform.Windows.Tests` e `App.SmokeTests`. Resultados em `artifacts\test-results\windows\` |
| `run.ps1 [-Configuration Debug] [-- args]` | Executa `SideWar.App` |
| `measure-perf.ps1 -Scenario <nome> -Seconds <n>` | Ver SDD-16 e SDD-17 |
| `publish.ps1` | Ver SDD-18 |

### RF-02.5: Matriz de execução de testes

| Projeto de teste | WSL | Windows (sessão não interativa) | Windows (desktop interativo) |
|---|---|---|---|
| Core.Tests, Runtime.Tests, Persistence.Tests | ✔ | ✔ | ✔ |
| Rendering.Tests (Skia raster) | ✔ (`NativeAssets.Linux.NoDependencies`) | ✔ | ✔ |
| Platform.Windows.Tests | ✘ | ✘ (precisa de desktop) | ✔ |
| App.SmokeTests | ✘ | ✘ | ✔ |
| Checklists manuais (MTC, SDD-17) | ✘ | ✘ | ✔ (pessoa operando) |

Os testes que exigem desktop interativo são marcados com o traço `[Trait("Env","WindowsInteractive")]`. Por padrão, `test.ps1` os exclui, a não ser com `-IncludeInteractive`.

### RF-02.6: Política de evidências

Toda aprovação de etapa registra em `specs/evidencias/E<n>.md`:

1. Comando executado e ambiente: WSL ou Windows, versão do SO (`winver`/`[Environment]::OSVersion`), versão do SDK.
2. Resultado (copiar o resumo do TRX ou da saída).
3. Data e hash do commit.

É **proibido** registrar como "passou no Windows" algo executado só no WSL.

### RF-02.7: Controle de versão

- Inicializar o repositório Git na etapa E0 (`git init`, branch `main`) com `.gitignore` para `bin/ obj/ artifacts/ *.user .vs/`.
- O primeiro commit contém `specs/` e o esqueleto. Commits só com autorização do usuário.

## 6. Requisitos não funcionais

- **RNF-02.1:** `build.sh` incremental sem alterações termina em ≤ 15 s no WSL (meta, não comprovada).
- **RNF-02.2:** O build é determinístico: `<Deterministic>true</Deterministic>` e `<ContinuousIntegrationBuild>` ligado nos scripts de release.
- **RNF-02.3:** Os scripts são idempotentes, falham com exit code ≠ 0 e mensagem clara, e não exigem privilégios de administrador.

## 7. Estratégia de implementação

1. Criar `global.json`, `.gitignore`, `.editorconfig` (C# padrão, LF no repositório) e `.gitattributes` (`* text=auto eol=lf`, `*.ps1 text eol=crlf`, `*.png binary`).
2. Escrever os scripts de RF-02.3 e RF-02.4 com `set -euo pipefail` no Bash e `$ErrorActionPreference='Stop'` no PowerShell.
3. Validar `win.sh build.ps1` a partir do WSL. Se o interop do WSL estiver desabilitado, documentar a execução manual no PowerShell do Windows.
4. Registrar a evidência E0.

## 8. Dependências

SDD-01 (estrutura de projetos).

## 9. Critérios de aceitação

- **CA-02.1:** `scripts/wsl/build.sh Release` retorna 0 no WSL, compilando também os projetos `net10.0-windows`. Se isso não for possível, o fallback de SDD-01 §11 fica documentado e aprovado.
- **CA-02.2:** `scripts/wsl/test.sh` retorna 0, com os TRX salvos.
- **CA-02.3:** `scripts/wsl/win.sh build.ps1` retorna 0 com o SDK do Windows, comprovado pela saída mostrando `dotnet.exe` e o caminho `C:\dev\sidewar`.
- **CA-02.4:** `specs/evidencias/E0.md` existe e segue RF-02.6.

## 10. Cenários de teste

| ID | Cenário | Ambiente |
|---|---|---|
| T-02.1 | Build limpo (`git clean -xfd` e depois build) | WSL |
| T-02.2 | Sync seguido de build no Windows | WSL→Windows |
| T-02.3 | Falha proposital (erro de compilação) propaga exit ≠ 0 pelo `win.sh` | WSL→Windows |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| SDK ausente ou divergente no Windows | Média | Alto | `global.json`; `build.ps1` imprime `dotnet --info` e falha se a versão for < 10.0.400 |
| Interop do WSL desabilitado (`pwsh.exe` não encontrado) | Baixa | Médio | Execução manual documentada |
| Diferenças de fim de linha (CRLF) | Média | Baixo | `.gitattributes` |
| Antivírus lento no espelho NTFS | Média | Baixo | Recomendar exclusão de `C:\dev\sidewar\**\obj` do Defender (opcional, decisão do usuário) |
