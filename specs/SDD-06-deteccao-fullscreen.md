# SDD-06: Detecção de fullscreen e visibilidade automática

**Status:** Rascunho para revisão · **Etapa:** E2 · **Depende de:** SDD-03, SDD-05 · **Bloqueia:** SDD-07 (completo)

## 1. Identificador e título

SDD-06: Classificação de janelas em tela cheia por monitor, por eventos, com histerese, e ocultação e reexibição do overlay.

## 2. Objetivo

Ocultar o overlay quando houver conteúdo realmente em tela cheia no **mesmo monitor**: vídeo em tela cheia no Chrome, F11, jogos, apresentações. O overlay deve continuar visível com janelas apenas maximizadas, como o Chrome ou o VS Code maximizados. A detecção usa eventos, quase sem custo, e evita pisca-pisca.

## 3. Contexto técnico

| Situação | Retângulo da janela | `IsZoomed` | Estilo | Barra de tarefas |
|---|---|---|---|---|
| Chrome/VS Code **maximizado** | = `rcWork` (com barra visível) | true | com `WS_CAPTION` ou moldura custom | visível |
| Chrome **F11** / vídeo em tela cheia | = `rcMonitor` | false | sem `WS_CAPTION` | oculta |
| VS Code F11 / Zen | = `rcMonitor` | false | sem caption | oculta |
| Jogo borderless | = `rcMonitor` | false | `WS_POPUP` | oculta |
| Jogo exclusivo D3D | modo de vídeo próprio | — | — | `SHQueryUserNotificationState = QUNS_RUNNING_D3D_FULL_SCREEN` |
| PowerPoint em apresentação | = `rcMonitor` | false | popup | `QUNS_PRESENTATION_MODE` / `QUNS_BUSY` |
| Maximizado **com barra em ocultação automática** | = `rcMonitor` (rcWork == rcMonitor) | **true** | com caption | — |

As coordenadas são físicas (PMv2). O retângulo visível vem de `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)`, porque `GetWindowRect` inclui bordas invisíveis no Windows 10/11.

## 4. Escopo e exclusões

**Escopo:** coleta de sinais (WinEvent, AppBar, QUNS e polling de segurança), `FullscreenClassifier` (puro), rastreamento de janelas em tela cheia por monitor, histerese, política de ocultação e configurações.

**Exclusões:** o que acontece com o jogo ao ocultar (SDD-07), bloqueio de tela e sono (SDD-07) e janelas de outros desktops virtuais (tratadas como "cloaked").

## 5. Requisitos funcionais

### RF-06.1: Fontes de sinal (Platform, todas na T-Game)

1. **Foreground:** `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, cb, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)`.
2. **Minimizar/restaurar:** `EVENT_SYSTEM_MINIMIZESTART..EVENT_SYSTEM_MINIMIZEEND` (mesmas flags).
3. **Mudança de geometria da janela em foreground:** `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, …, idProcess = pid do foreground, idThread = 0, …)`, **refeito a cada mudança de foreground** (o hook anterior é removido). No callback, ignorar `idObject != OBJID_WINDOW || idChild != CHILDID_SELF || hwnd != foregroundHwnd`.
4. **Notificação do shell:** uma janela sonda top-level oculta (`AppBarProbeWindow`, sem `WS_VISIBLE`) registrada com `SHAppBarMessage(ABM_NEW)` e `uCallbackMessage = WM_APP+0x60`. `ABN_FULLSCREENAPP` (lParam TRUE/FALSE) agenda uma reavaliação; o sinal **não** é tratado como verdade.
5. **Polling de segurança:**
   - a cada **2 s** enquanto o overlay estiver visível;
   - a cada **1 s** enquanto houver janela rastreada em tela cheia (RF-06.4);
   - desligado quando o overlay estiver oculto por outro motivo que não fullscreen.
6. **Coalescência:** qualquer sinal agenda `Evaluate()` para daqui a **100 ms** (deadline único); sinais repetidos no intervalo não reagendam.

### RF-06.2: Coleta (`WindowProbe.Capture(hwnd)` em Platform → `WindowSnapshot`, em Runtime)

```csharp
public readonly record struct WindowSnapshot(
    nint Hwnd, uint ProcessId, string ClassName,           // GetClassNameW (até 64 chars; cache por hwnd)
    uint Style, uint ExStyle, bool IsZoomed, bool IsIconic, bool IsVisible,
    bool IsCloaked,                                        // DWMWA_CLOAKED != 0
    PixelRect Frame,                                       // DWMWA_EXTENDED_FRAME_BOUNDS; fallback GetWindowRect
    string MonitorDevice, PixelRect MonitorBounds, PixelRect MonitorWork);   // MonitorFromWindow(MONITOR_DEFAULTTONEAREST)
public enum Quns { Unknown, NotPresent, Busy, D3DFullscreen, PresentationMode, AcceptsNotifications, QuietTime, App }
```

### RF-06.3: Classificador (`FullscreenClassifier.Classify(WindowSnapshot w, Quns q, ownPid) → Verdict`, puro)

```
enum Verdict { Fullscreen, NotFullscreen, Ignore }   // Ignore = manter o estado anterior
Regras, em ordem:
R1  w.Hwnd == 0                                  → NotFullscreen
R2  w.ProcessId == ownPid                        → Ignore     (janelas WPF do próprio app)
R3  w.IsCloaked || !w.IsVisible                  → Ignore
R4  w.IsIconic                                   → NotFullscreen
R5  ClassName ∈ ShellTransient                   → Ignore
       ShellTransient = { "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
                          "MultitaskingViewFrame", "ForegroundStaging", "TaskListThumbnailWnd",
                          "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
                          "TopLevelWindowForOverflowXamlIsland" }
R6  ClassName ∈ Desktop = { "Progman", "WorkerW" } → NotFullscreen
R7  covers = Frame contém MonitorBounds com tolerância de 1 px em cada lado
R8  covers && !IsZoomed && (Style & WS_CAPTION) != WS_CAPTION → Fullscreen
R9  covers && !IsZoomed && (Style & WS_POPUP) != 0            → Fullscreen
R10 q ∈ { D3DFullscreen, PresentationMode }                    → Fullscreen   (monitor = w.MonitorDevice)
R11 covers && IsZoomed                                         → NotFullscreen (maximizado com ocultação automática)
R12 caso contrário                                             → NotFullscreen
```

- `QUNS_BUSY` sozinho **não** classifica, porque é global e ambíguo. Ele só fica registrado no log de diagnóstico.
- As listas `ShellTransient` e `Desktop` ficam em `Runtime/Fullscreen/ShellClasses.cs`, com comentário explicando a origem de cada classe.

### RF-06.4: Rastreamento por monitor (`FullscreenTracker`, puro + Platform para revalidar)

- Mantém `Dictionary<string monitorDevice, nint hwnd>` com as janelas classificadas como Fullscreen.
- Em cada `Evaluate()`:
  1. classificar o foreground. Se `Fullscreen`, registrar `tracked[monitor] = hwnd`;
  2. para cada janela rastreada: capturar de novo. Ela sai do rastreamento se `!IsWindow(hwnd)`, se a captura der `IsIconic`, `IsCloaked`, `!IsVisible`, se ela não cobre mais o monitor (R7 falso) ou se `IsZoomed`;
  3. **Exceção:** se a janela rastreada continua cobrindo o monitor mas o foreground mudou para **outro monitor**, ela **permanece** rastreada (por exemplo, vídeo em tela cheia no monitor 2 enquanto o usuário trabalha no 1);
  4. se o foreground estiver no **mesmo** monitor e não for a janela rastreada nem `Ignore`, verificar a ordem de empilhamento (z-order): se o foreground está acima e não cobre o monitor, a rastreada é removida (o usuário trouxe outra janela para frente no mesmo monitor, então a tela cheia deixou de ser visível).
- Saída: `IReadOnlySet<string> FullscreenMonitors`.

### RF-06.5: Política de ocultação (`FullscreenPolicy`, puro)

- `settings.behavior.hideOnFullscreen ∈ { SameMonitor (padrão), AnyMonitor, Never }`.
- `shouldHide`:
  - `SameMonitor`: `FullscreenMonitors.Contains(overlayMonitor)`;
  - `AnyMonitor`: `FullscreenMonitors.Count > 0`;
  - `Never`: false.
- **Histerese:** entrar em "oculto por fullscreen" exige `shouldHide` estável por **250 ms**. Sair exige `!shouldHide` estável por **600 ms**. Implementar com timestamps do `IClock`.
- O resultado emite `VisibilitySignal.FullscreenEntered` / `FullscreenExited`, consumido por SDD-07 como o motivo de ocultação `HiddenReason.Fullscreen`.

### RF-06.6: Diagnóstico

- No log em nível Debug: cada veredito com `ClassName`, `Frame`, `MonitorBounds`, `IsZoomed`, `Style` e `Quns`, com no máximo 1 linha por segundo por hwnd.
- A tela de diagnóstico (SDD-16) mostra o estado atual: `FullscreenMonitors`, foreground e veredito.

## 6. Requisitos não funcionais

- **RNF-06.1:** O custo de `Evaluate()` é ≤ 200 µs (meta; benchmark em Windows).
- **RNF-06.2:** Com o overlay visível e sem mudança de foreground, os wakes atribuíveis à detecção ficam em ≤ 0,5/s (polling de 2 s).
- **RNF-06.3:** A latência do F11 até o overlay oculto é ≤ 500 ms. A latência da saída do F11 até o overlay visível é ≤ 1 s (meta; MTC).
- **RNF-06.4:** Zero falsos positivos nos cenários de "maximizado" da matriz MTC-06.

## 7. Estratégia de implementação

1. `WindowSnapshot`, `FullscreenClassifier`, `FullscreenTracker` (lógica pura com uma porta `IWindowProbe` para revalidar) e `FullscreenPolicy` em Runtime, com testes de tabela no Linux, incluindo **fixtures reais**: na E2, capturar com uma ferramenta de diagnóstico (comando `--dump-foreground` do App) os snapshots do Chrome maximizado, Chrome F11, YouTube em tela cheia, VS Code maximizado/F11/Zen, PowerPoint em apresentação, Explorer, área de trabalho, Win+Tab e menu Iniciar no Windows 11 do usuário. Salvar como JSON em `tests/SideWar.Runtime.Tests/Fixtures/windows/*.json`.
2. `ForegroundWatcher`, `AppBarProbeWindow`, `WindowProbe` e `QunsReader` em Platform.
3. Integrar no host: deadlines de coalescência, polling e histerese.

## 8. Dependências

SDD-03 (laço e janelas), SDD-05 (`MonitorInfo` e device name do overlay). SDD-07 consome os sinais.

## 9. Critérios de aceitação

- **CA-06.1:** Os testes de tabela com as fixtures reais passam e classificam todos os casos da matriz conforme a coluna "esperado".
- **CA-06.2:** Na matriz MTC-06 (abaixo), 100% dos casos se comportam como esperado no Windows 11 do usuário, com registro em `specs/evidencias/E2.md`.
- **CA-06.3:** Alternar F11 no Chrome 20 vezes seguidas (1 por segundo) não deixa o overlay em estado errado ao final e não provoca mais de 1 transição por alternância (histerese).
- **CA-06.4:** Com 2 monitores, vídeo em tela cheia no monitor 2 e overlay no monitor 1 (modo `SameMonitor`), o overlay continua visível. Com o overlay no monitor 2, ele fica oculto **mesmo quando o foco vai para uma janela do monitor 1**.

### Matriz MTC-06 (manual, Windows 11)

| # | Ação | Esperado (overlay no mesmo monitor) |
|---|---|---|
| 1 | Chrome maximizado | visível |
| 2 | Chrome F11 | oculto |
| 3 | Vídeo do YouTube em tela cheia (botão do player) | oculto |
| 4 | Sair do F11 (Esc) | visível em ≤ 1 s |
| 5 | VS Code maximizado | visível |
| 6 | VS Code F11 e Zen Mode | oculto |
| 7 | Barra em ocultação automática + Chrome maximizado | visível |
| 8 | Abrir o menu Iniciar com o Chrome em F11 | continua oculto (Ignore) |
| 9 | Win+Tab / Alt+Tab | sem mudança (Ignore) |
| 10 | Clicar na área de trabalho | visível |
| 11 | Jogo borderless (qualquer) ou janela de teste `TestFullscreenWindow` (popup do tamanho do monitor) | oculto |
| 12 | PowerPoint F5 (se disponível) | oculto |
| 13 | Vídeo em tela cheia no monitor 2 e foco no monitor 1 | conforme CA-06.4 |
| 14 | Minimizar a janela em tela cheia | visível |

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-06.1 | Classificador R1–R12 com snapshots sintéticos (casos limite: tolerância de 1 px, frame maior que o monitor, monitor com origem negativa) | Linux |
| T-06.2 | Classificador com as fixtures reais | Linux |
| T-06.3 | Tracker: multi-monitor, foco em outro monitor, janela destruída, minimizada | Linux (probe falso) |
| T-06.4 | Histerese com FakeClock: rajadas de alternância | Linux |
| T-06.5 | `TestFullscreenWindow` (processo auxiliar) abre, fecha, minimiza → sinais chegam em ≤ 500 ms | WindowsInteractive |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Classes de janela do shell mudarem em builds futuros do Windows 11 | Média | Médio | Lista centralizada; regra conservadora (`Ignore` mantém o estado); diagnóstico `--dump-foreground` |
| Chrome F11 manter `WS_CAPTION` em alguma versão | Baixa | Alto | R9 e fixtures reais; caso falhe, adicionar regra específica com ADR |
| `EVENT_OBJECT_LOCATIONCHANGE` ruidoso (por exemplo, apps que se movem com frequência) | Média | Baixo | Hook por processo + coalescência de 100 ms |
| Jogos com anticheat que reagem a hooks | Baixa | Médio | Usamos apenas WinEvent out-of-context (sem injeção); documentar |
| UWP/Store em tela cheia (ApplicationFrameWindow) | Média | Médio | Fixture específica na E2 |
