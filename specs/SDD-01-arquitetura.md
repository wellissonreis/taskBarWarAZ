# SDD-01: Arquitetura da solução

**Status:** Rascunho para revisão · **Etapa:** E0 · **Depende de:** nenhuma (revisada pelo design idle: ver SDD-20 a SDD-27 e ADR-018 a ADR-030) · **Bloqueia:** todas

## 1. Identificador e título

SDD-01: Arquitetura da solução SideWar Desktop (projetos, camadas, threads e fluxo de dados).

## 2. Objetivo

Definir a estrutura de projetos, as fronteiras entre camadas, o modelo de threads e os contratos entre componentes. O objetivo é que cada ficha seguinte seja implementada sem decisões arquiteturais adicionais.

## 3. Contexto técnico

- Stack obrigatória: C#/.NET 10, WPF, SkiaSharp e Win32.
- O repositório fica no WSL, e só parte do código pode ser testada no Linux (ADR-012).
- Prioridade: baixo consumo de CPU, RAM e GPU (SDD-16).
- Problemas da referência a evitar: L1, L2, L3, L5 e L9 (ANALISE-SIDEWARAZ.md §7).

## 4. Escopo e exclusões

**Escopo:** solução, projetos, dependências entre projetos, namespaces, modelo de threads, contratos (interfaces), fluxo de comandos e eventos, e convenções de código.

**Exclusões:** detalhes de cada subsistema (fichas próprias), scripts de build (SDD-02) e empacotamento (SDD-18).

## 5. Requisitos funcionais

### RF-01.1: Estrutura de diretórios

```
/ (raiz do repositório taskBarWarAZ)
├─ SideWar.slnx
├─ Directory.Build.props          # LangVersion=latest, Nullable=enable, TreatWarningsAsErrors=true, ImplicitUsings=enable
├─ Directory.Packages.props       # Central Package Management (versões fixas)
├─ global.json                    # SDK 10.0.x, rollForward=latestFeature
├─ NativeMethods.txt              # (fica em src/SideWar.Platform.Windows/)
├─ specs/                         # estas fichas
├─ assets/
│  ├─ source/                     # PNG de origem (importados, SDD-09)
│  └─ fonts/                      # TTF/OTF + licenças OFL
├─ src/
│  ├─ SideWar.Core/               # net10.0: simulação, regras e modelo de conteúdo
│  ├─ SideWar.Runtime/            # net10.0: GameHost, políticas, estados da app e portas (interfaces)
│  ├─ SideWar.Persistence/        # net10.0: saves, settings e migrações
│  ├─ SideWar.Rendering/          # net10.0: Skia, atlas, cenas, HUD e efeitos
│  ├─ SideWar.Platform.Windows/   # net10.0-windows: Win32 (janelas, hooks, DPI, energia)
│  └─ SideWar.App/                # net10.0-windows, UseWPF: composition root e janelas WPF
├─ tools/
│  └─ SideWar.AssetPipeline/      # net10.0 console: gera atlas e manifesto
├─ tests/
│  ├─ SideWar.Core.Tests/         # Linux e Windows
│  ├─ SideWar.Runtime.Tests/      # Linux e Windows
│  ├─ SideWar.Persistence.Tests/  # Linux e Windows
│  ├─ SideWar.Rendering.Tests/    # Linux e Windows (Skia raster)
│  ├─ SideWar.Platform.Windows.Tests/  # somente Windows (sessão interativa)
│  └─ SideWar.App.SmokeTests/     # somente Windows (sessão interativa)
├─ bench/
│  └─ SideWar.Benchmarks/         # net10.0, BenchmarkDotNet
└─ scripts/
   ├─ wsl/                        # build, test e sync para o Windows
   └─ windows/                    # PowerShell: build, test, medição e publicação
```

### RF-01.2: Grafo de dependências entre projetos (permitido, acíclico)

```
Core  ←  Runtime  ←  Rendering
  ↑         ↑    ↖
  │   Persistence  Platform.Windows (referencia Runtime para implementar as portas)
  │         ↑            ↑
  └──────── App (composition root; referencia todos)
```

- `Core` não referencia nenhum projeto nem pacote além do BCL.
- `Runtime` referencia apenas `Core`. Ele **define as portas** (interfaces) que `Platform.Windows`, `Rendering` e `Persistence` implementam ou consomem.
- `Rendering` referencia `Core`, `Runtime` e `SkiaSharp`. Nunca referencia Win32 nem WPF.
- `Persistence` referencia `Core` e `Runtime`.
- `Platform.Windows` referencia `Runtime`, `SkiaSharp` (apenas para o tipo `SKSurface` sobre o DIB) e `CsWin32`. Não referencia `Core`, a não ser os tipos de comando expostos via `Runtime`.
- `App` referencia todos e é o único lugar com `new` dos serviços concretos.
- Qualquer referência fora deste grafo faz o teste de arquitetura falhar (RF-01.7).

### RF-01.3: Responsabilidades por projeto

| Projeto | Contém | Não contém |
|---|---|---|
| Core | `Simulation` (estado em pools de structs), sistemas (`SpawnSystem`, `TargetingSystem`, `ProjectileSystem`, `MovementSystem`, `EconomySystem`, `WaveSystem`), `Commands` com resultado tipado, `SimEvents` (buffer circular), `Content` (modelos e carregamento de JSON embutido), `Rng` (PCG32), `StateHash` | I/O, relógio real, threads, Skia, Win32 |
| Runtime | `GameHost` (laço lógico: acumulador, ticks e despacho de comandos), `AppStateMachine` (visibilidade e modos), `PauseReasons`, `RenderPolicy`, `FullscreenClassifier` (pura), `PresenceTracker` e `OfflineCalculator` (SDD-25; substituem `IdlePolicy`, ADR-022), `TaskbarClassifier` e `SlotMap` (SDD-21, puros), `DockLayout`/`SceneLayout` (cálculo puro de geometria; SDD-05, SDD-22), `DragController`, `HudModel`, portas `IClock`, `IOverlaySurface`, `IWindowEnvironment`, `ISaveStore`, `IUiBridge` | Chamadas Win32 e WPF |
| Persistence | `SettingsStore`, `ProfileStore`, `SessionStore`, `AtomicFileWriter`, `Migrations`, contextos de source-gen JSON | Regras de jogo |
| Rendering | `SpriteAtlas`, `ScaledSpriteCache`, `StaticLayer`, `FieldRenderer`, `HudRenderer`, `EffectSystem` (visual), `TextCache`, `FrameComposer` | Lógica de jogo e janelas |
| Platform.Windows | `GameThread` (laço de mensagens com timer), `OverlayWindow` (a janela da faixa, `SceneWindow` em SDD-22), janelas do conjunto de ADR-021 (widget HUD, painel, cards, banners/hotspots, caixa de suprimentos e selo de ausente; SDD-03 RF-03.9), `TaskbarService` e `TaskbarProbe` (UIA, SDD-21), `LayeredSurface` (DIB e `UpdateLayeredWindow`), `MonitorService`, `ForegroundWatcher` (WinEvent), `AppBarProbe`, `SessionPowerWatcher`, `HotkeyService`, `IdleInputProbe`, `EcoQosController`, `NativeMenu` | Regras de decisão (ficam em Runtime) |
| App | `Program`/`App.xaml` (lifetime), `Bootstrapper`, `SingleInstance`, `TrayIcon`, `UiBridge` (`IUiBridge`), janelas WPF (somente `SettingsWindow` e `AboutWindow`, ADR-021; painel, cards e perguntas são janelas Skia da T-Game, SDD-24/26), `CrashHandler`, `Log` | Desenho do jogo |

### RF-01.4: Modelo de threads (ADR-002)

> **Estendido por SDD-21 RF-21.3 (ADR-023).** Acrescenta a thread `SideWar.Shell` (MTA, sob demanda, só UI Automation); o restante do modelo (ADR-002) continua.

| Thread | Nome | Responsabilidades | Bloqueia em |
|---|---|---|---|
| T-UI | `Main` (STA, WPF) | `Application`, janelas WPF, bandeja (HwndSource oculto), `SessionEnding` do WPF | Dispatcher do WPF |
| T-Game | `SideWar.Game` (criada pelo `GameThread`, `IsBackground=false`) | Cria e possui a `OverlayWindow` (faixa) e as demais janelas do jogo (widget, painel, cards, banners, caixa, selo; SDD-03 RF-03.9) e as janelas sonda (AppBar e energia). Executa hooks WinEvent, hotkeys, simulação e renderização | `MsgWaitForMultipleObjectsEx(timer, wakeEvent; QS_ALLINPUT)` |
| T-Shell | `SideWar.Shell` (MTA, criada **sob demanda**, ADR-023) | Consulta UI Automation **somente leitura** da barra de tarefas (`TaskbarProbe`, SDD-21 RF-21.3). Acionada por evento (sem polling contínuo); entrega o resultado à T-Game como `TaskbarLayoutChanged`. Cai para o fallback virtual após 3 falhas seguidas. Nunca altera a barra | fila de pedidos (bloqueada quando ociosa) |
| T-IO | ThreadPool | Escrita de arquivos (`AtomicFileWriter`) e logs com flush em lote | async I/O |

Regras:

1. Toda mutação de `Simulation` acontece **somente** na T-Game. O resultado da T-Shell chega à T-Game por fila e vira o comando `LayoutChanged`, aplicado **entre ticks** (ADR-026).
2. A T-UI envia comandos com `IGameCommandSink.Post(ICommand)`: uma fila `ConcurrentQueue<ICommand>` mais um `AutoResetEvent` que acorda a T-Game. Os comandos são processados no início do próximo tick lógico (ou imediatamente, se a simulação estiver parada).
3. A T-Game publica para a UI com `IUiBridge`, que encapsula `Dispatcher.BeginInvoke`. O conteúdo são registros imutáveis (`record`) com snapshot dos dados necessários, nunca referências ao estado vivo.
4. Snapshots para a UI WPF são criados no máximo a 4 Hz e somente quando há janela WPF aberta que os consome.
5. A serialização do save acontece na T-Game e gera um `byte[]` alugado de `ArrayPool`. A escrita em disco acontece na T-IO.
6. É proibido usar `lock` na trilha de render/simulação, com exceção da fila de comandos (lock-free).

### RF-01.5: Fluxo por frame na T-Game

```
acorda (timer | evento | mensagem)
 ├─ PumpMessages()              # mensagens Win32 da thread (mouse, DPI, hotkey, WinEvent)
 ├─ DrainCommands()             # comandos da UI e da entrada
 ├─ GameHost.Advance(now)       # 0..N ticks de 1/30 s conforme RenderPolicy e PauseReasons
 ├─ if (RenderPolicy.ShouldRender(dirty, now))
 │     FrameComposer.Compose(surface)  # Skia → DIB
 │     surface.Present()               # UpdateLayeredWindow(Indirect)
 ├─ agenda o próximo wake (SetWaitableTimer) ou espera infinita
 └─ volta a esperar
```

### RF-01.6: Contratos mínimos (assinaturas obrigatórias)

```csharp
// SideWar.Runtime
public interface IClock { long NowTicks { get; } double TicksPerSecond { get; } }        // Stopwatch no Windows; FakeClock nos testes
public interface IGameCommandSink { void Post(IGameCommand command); }
public interface IUiBridge { void Publish(UiNotification n); }                           // T-Game → T-UI
public interface IOverlaySurface {                                                       // implementada por Platform.Windows
    PixelSize Size { get; }  float Scale { get; }
    SKCanvas BeginFrame();   void EndFrame(PixelRect? dirty);                            // Present
    void SetOpacity(byte constantAlpha);                                                 // SourceConstantAlpha
}
public interface IWindowEnvironment {                                                    // eventos do SO normalizados
    event Action<VisibilitySignal> VisibilitySignal;     // fullscreen, lock, display off, suspend, resume
    event Action<DisplayTopologyChanged> TopologyChanged;
    event Action<PowerProfileSignal> PowerProfileChanged;
}
public interface ISaveStore { ValueTask SaveSessionAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken ct); /* ... SDD-14 */ }

// SideWar.Core
public interface IGameCommand { }                       // records: BuildTower, UpgradeTower, LayoutChanged (ADR-026), comandos de SDD-26/27 (sem StartWave e sem SellTower: ADR-019, SDD-27)...
public enum CommandStatus { Ok, InvalidPhase, Paused, InsufficientGold, InvalidPlacement, LimitReached, NotFound, MaxLevel,
    ShuttingDown,
    NotAvailable, AlreadyUsed /* SDD-26: evento inativo ou já usado (ledger) */ }
// Coerente com SDD-27: sem StartWave, logo sem InvalidPhase por fase Build. Os valores InvalidPhase, Paused, InsufficientGold (créditos), InvalidPlacement, LimitReached, NotFound e MaxLevel
// permanecem apenas se o comando correspondente existir em SDD-27 (a lista final é de SDD-11/27). QuestionPending e ModuleDisabled saem (ADR-024).
public readonly record struct CommandResult(CommandStatus Status, PlacementReason Reason = PlacementReason.None, int Missing = 0);
```

Os tipos concretos de cada comando ficam definidos em SDD-11 e SDD-15.

### RF-01.7: Teste de arquitetura

Em `tests/SideWar.Core.Tests/ArchitectureTests.cs`, ler por reflexão as referências de assembly e falhar se:

- `SideWar.Core` referenciar qualquer assembly fora de `System.*`;
- `SideWar.Rendering` referenciar `PresentationFramework`, `WindowsBase` ou `SideWar.Platform.Windows`;
- `SideWar.Runtime` referenciar `SkiaSharp`, WPF ou `SideWar.Platform.Windows`.

## 6. Requisitos não funcionais

- **RNF-01.1:** `Core`, `Runtime`, `Persistence` e `Rendering` compilam e passam nos testes no Linux (WSL) e no Windows.
- **RNF-01.2:** `Nullable=enable` e `TreatWarningsAsErrors=true` em todos os projetos. Os analisadores padrão do SDK ficam em `AnalysisLevel=latest-recommended`.
- **RNF-01.3:** Nenhuma dependência NuGet fora da lista: `SkiaSharp`, `SkiaSharp.NativeAssets.Linux.NoDependencies` (somente nos projetos de teste e benchmark), `Microsoft.Windows.CsWin32`, `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `BenchmarkDotNet`. Adicionar qualquer outra exige ADR.
- **RNF-01.4:** Nenhum uso de reflexão em tempo de execução nos caminhos de jogo. Em JSON, apenas source-gen.
- **RNF-01.5:** Convenção de namespaces: `SideWar.<Projeto>.<Área>`. Um tipo público por arquivo.

## 7. Estratégia de implementação

1. Criar `global.json`, `Directory.Build.props`, `Directory.Packages.props` e `SideWar.slnx` com os 6 projetos `src`, os 6 de `tests`, `bench` e `tools` vazios (classe placeholder só nos testes).
2. Configurar o `TargetFramework` de cada projeto conforme RF-01.1. Em `Platform.Windows` e `App`: `<TargetFramework>net10.0-windows</TargetFramework>`, `<EnableWindowsTargeting>true</EnableWindowsTargeting>`, `<SupportedOSPlatformVersion>10.0.22000.0</SupportedOSPlatformVersion>` (Windows 11). Em `App`: `<UseWPF>true</UseWPF>`, `<OutputType>WinExe</OutputType>` e `app.manifest` (SDD-05).
3. Criar as interfaces de RF-01.6 com corpo mínimo e o teste de arquitetura RF-01.7.
4. Verificar a compilação no WSL e no Windows (SDD-02).

## 8. Dependências

Nenhuma (é a primeira ficha). SDD-02 é executada na mesma etapa.

## 9. Critérios de aceitação

- **CA-01.1:** `dotnet build SideWar.slnx -c Release` termina sem erros e sem warnings no WSL e no Windows.
- **CA-01.2:** `dotnet test` dos 4 projetos portáveis passa no WSL. O teste de arquitetura existe e passa.
- **CA-01.3:** Introduzir uma referência proibida (por exemplo, `Rendering → Platform.Windows`) num branch de teste faz CA-01.2 falhar (verificação manual registrada no PR).
- **CA-01.4:** `Directory.Packages.props` contém somente os pacotes de RNF-01.3, com versões fixas.

## 10. Cenários de teste

| ID | Cenário | Tipo |
|---|---|---|
| T-01.1 | Grafo de referências respeita RF-01.2 | Unitário (reflexão) |
| T-01.2 | Build Release no WSL sem warnings | Script `scripts/wsl/build.sh` |
| T-01.3 | Build Release no Windows sem warnings | Script `scripts/windows/build.ps1` |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| `EnableWindowsTargeting` não compilar WPF no Linux com o SDK 10 | Baixa | Médio | Fallback: compilar `App` e `Platform.Windows` somente no Windows; ajustar o script de SDD-02 |
| Duas threads com janelas Win32 e WPF interferirem (foco, mensagens) | Média | Alto | Proibir owner entre threads; validação na etapa E1 (SDD-03, CA-03.x) |
| Excesso de camadas para um jogo pequeno | Média | Baixo | Camadas finas; Runtime concentra a orquestração; teste de arquitetura evita erosão |
