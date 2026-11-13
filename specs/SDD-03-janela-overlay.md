# SDD-03: Janela do overlay, GameThread e integração Win32 base

**Status:** Rascunho para revisão · **Etapa:** E1 (MVP técnico) · **Depende de:** SDD-01, SDD-02 (revisada pelo design idle: SDD-21, SDD-22, SDD-23, ADR-018 a ADR-030) · **Bloqueia:** SDD-04, 05, 06, 07, 08, 10, 15, 21, 22, 23

## 1. Identificador e título

SDD-03: Criação e posse das janelas do overlay, laço de mensagens da GameThread, topmost e não ativação (não roubar o foco).

## 2. Objetivo

Ter uma janela sempre visível sobre outros aplicativos que **nunca** se torna a janela ativa por iniciativa própria, que não aparece na barra de tarefas nem no Alt+Tab e que é governada por uma thread dedicada de baixo consumo.

## 3. Contexto técnico

- ADR-001 (janela *layered*), ADR-002 (GameThread), ADR-021 (conjunto de janelas layered Skia; substitui ADR-011), ADR-028 (sem HandleWindow), ADR-015 (não roubar o foco, refinada).
- Windows 11: janelas `WS_POPUP` sem moldura não recebem cantos arredondados, mas o DWM pode animar transições. Por isso as transições são desligadas.
- Toda interop é via CsWin32 (ADR-005). **Os delegates de WndProc e WinEvent precisam ser mantidos vivos em campos**, para não serem coletados pelo GC.

## 4. Escopo e exclusões

**Escopo:** `GameThread`, registro de classe de janela, `OverlayWindow` (a janela da faixa), conjunto de janelas do jogo (RF-03.9), estilos, z-order, não ativação, captura de mouse, *waitable timer*, despertar por evento e encerramento da thread.

**Exclusões:**

- transparência e modos de clique (SDD-04);
- posicionamento e DPI (SDD-05);
- desenho (SDD-08);
- fullscreen (SDD-06);
- energia e sessão (SDD-07).

## 5. Requisitos funcionais

### RF-03.1: GameThread

- `GameThread.Start()` cria uma `Thread` com nome `SideWar.Game`, `IsBackground=false` e `ApartmentState.STA` (não usa COM, mas STA evita surpresas com shell APIs).
- Dentro da thread:
  1. `SetThreadDescription` (ajuda o diagnóstico);
  2. criar o timer `CreateWaitableTimerExW(null, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS)`. Se falhar (Windows antigo), usar `CreateWaitableTimerExW` sem a flag e registrar um aviso no log;
  3. criar o `wakeEvent` (`AutoResetEvent` gerenciado → `SafeWaitHandle`);
  4. registrar as classes de janela e criar as janelas (RF-03.2 e RF-03.3);
  5. sinalizar `Ready` (um `TaskCompletionSource` aguardado pela T-UI com timeout de 5 s);
  6. entrar no laço RF-03.4.
- **Proibido:** `timeBeginPeriod`, `Thread.Sleep` no laço, `SpinWait` e espera ativa.

### RF-03.2: OverlayWindow (criação)

> **Refinado por SDD-22 RF-22.1 (ADR-021).** A `OverlayWindow` passa a ser a janela da **faixa** (`SceneWindow`, classe `SideWar.Scene`), com os mesmos estilos e atributos DWM abaixo; a geometria vem de SDD-22 (96 DIP, largura do monitor), não de SDD-05 RF-05.4.

- Classe `SideWar.Overlay`: `CS_DBLCLKS` desligado, `hCursor = IDC_ARROW`, `hbrBackground = null`.
- `CreateWindowExW` com:
  - `dwExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST` (no modo passivo, SDD-04 acrescenta `WS_EX_TRANSPARENT`);
  - `dwStyle = WS_POPUP` (**sem** `WS_VISIBLE`);
  - posição e tamanho iniciais 0,0,1,1 (SDD-05 aplica a geometria real antes do primeiro Show).
- Logo após criar:
  - `DwmSetWindowAttribute(DWMWA_TRANSITIONS_FORCEDISABLED, TRUE)`;
  - `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_DONOTROUND)`;
  - `DwmSetWindowAttribute(DWMWA_EXCLUDED_FROM_PEEK, TRUE)` (o Aero Peek não esconde o jogo de forma estranha).
- Mostrar sempre com `ShowWindow(hwnd, SW_SHOWNOACTIVATE)` seguido de `SetWindowPos(hwnd, HWND_TOPMOST, …, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE | SWP_NOOWNERZORDER)`.
- Ocultar com `ShowWindow(hwnd, SW_HIDE)`.

### RF-03.3: HandleWindow

> **Substituída por RF-03.9 (ADR-021, ADR-028).** A HandleWindow não existe mais: o widget HUD (SDD-23) cumpre o papel de ponto de interação e o selo de ausente é uma janela própria click-through (SDD-22 RF-22.7). O texto abaixo fica como histórico e **não** é implementado.

- É uma classe separada (`SideWar.Handle`), com os mesmos estilos da OverlayWindow, **exceto `WS_EX_TRANSPARENT`, que nunca é aplicado**.
- Criada com `hWndParent = OverlayWindow`, o que a torna *owned* e a mantém sempre acima da dona. Como a dona tem `WS_EX_TOOLWINDOW`, nenhuma das duas aparece na barra de tarefas.
- Tamanho: 28×28 DIP (escalado por DPI, SDD-05). A posição é definida por SDD-05 e o desenho por SDD-08/15.
- É mostrada e ocultada junto com a OverlayWindow.

### RF-03.4: Laço de mensagens da GameThread

```
while (!stopping):
    handles = [timer, wakeEvent]
    timeout = nextDeadline == null ? INFINITE : max(0, ms até nextDeadline)
    r = MsgWaitForMultipleObjectsEx(2, handles, timeout, QS_ALLINPUT, MWMO_INPUTAVAILABLE)
    while PeekMessageW(&msg, NULL, 0, 0, PM_REMOVE):
        if msg.message == WM_QUIT: stopping = true; break
        TranslateMessage(&msg); DispatchMessageW(&msg)
    host.OnWake(now)            # comandos, ticks, render (SDD-10)
    nextDeadline = host.NextDeadline   # null = esperar por evento
    if nextDeadline != null: SetWaitableTimer(timer, relativo(-100ns * delta), 0, null, null, false)
```

- O timer é *one-shot* e é rearmado a cada iteração. Não se usa período fixo, para evitar acordar sem necessidade.
- `GameThread.Post(Action)` (uso interno da Platform) e `IGameCommandSink.Post` colocam o item na fila e chamam `wakeEvent.Set()`.

### RF-03.5: Não ativação (não roubar o foco)

O WndProc de **todas as janelas do jogo** (RF-03.9) implementa:

| Mensagem | Tratamento |
|---|---|
| `WM_MOUSEACTIVATE` | retornar `MA_NOACTIVATE` |
| `WM_NCACTIVATE` / `WM_ACTIVATE` | default (não deveriam ocorrer); registrar no log em Debug se ocorrerem |
| `WM_SETFOCUS` | se ocorrer, registrar e chamar `SetFocus(null)` não é permitido; apenas registrar (diagnóstico de bug) |
| `WM_LBUTTONDOWN` / `WM_RBUTTONDOWN` | `SetCapture(hwnd)` somente se a entrada começar uma interação (SDD-15) |
| `WM_CAPTURECHANGED` | cancelar a interação em andamento (arraste) |
| `WM_SETCURSOR` | aplicar o cursor solicitado pela entrada (seta, mão ou proibido) e retornar TRUE |
| `WM_NCHITTEST` | retornar `HTCLIENT` (o default de popup) |

- **Nunca chamar** `SetForegroundWindow`, `SetActiveWindow`, `SetFocus`, `BringWindowToTop` nem `SwitchToThisWindow` nessas janelas.
- O teclado não é capturado pelo overlay. As teclas do jogo usam hotkeys globais (SDD-15).

### RF-03.6: Reafirmação de topmost

> **Refinado por SDD-23 RF-23.10.** O widget e as demais janelas do jogo precisam ficar **acima da barra de tarefas** (`Shell_TrayWnd`); a reafirmação também é disparada quando a barra (ou o Menu Iniciar/Pesquisa) vira foreground, com debounce de 100 ms e ordem fixa (RF-03.9). O debounce de 1 s abaixo vale para os demais gatilhos.

- Na mudança de foreground (evento vindo de SDD-06) e após `WM_DISPLAYCHANGE`, chamar `SetWindowPos(overlay, HWND_TOPMOST, …, SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOOWNERZORDER)`, limitado a **1 vez por segundo** (debounce).
- Não reafirmar quando oculto.

### RF-03.7: Encerramento da GameThread

- `GameThread.StopAsync(timeout: 3 s)` executa em sequência:
  1. posta a ação de parada;
  2. `host.Shutdown()` (SDD-07: pausa e entrega o snapshot para salvar);
  3. `DestroyWindow` das janelas owned (RF-03.9) e depois da `OverlayWindow`;
  4. `UnregisterClassW`;
  5. fecha o timer;
  6. `PostQuitMessage(0)`;
  7. a thread termina.
- A T-UI faz `Join` com timeout. Se o timeout estourar, registrar um erro e seguir com `Environment.Exit(0)` depois do save de emergência (SDD-07).

### RF-03.8: Exceções na GameThread

- Envolver `host.OnWake` e o WndProc em `try/catch` que encaminha a exceção ao `CrashHandler` (SDD-07).
- Uma exceção no WndProc **não** pode propagar para o código nativo: capturar, registrar e retornar `DefWindowProcW`.

### RF-03.9: Conjunto de janelas do jogo (ADR-021, ADR-028)

Todas as janelas abaixo são criadas e possuídas pela **T-Game**, são `WS_POPUP` sem `WS_VISIBLE`, usam os mesmos `DwmSetWindowAttribute` de RF-03.2, `WM_MOUSEACTIVATE → MA_NOACTIVATE` e superfície `LayeredSurface` própria. **Nenhuma** aparece na barra de tarefas nem no Alt+Tab (`WS_EX_TOOLWINDOW`). WPF fica apenas para Configurações e Sobre.

| Janela | Ficha | `dwExStyle` | Cliques | Opacidade global | Ownership |
|---|---|---|---|---|---|
| **Faixa** (`OverlayWindow`/`SceneWindow`; ADR-034: cena acima da barra **e** faixa sobre a barra) | SDD-22 | `LAYERED\|TOOLWINDOW\|NOACTIVATE\|TOPMOST` (+`TRANSPARENT` em Passivo) | só nos pixels de torre em Interativo, e a janela inteira no modo construção (SDD-04, SDD-22 RF-22.11; antes torre/slot vazio) | 255 presente, 128 ausente | **dona** de todas as outras; sem owner |
| **Widget HUD** (48 DIP de altura, largura natural, à direita acima da barra; ADR-034, antes 420×48) | SDD-23 | `LAYERED\|TOOLWINDOW\|NOACTIVATE\|TOPMOST` | sempre clicável | 255 | owned pela faixa |
| **Painel** (4 abas) e **cards** | SDD-24 | idem | sempre clicáveis | 255 | owned pela faixa |
| **Banners e caixas** (hotspots) | SDD-22 RF-22.8, SDD-26 | idem | sempre clicáveis (alfa por pixel) | 255 | owned pela faixa |
| **Selo de ausente** (`SceneSealWindow`) | SDD-22 RF-22.7 | `LAYERED\|TOOLWINDOW\|NOACTIVATE\|TOPMOST\|TRANSPARENT` | **nunca** (click-through) | 255 | owned pela faixa |

- **Z-order (ADR-032):** um único **`ZOrderCoordinator`** em `Platform.Windows` é dono da reafirmação de z-order de faixa, widget, painel e cards, na ordem de SDD-23 RF-23.10; nenhuma outra classe chama `SetWindowPos(HWND_TOPMOST)` para esse fim.
- **Ownership:** todas as janelas do conjunto são *owned* pela janela da faixa, na mesma thread (T-Game; ADR-002 proíbe owner entre threads). Assim ficam acima da faixa, são ocultadas e destruídas junto com ela e não criam entradas na barra de tarefas.
- **Z-order de reafirmação** (rotina única compartilhada, DP-23.6): faixa, widget, painel e cards, banners/caixas, selo. As de cima ficam acima das de baixo, **e todas acima da barra de tarefas** (`Shell_TrayWnd`), como em SDD-23 RF-23.10.
- **Aparecem sozinhas** (sem ação do usuário) apenas: o card de retorno (D-11), banners, caixas e o selo de ausente. Todas continuam não ativáveis (ADR-015 refinada).
- O WndProc de cada janela cobre também `WM_NCHITTEST → HTCLIENT`, `WM_SETCURSOR`, `SetCapture` só dentro de um controle e `WM_CAPTURECHANGED`, conforme RF-03.5.

## 6. Requisitos não funcionais

- **RNF-03.1:** Com o overlay visível, o jogo parado e sem animações, a GameThread fica bloqueada: no máximo 1 wake/s, excluindo mensagens de entrada (medido por contador interno de wakes; meta).
- **RNF-03.2:** Criar a janela da faixa e o primeiro Show leva ≤ 150 ms (as demais janelas são criadas no primeiro uso, SDD-22 RF-22.8) após `Start()` (meta).
- **RNF-03.3:** Zero `WM_SETFOCUS` e zero mudanças de foreground causadas pelo processo em 100 cliques no overlay (CA-03.2).

## 7. Estratégia de implementação

1. Em `NativeMethods.txt`, listar: `CreateWindowExW, RegisterClassExW, UnregisterClassW, DefWindowProcW, DestroyWindow, ShowWindow, SetWindowPos, GetWindowLongPtrW, SetWindowLongPtrW, MsgWaitForMultipleObjectsEx, PeekMessageW, TranslateMessage, DispatchMessageW, PostQuitMessage, PostMessageW, CreateWaitableTimerExW, SetWaitableTimer, CancelWaitableTimer, SetCapture, ReleaseCapture, TrackMouseEvent, SetCursor, LoadCursorW, DwmSetWindowAttribute, SetThreadDescription, GetForegroundWindow, GetWindowThreadProcessId`, além das constantes `WS_*`, `WM_*`, `MA_*`, `HTCLIENT`, `DWMWA_*`, `DWMWCP_*` e `CREATE_WAITABLE_TIMER_*`.
2. Implementar `Win32Window` (base: registro de classe, WndProc estático que despacha para a instância via `GWLP_USERDATA` com `GCHandle`) e as derivadas `OverlayWindow` (faixa) e as janelas de RF-03.9 (a `HandleWindow` foi removida, ADR-028).
3. Implementar `GameThread` e o laço RF-03.4 com um `IGameLoopHost` mínimo de teste, que desenha um quadro sólido (SDD-08 entra depois).
4. Criar um "Harness E1" no `App`: inicia a GameThread, mostra o overlay com retângulo animado e encerra pela bandeja (bandeja mínima de SDD-15) ou por `Ctrl+C` no console de debug.
5. Escrever os testes de `Platform.Windows.Tests` (seção 10).

## 8. Dependências

SDD-01 e SDD-02. Para testes visuais completos, SDD-04/05/08 em paralelo na mesma etapa E1.

## 9. Critérios de aceitação

- **CA-03.1:** O overlay aparece sobre Chrome (normal e maximizado) e VS Code, **acima da barra de tarefas** no caso do widget (SDD-23 RF-23.10), e **não** aparece na barra de tarefas nem no Alt+Tab (MTC-03.1).
- **CA-03.2:** Com o Bloco de Notas em foco digitando, 100 cliques e arrastes no overlay em modo interativo não mudam `GetForegroundWindow()` e o texto continua indo para o Bloco de Notas (teste automatizado T-03.2 com `SendInput`, mais MTC-03.2).
- **CA-03.3:** Encerrar pela bandeja finaliza o processo em ≤ 3 s, sem janelas órfãs e com exit code 0.
- **CA-03.4:** Com o jogo parado, o contador de wakes registra ≤ 1/s durante 60 s (RNF-03.1).
- **CA-03.5:** `GetWindowLongPtr(GWL_EXSTYLE)` contém `LAYERED|TOOLWINDOW|NOACTIVATE|TOPMOST`, e as demais janelas de RF-03.9 têm owner igual à OverlayWindow (`GetWindow(GW_OWNER)`) e os estilos da tabela de RF-03.9; não existe HandleWindow.

## 10. Cenários de teste

| ID | Cenário | Tipo / Env |
|---|---|---|
| T-03.1 | Estilos e owner de todas as janelas de RF-03.9 (CA-03.5) | Automatizado, WindowsInteractive |
| T-03.2 | Abrir o processo auxiliar `TestFocusTarget` (janela WPF com TextBox), focá-lo, clicar no overlay com `SendInput` e verificar foreground e TextBox inalterados | Automatizado, WindowsInteractive |
| T-03.3 | Stop/Start da GameThread 20 vezes sem vazamento de handles (`GetGuiResources(GR_USEROBJECTS)` estável ±2) | Automatizado, WindowsInteractive |
| T-03.4 | Exceção lançada dentro do WndProc não derruba o processo e é registrada no log | Automatizado, WindowsInteractive |
| MTC-03.1 | Taskbar e Alt+Tab (Win+Tab também) | Manual |
| MTC-03.2 | Digitar no VS Code enquanto clica em torres e no widget (a faixa em Interativo) | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| `WS_EX_NOACTIVATE` com `SetCapture` não entregar `WM_MOUSEMOVE` fora da janela durante o arraste | Baixa | Alto | Validar em T-03.2 na E1; fallback com `TrackMouseEvent` e polling do cursor a 60 Hz apenas durante o arraste |
| Outra janela topmost (por exemplo, Teams ou um player) ficar acima | Média | Médio | Reafirmação de topmost (RF-03.6); documentar a limitação |
| GC coletar delegate nativo (crash intermitente) | Média | Alto | Campos `static readonly` para os delegates; teste T-03.3 com `GC.Collect` forçado entre os ciclos |
| Barra de tarefas (topmost) sobrepor a faixa com ocultação automática ativa | Média | Baixo | SDD-05 (margem configurável) |
