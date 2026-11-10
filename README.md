# SideWar Desktop

Jogo idle sobre a barra de tarefas do Windows 11. A etapa E0 contém somente a fundação da solução; o overlay e a simulação ainda não estão implementados. As fichas em [specs/PLANO.md](specs/PLANO.md) definem a execução.

## Pré-requisitos

- WSL2: .NET SDK 10.0.x (mínimo 10.0.400), `rsync`, Bash e interop habilitado.
- Windows 11 22H2 ou superior: .NET SDK 10.0.x (mínimo 10.0.400, `dotnet.exe` no PATH) e PowerShell 7 (`pwsh.exe`).
- Opcionais: Windows Performance Toolkit (WPR/WPA) e PerfView para as etapas de desempenho.

O `global.json` usa `latestFeature`, sem versões preview. As dependências diretas têm versões fixas em `Directory.Packages.props`. Não há chamadas de rede no produto; restore do SDK/NuGet requer acesso aos respectivos servidores.

## Build e testes

Na raiz do repositório no WSL:

```bash
scripts/wsl/build.sh Release
scripts/wsl/test.sh
scripts/wsl/win.sh build.ps1
scripts/wsl/win.sh test.ps1
```

O build WSL inclui os projetos Windows, mas não os executa. Os testes portáveis são Core, Runtime, Persistence e Rendering. Somente Core contém testes nesta etapa (arquitetura); os outros projetos estão vazios. Os TRX ficam em `artifacts/test-results/linux/` e, no espelho Windows, `artifacts/test-results/windows/`.

`win.sh` sincroniza o código com `rsync --delete` para **`C:\dev\sidewar`** (`/mnt/c/dev/sidewar`). Essa pasta é um espelho dedicado: arquivos extras nela podem ser removidos. `.git`, `bin`, `obj` e `artifacts` são excluídos da sincronização; os resultados locais de cada ambiente são preservados. Para outro destino, configure `SIDEWAR_WIN_MIRROR=/mnt/c/dev/outro-espelho`.

Se `pwsh.exe` não estiver no PATH do WSL, o wrapper procura a instalação padrão e os aliases WindowsApps. É possível definir `SIDEWAR_PWSH` com o caminho WSL completo do executável. Os scripts PowerShell recuperam o PATH de máquina/usuário para instalações recentes do SDK.

Sem interop, sincronize pelo WSL e execute manualmente no PowerShell 7 do Windows:

```powershell
Set-Location C:\dev\sidewar
./scripts/windows/build.ps1
./scripts/windows/test.ps1
```

`test.ps1 -IncludeInteractive` inclui os projetos de desktop Windows, vazios em E0. Por padrão o script exclui `Env=WindowsInteractive`. Testes de foco, DPI, UIA e desempenho pertencem às próximas etapas e exigem evidência Windows.

```bash
scripts/wsl/bench.sh '*'
scripts/wsl/win.sh run.ps1 -Configuration Debug
```

Benchmarks Linux são indicativos. O executável de benchmark de E0 não tem cenários e não mede desempenho. O App é apenas o lifetime WPF vazio, sem janela ou serviços; `run.ps1` permanece aberto no dispatcher até ser encerrado. `measure-perf.ps1` e `publish.ps1` falham explicitamente como esqueletos das etapas futuras.

## Estrutura e limites

`src/` contém Core, Runtime, Persistence, Rendering, Platform.Windows e App; `tests/` contém seis projetos; `tools/SideWar.AssetPipeline` e `bench/SideWar.Benchmarks` têm apenas pontos de entrada vazios. O App é o único projeto com WPF. Runtime não referencia Skia. Os projetos Windows explicitam `net10.0-windows10.0.22000.0`, pois o TFM sem versão assume plataforma 7.0, incompatível com `SupportedOSPlatformVersion=10.0.22000.0`.

RF-01.6 permanece parcial: `IClock`, `IGameCommand`, `IGameCommandSink` e `ISaveStore` existem. `IOverlaySurface` depende de resolver o conflito entre `SKCanvas` e a proibição de Skia em Runtime. Os contratos que exigem tipos ainda não definidos (`UiNotification`, sinais de ambiente e resultados/comandos concretos) ficam pendentes; E0 não inventa seus modelos nem implementa E1.

A validação está em [specs/evidencias/E0.md](specs/evidencias/E0.md). O commit inicial em `main` foi explicitamente autorizado pelo usuário (SDD-02 RF-02.7). As evidências de execução registradas são anteriores ao commit.
