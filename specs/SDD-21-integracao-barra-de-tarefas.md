# SDD-21: Integração com a barra de tarefas (TaskbarLayout, slots de torre e fallback)

**Status:** Rascunho para revisão · **Etapa:** E1 (MVP técnico; faz parte dele como **risco a validar**) · **Depende de:** SDD-03, SDD-05 · **Bloqueia:** SDD-22, SDD-23

## 1. Identificador e título

SDD-21: Obtenção da geometria da barra de tarefas do Windows (`TaskbarLayout`), classificação dos botões, zona livre à esquerda, bandeja, slots de torre e fallback virtual.

## 2. Objetivo

Descobrir, **por evento e sem polling contínuo**, onde estão a barra de tarefas e os botões dos aplicativos, para que:

- cada botão de aplicativo vire um **slot de torre** (SDD-22);
- o widget HUD (SDD-23) saiba qual é a zona livre à esquerda da barra;
- o portal de entrada dos inimigos (SDD-22) fique ao lado da bandeja.

Quando a barra não puder ser lida com segurança, o jogo continua funcionando com **8 slots virtuais** (fallback). Esta ficha também define o comando de diagnóstico que grava fixtures reais do Windows 11 do usuário para testes no Linux.

## 3. Contexto técnico

- **Decisões:** ADR-018 (design oficial, tokens em SDD-20), ADR-020 (geometria: faixa de cena de 96 DIP acima da barra, widget HUD 420×48 sobre a zona livre à esquerda, slots = botões de apps da barra com fallback virtual), ADR-023 (UI Automation **somente leitura** para a geometria da barra, consultada por evento, com fallback). Respeita ADR-005 (CsWin32), ADR-014 (sem rede) e ADR-015 (nunca roubar foco).
- **Distinção importante:** ADR-009/SDD-15 dizem que a HUD do jogo **não** expõe UI Automation. Aqui o UIA é usado no sentido inverso: o jogo **lê** a árvore da barra do Explorer. Não há `SetFocus`, `Invoke`, `Select` nem qualquer padrão de controle. Só `BoundingRectangle`, `ClassName`, `AutomationId` e `ControlType`.
- No Windows 11, a barra de tarefas é uma ilha XAML hospedada em `Shell_TrayWnd`. Os botões **não são janelas Win32 filhas**, então a geometria vem do UIA. Os nomes de classe e de `AutomationId` abaixo vêm da especificação do Tech Lead e **precisam ser confirmados pelas fixtures reais** (RF-21.9). A lista de classificação fica centralizada (RF-21.4) por esse motivo.
- As coordenadas do UIA (`BoundingRectangle`) e de `GetWindowRect` são **físicas** (processo PMv2, SDD-05). O `TaskbarLayout` converte tudo para DIP relativo à origem do monitor (RF-21.1).
- Os requisitos de baixo consumo continuam valendo (SDD-03 RNF-03.1, SDD-06 RNF-06.2, SDD-07 RNF-07.1): nenhum timer periódico curto, nenhuma consulta com o overlay oculto.
- O design de referência (`Strip.dc.html`, 1440 DIP) mostra os 8 ícones da barra em x = 640 + 48·i (botão 44, intervalo 4), o widget em [0, 420) e a bandeja em [1240, 1440). Esses números só servem de exemplo para o fallback (RF-21.7).

### Substituições e conflitos com fichas anteriores (o design vence)

| Ficha antiga | O que muda | Tratamento |
|---|---|---|
| SDD-05 RF-05.7 | Detecção da borda e de `ABS_AUTOHIDE` pela própria ficha SDD-05 | **Não substituído.** SDD-21 consome o mesmo `ABM_GETSTATE` e **acrescenta** os botões. A margem de 2 px com ocultação automática de RF-05.7 continua valendo (SDD-22) |
| SDD-01 RF-01.4 (modelo de threads) | Nova thread `SideWar.Shell` (MTA) | **Substitui SDD-01 RF-01.4** apenas por acréscimo de uma linha na tabela de threads (ver seção 11, "Fichas a revisar") |
| SDD-11 RF-11.9 (`PlacementValidator`, posição livre x,y) e SDD-15 (arraste de torre da paleta) | Torre passa a ser presa a um **índice de slot** | **Substitui SDD-11 RF-11.9** para este mapa; efeitos em SDD-11 RF-11.10 (comandos de construção) e SDD-14 (campo `slotIndex` no save) a revisar em SDD-22/SDD-25 |

## 4. Escopo e exclusões

**Escopo:** modelo `TaskbarLayout`, localização das janelas da barra por monitor, consulta UIA em thread dedicada, `TaskbarClassifier` (puro), cálculo de zona livre, bandeja e alinhamento, gatilhos de atualização, `SlotMap` (slots reais e virtuais), vínculo torre↔índice de slot, interruptor de configuração, comando `--dump-taskbar` e fixtures.

**Exclusões:**

- geometria e desenho da janela de cena (SDD-22);
- posição e conteúdo do widget HUD (SDD-23). Esta ficha só entrega `leftFreeZone` e `alturaDIP`;
- regras de combate (SDD-11) e persistência do `slotIndex` (SDD-14);
- qualquer ação **sobre** a barra: mover, ocultar, fixar ou desafixar aplicativos. É proibido;
- Windows 10 (a validação é só no Windows 11; o classificador aceita `MSTaskListWClass` por compatibilidade, sem garantia);
- idiomas com leitura da direita para a esquerda (barra espelhada).

## 5. Requisitos funcionais

### RF-21.1: Modelo (`TaskbarLayout`, em Runtime, tipo imutável)

```csharp
public enum TaskbarEdge { Bottom, Top, Left, Right, Unknown }
public enum TaskbarAlignment { Center, Left }
public enum TaskbarSource { Uia, Fallback }

public readonly record struct DipRect(float X, float Y, float W, float H);   // DIP, origem = canto superior esquerdo do monitor

public readonly record struct AppButton(int Order, DipRect Rect);            // Order = 0..n-1, da esquerda para a direita

public readonly record struct TaskbarLayout(
    string MonitorDevice,                 // MonitorInfo.DeviceName (SDD-05)
    TaskbarEdge Edge,
    bool AutoHide,                        // ABM_GETSTATE & ABS_AUTOHIDE
    DipRect TaskbarRect,
    float HeightDip,                      // "alturaDIP": altura (borda inferior/superior) ou largura (bordas laterais) da barra
    IReadOnlyList<AppButton> AppButtons,  // só botões de aplicativo (RF-21.4), ordenados por X
    DipRect? WidgetsButton,               // botão "Widgets", se existir (informativo)
    (float X0, float X1) LeftFreeZone,    // intervalo livre à esquerda, em DIP desde a borda esquerda do monitor (RF-21.5)
    float TrayLeftX,                      // menor X dos elementos da bandeja (RF-21.5)
    TaskbarAlignment Alignment,
    TaskbarSource Source,
    uint Dpi);                            // DPI efetivo do monitor, para converter de volta a pixels
```

- Conversão: `dip = px_físico / (Dpi / 96)`, medida a partir de `MonitorInfo.Bounds` (SDD-05). Como a barra inferior não altera `WorkArea.Left`, o X em DIP do `TaskbarLayout` coincide com o X da faixa de cena de SDD-22.
- Igualdade para decidir "mudou?": comparar `Edge`, `AutoHide`, `Source`, número de botões e cada retângulo com tolerância de **0,5 DIP**. Se nada mudou, **não** notificar (nenhum redesenho, RF-21.6).
- Em `Source = Fallback`: `AppButtons` vem vazio, e `SlotMap` (RF-21.7) usa os slots virtuais.

### RF-21.2: Localização da barra por monitor (Platform, thread T-Game)

1. **Primário:** `FindWindowW("Shell_TrayWnd", null)`. **Demais monitores:** `FindWindowExW(null, prev, "Shell_SecondaryTrayWnd", null)` em laço até esgotar.
2. Para cada janela encontrada: `GetWindowRect` (físico) e `MonitorFromWindow(MONITOR_DEFAULTTONEAREST)` → `MonitorInfo` (SDD-05). Guardar `hwnd → monitor`.
3. **Borda:**
   - barra primária: `SHAppBarMessage(ABM_GETTASKBARPOS)` → `uEdge` (`ABE_BOTTOM`, `ABE_TOP`, `ABE_LEFT`, `ABE_RIGHT`);
   - barra secundária (a API só responde pela primária): inferir comparando o retângulo da barra com `MonitorInfo.Bounds`: encostada na borda inferior com largura igual à do monitor → `Bottom`, e assim por diante;
   - qualquer inconsistência → `Unknown`.
4. **Ocultação automática:** `SHAppBarMessage(ABM_GETSTATE)` e `ABS_AUTOHIDE`. O resultado vale para todas as barras.
5. **Monitor do jogo sem barra** (por exemplo, "mostrar a barra em todos os monitores" desligado no Windows): `Edge = Unknown`, `Source = Fallback`.
6. As chamadas de RF-21.2 são baratas e síncronas, e podem rodar na T-Game. O que usa UIA **nunca** roda aqui (RF-21.3).

### RF-21.3: Consulta UIA (`TaskbarProbe` em Platform, thread `SideWar.Shell`)

- **Thread:** `Thread` com nome `SideWar.Shell`, `IsBackground=true`, `ApartmentState.MTA`. Criada **sob demanda** na primeira consulta e encerrada junto com o app. Bloqueia em um evento (sem timer, sem polling). **Proibido** chamar UIA na T-UI (WPF) ou na T-Game.
- **Interop:** `IUIAutomation` (CLSID `CUIAutomation8`) via CsWin32 (ADR-005); se algum tipo não existir nos metadados, P/Invoke manual documentado em `NativeMethods.txt`.
- **Consulta** (uma por pedido):
  1. `ElementFromHandle(hwndDaBarra)`;
  2. `CacheRequest` com `ClassName`, `AutomationId`, `ControlType`, `BoundingRectangle` e o **pai** (`TreeScope_Element | TreeScope_Parent`), para uma única viagem entre processos;
  3. `FindAllBuildCache(TreeScope_Descendants, condição ControlType == Button, cacheRequest)`;
  4. devolver `UiaNode[]` (`ClassName`, `ParentClassName`, `AutomationId`, `Rect` físico). **O `Name` do elemento não é lido** (ele contém nomes de aplicativos do usuário).
- **Timeout:** a T-Game espera a resposta por no máximo **300 ms** (deadline do host, sem bloquear). Se estourar: resultado descartado e a consulta conta como **falha** (RF-21.3, "Falhas"; o fallback só vale após 3 falhas seguidas, ADR-023). Enquanto a consulta anterior não voltar, nenhuma nova é emitida (a thread nunca acumula pedidos); a chegada tardia de uma resposta é ignorada.
- **Liberação:** o `IUIAutomation` e os elementos COM são criados por consulta e liberados (`Marshal.ReleaseComObject`) ao final. Se a medição da E1 (RNF-21.3) mostrar que recriar custa mais do que manter, a decisão de manter a instância viva é tomada com a medição anexada (sem ADR, pois é detalhe de implementação, mas registrada em `specs/evidencias/E1.md`).
- **Falhas (ADR-023):** qualquer exceção COM (`UIA_E_ELEMENTNOTAVAILABLE`, `RPC_E_DISCONNECTED`, Explorer reiniciando) ou timeout conta como **uma falha**; o contador zera a cada consulta bem-sucedida. Com 1 ou 2 falhas seguidas, mantém-se o último layout `Uia` (se nunca houve, `Fallback`) e agenda-se **uma** nova tentativa por deadline do host em 5 s. **Na 3ª falha seguida, `Source = Fallback`** (RF-21.7). Log em Debug, no máximo 1 linha por 10 s.

### RF-21.4: Classificação (`TaskbarClassifier.Classify(UiaNode[], context) → TaskbarLayout`, puro, em Runtime)

Regras em ordem, por nó:

| Ordem | Regra | Categoria |
|---|---|---|
| C1 | `AutomationId ∈ { StartButton, SearchButton, TaskViewButton, WidgetsButton, CopilotButton }` | **Sistema** (não vira slot) |
| C2 | `ClassName` começa com `SystemTray.` **ou** a área é a de notificação (ancestral da área de notificação) | **Bandeja** |
| C3 | `ClassName` contém `TaskListButton` **ou** `ParentClassName == "MSTaskListWClass"` | **App** (vira slot) |
| C4 | demais | **Desconhecido** (ignorado; entra no diagnóstico) |

- Elementos com retângulo vazio, fora do retângulo da barra ou com largura/altura < 8 px físicos são descartados.
- `AppButtons` = elementos C3 ordenados por `Rect.X` crescente, `Order` = posição na ordenação. Empate de X: menor Y.
- As listas `{StartButton…}`, `"SystemTray."`, `"TaskListButton"` e `"MSTaskListWClass"` ficam em `Runtime/Taskbar/TaskbarClasses.cs`, com comentário explicando a origem de cada uma (como `ShellClasses.cs` de SDD-06).
- **Sem `StartButton` na lista**, a classificação falha (a árvore não é a esperada): `Source = Fallback`.
- `WidgetsButton` (C1) preenche `TaskbarLayout.WidgetsButton`.

### RF-21.5: Zona livre, bandeja e alinhamento (parte do `TaskbarClassifier`)

- **`TrayLeftX`** = menor `Left` entre os elementos C2. Sem nenhum C2: `TrayLeftX = TaskbarRect.Right − 200` (mesma convenção do fallback, ver RF-21.7) e registro em Debug.
- **Borda esquerda efetiva (`edgeL`):** se `WidgetsButton` existe **no canto esquerdo** (`Left − TaskbarRect.Left ≤ 16 DIP`), `edgeL = WidgetsButton.Right`; senão `edgeL = TaskbarRect.Left`.
- **`LeftFreeZone`** = `[edgeL, firstLeft)`, onde `firstLeft` é o menor `Left` entre os elementos C1 (exceto Widgets no canto esquerdo) e C3. Se `firstLeft ≤ edgeL`, a zona tem largura 0.
- **`Alignment`:** `Left` se `StartButton.Left − edgeL ≤ 16 DIP`; caso contrário `Center`. O limiar de 16 DIP é **valor inicial** a confirmar com as fixtures (CA-21.1).
- Barra em borda lateral (`Left`/`Right`): `LeftFreeZone = (0, 0)`; o classificador não tenta interpretar barras verticais (cai em fallback, RF-21.7).
- Se a largura de `LeftFreeZone` for menor que 420 DIP (widget HUD), a decisão de posicionar o widget é de SDD-23 (ver Decisões pendentes).

### RF-21.6: Gatilhos de atualização (sem polling contínuo)

Todo gatilho apenas **agenda uma reavaliação** (deadline único do host, **500 ms** após o último gatilho; sinais repetidos reagendam o mesmo deadline). A reavaliação executa RF-21.2 e, se aplicável, RF-21.3.

| Gatilho | Origem |
|---|---|
| Início do app e cada `Show` após `Hide` (SDD-07 RF-07.6, passo 1) | imediato (sem debounce), 1 consulta |
| `WM_SETTINGCHANGE` com `wParam == SPI_SETWORKAREA` **ou** `lParam` apontando para a string `"TraySettings"` | WndProc da janela de cena |
| `WM_DISPLAYCHANGE` | idem (já tratado por SDD-05 RF-05.6) |
| `WM_DPICHANGED` | idem |
| Mensagem registrada `TaskbarCreated` (`RegisterWindowMessageW`) | Explorer reiniciou: refazer RF-21.2, **reinstalar** o hook abaixo com o PID novo e consultar |
| Retomada de sono ou desbloqueio (SDD-07 RF-07.7) | a reenumeração em 1 s já prevista inclui a barra |
| **WinEvent** `EVENT_OBJECT_CREATE`, `EVENT_OBJECT_DESTROY`, `EVENT_OBJECT_LOCATIONCHANGE` do processo do Explorer | `SetWinEventHook(min, max, 0, cb, idProcess = pid do Explorer, 0, WINEVENT_OUTOFCONTEXT)`; no callback, descartar tudo que não pertença à árvore da barra (`hwnd` igual à janela da barra ou descendente dela, `idObject == OBJID_WINDOW` ou `OBJID_CLIENT`) |
| **Poll de segurança a cada 60 s**, **somente com o overlay visível** e **condicionado**: só existe se a E1 mostrar que os botões XAML **não** emitem WinEvent (DP-21.1); senão é **removido** (ADR-032) | deadline do host; desligado quando oculto (SDD-07) |

- **Overlay oculto:** nenhum gatilho consulta o UIA. Eles só marcam `layoutDirty`; a consulta acontece no próximo `Show` (como SDD-05 RF-05.6). O **hook WinEvent é desinstalado no `Hide` e reinstalado no `Show`** (consumo zero oculto, ADR-022 e SDD-07 RNF-07.1; o `Show` já faz uma consulta imediata, então nenhum evento perdido importa).
- **Notificação:** o resultado só vira `TaskbarLayoutChanged` se o layout mudou (RF-21.1). Quando muda, SDD-22 recalcula a janela de cena e o `SlotMap`, e o próximo frame é completo (SDD-08 RF-08.7).
- **Contador:** `Meter` `taskbar.queries` (consultas UIA executadas) e `taskbar.updates` (layouts que mudaram), exibidos na tela de diagnóstico (SDD-16).

### RF-21.7: Slots (`SlotMap`, puro, em Runtime) e fallback virtual

```csharp
public readonly record struct TowerSlot(int Index, float CenterXDip, float WidthDip, bool IsVirtual);
// SlotMap.From(TaskbarLayout layout, float stripWidthDip) → IReadOnlyList<TowerSlot>
```

- **Slots reais** (`Source = Uia`, `Edge == Bottom`, `!AutoHide`, ≥ 1 botão de app): um slot por `AppButton`, na mesma ordem, **no máximo 12** (os botões além do 12º são ignorados). `CenterXDip` = centro X do botão; `WidthDip` = largura do botão.
- **Fallback virtual** (qualquer uma das condições): `Edge != Bottom`, `AutoHide == true`, **3 falhas ou timeouts de 300 ms seguidos do UIA (ADR-023, RF-21.3)**, nenhum botão de app, `StartButton` ausente, ou `settings.taskbar.useUia == false`:
  - **8 slots**, passo de **48 DIP**, largura 44 DIP;
  - agrupados e **centrados** na região entre o widget (X = 420) e a bandeja virtual (`TrayLeftX = stripWidthDip − 200`). O grupo tem largura `8·48 − 4 = 380`, então `grupoLeft = (420 + (stripWidthDip − 200)) / 2 − 190`, e `CenterXDip(i) = grupoLeft + 22 + 48·i`. Com `stripWidthDip = 1440`, o grupo começa em 640 e reproduz exatamente o design;
  - o portal (SDD-22) usa `TrayLeftX = stripWidthDip − 200`, isto é, X = `largura − 184`.
- Mistura é proibida: ou todos os slots são reais, ou todos são virtuais.
- `settings.taskbar.useUia` (padrão `true`) é o **interruptor de segurança**: com `false`, o UIA nunca é chamado.

### RF-21.8: Torres presas ao índice ordinal do slot

- A torre guarda `slotIndex` (0-based), não coordenadas. A posição X em DIP vem sempre do `SlotMap` atual.
- **Slots diminuíram** (por exemplo, o usuário desafixou um app ou o layout caiu em fallback): torres com `slotIndex ≥ contagemDeSlots` ficam em estado **`SEM SLOT`**:
  - não atiram, não são desenhadas na faixa, **não entram** no bônus do Scale Set nem no DPS offline, mas **contam** no `n` do custo de construção (SDD-27 RF-27.5; ADR-032);
  - aparecem no Arsenal com o selo "SEM SLOT";
  - **não são vendidas nem perdidas**;
  - ao surgir um slot com aquele índice (o usuário fixou um app ou a barra voltou ao normal), a torre **volta** ao slot, sem custo.
- Efeito colateral aceito: fixar ou desafixar um app **no meio** da barra desloca os índices seguintes; as torres acompanham o índice, não o aplicativo. Isso é registrado como Decisão pendente (DP-21.3).
- Esta ficha define o contrato; o campo no save e o comportamento no combate ficam em SDD-14 e SDD-11 (a revisar).

### RF-21.9: Diagnóstico `--dump-taskbar` (App, sem janelas, sem jogo)

- Executa uma consulta completa (RF-21.2 e RF-21.3, ignorando `useUia`) e grava um JSON em `tests/SideWar.Runtime.Tests/Fixtures/windows/taskbar-<nome>.json` (o `<nome>` vem do argumento `--name`), em UTF-8, com:
  - `schema`: 1; `windowsBuild`; `dpi`; `monitor` (`bounds`, `workArea`); `edge`; `autoHide`; `taskbarRectPx`;
  - `nodes[]`: **todos** os `Button` e também os `Pane`/`Group` ancestrais diretos (para entender a árvore), cada um com `ClassName`, `ParentClassName`, `AutomationId`, `ControlType`, `rectPx`. **Nunca grava `Name`**: no lugar, um rótulo sintético `app-<n>`;
  - `layout`: o `TaskbarLayout` que o classificador produziu (para comparação com o esperado).
- Sai com código 0 e imprime o caminho do arquivo. Sai com código 2 se o UIA falhar (e grava o que obteve).
- É uma ferramenta de desenvolvimento: disponível em Release, mas só por linha de comando.
- **Conjunto mínimo de fixtures** (capturadas pelo usuário no Windows 11 dele): `taskbar-centro`, `taskbar-esquerda`, `taskbar-com-widgets`, `taskbar-muitos-apps` (mais de 12 botões), `taskbar-pequena` (ícones pequenos / barra baixa), e, se houver segundo monitor, `taskbar-secundaria`. Complemento: `taskbar-autohide`.

## 6. Requisitos não funcionais

Todas as metas abaixo são **META NÃO COMPROVADA** até haver medição registrada em `specs/evidencias/E1.md` (mesma regra de SDD-16).

- **RNF-21.1:** O tempo entre a chegada do resultado da consulta e o `TaskbarLayoutChanged` aplicado (classificação + `SlotMap` + notificação, **sem** contar a consulta UIA) é ≤ 30 ms na T-Game. A consulta UIA em si tem timeout de 300 ms (RF-21.3); o p95 medido é registrado, sem meta fixa na primeira medição.
- **RNF-21.2:** Em regime (overlay visível, barra estável), ≤ 2 atualizações (consultas UIA) por minuto, contando o poll de 60 s. Com o overlay oculto: 0.
- **RNF-21.3:** O custo de memória do UIA é **medido na E1**: working set e private bytes antes, durante e após 20 consultas seguidas (e após `GC.Collect` forçado), com a thread `SideWar.Shell` viva. Meta provisória: ≤ 5 MB retidos depois de liberar os objetos COM. Se não for atingida, abrir ADR com as medições (SDD-16, "Revisão de metas").
- **RNF-21.4:** Zero mudanças de foreground e zero `WM_SETFOCUS` causados pelo processo durante 100 consultas seguidas.
- **RNF-21.5:** O custo de CPU do `explorer.exe` durante uma consulta é registrado (delta sobre o repouso) e **não** pode gerar um segundo ciclo de eventos (o hook de RF-21.6 não pode se auto-alimentar: consultas UIA não geram `EVENT_OBJECT_*` na árvore da barra; verificado em T-21.6).
- **RNF-21.6:** O hook WinEvent descarta, no callback, ≥ 95% dos eventos fora da árvore da barra sem alocar (contador `taskbar.events.dropped`).

## 7. Estratégia de implementação

1. Em Runtime, sem dependência de Windows: `TaskbarLayout`, `UiaNode`, `TaskbarClassifier`, `SlotMap` e a regra de vínculo torre↔slot, com testes de tabela no Linux, **incluindo as fixtures reais** (T-21.1 a T-21.3). Com um `FakeTaskbarProbe` para os testes de gatilho e fallback.
2. Em Platform: `TaskbarLocator` (RF-21.2), `TaskbarProbe` + thread `SideWar.Shell` (RF-21.3), `TaskbarWatcher` (WinEvent e mensagens, RF-21.6).
3. Em App: o comando `--dump-taskbar` (RF-21.9). **Primeira tarefa da E1 para esta ficha**: capturar as fixtures antes de escrever o classificador, para que as regras C1–C4 e o limiar de alinhamento nasçam das árvores reais.
4. Integrar no host: deadline único de 500 ms, poll de 60 s e `layoutDirty` quando oculto.
5. Medir RNF-21.1 a RNF-21.3 e registrar em `specs/evidencias/E1.md`. **Se o UIA se mostrar inviável (custo, instabilidade ou falta de eventos), o jogo segue com o fallback virtual**, e o risco é reportado ao Tech Lead.

## 8. Dependências

SDD-03 (laço da T-Game, janela e WndProc, `WM_SETTINGCHANGE`/`WM_DISPLAYCHANGE`), SDD-05 (`MonitorInfo`, DPI, `ABM_GETSTATE`, debounce e `layoutDirty`). SDD-06 e SDD-07 definem o padrão de hooks, o `Hide`/`Show` e o que acontece em sono e desbloqueio. Consumidores: SDD-22 (faixa de batalha e slots) e SDD-23 (widget HUD).

## 9. Critérios de aceitação

- **CA-21.1:** Os testes de tabela do `TaskbarClassifier` passam com as fixtures reais `centro`, `esquerda`, `com-widgets`, `muitos-apps` e `pequena`: contagem e ordem dos botões de app, `TrayLeftX`, `LeftFreeZone`, `Alignment` e `Source` conforme a coluna "esperado" (calculada e fixada no teste a partir de conferência manual com captura de tela).
- **CA-21.2:** No Windows 11 do usuário, a matriz MTC-21 (abaixo) se comporta 100% como esperado, com registro em `specs/evidencias/E1.md`.
- **CA-21.3:** Com o overlay visível e a barra estável por 5 min, o contador `taskbar.queries` fica em ≤ 6 (1 inicial + 5 de segurança); com o overlay oculto por 5 min, em 0 (T-21.5, com `FakeClock`).
- **CA-21.4:** Fixar ou desafixar um aplicativo na barra atualiza os slots em ≤ 1 s (debounce de 500 ms + consulta) (MTC-21.9), e a torre do índice afetado se comporta conforme RF-21.8.
- **CA-21.5:** Em cada condição de fallback de RF-21.7 (borda ≠ inferior, ocultação automática, 3 timeouts/falhas seguidos do UIA simulados, 0 botões, `StartButton` ausente, `useUia=false`; com 1 ou 2 falhas o último layout `Uia` é mantido), o `SlotMap` devolve exatamente 8 slots virtuais, e com largura 1440 o primeiro centro está em X = 662 (= 640 + 22).
- **CA-21.6:** `SlotMap` com mais de 12 botões devolve 12 slots. Torres com índice ≥ contagem ficam `SEM SLOT` e voltam quando o slot reaparece (T-21.4).
- **CA-21.7:** Reiniciar o `explorer.exe` (MTC-21.8) recupera o layout em ≤ 5 s sem reiniciar o jogo e sem exceção não tratada.
- **CA-21.8:** Toda chamada UIA acontece na thread de nome `SideWar.Shell` (verificado por asserção do nome da thread dentro do `TaskbarProbe` real em T-21.7, e por `FakeTaskbarProbe` que registra a thread chamadora em T-21.5) e 100 consultas não alteram o foreground (T-21.7).
- **CA-21.9:** `--dump-taskbar` gera JSON válido, sem nenhum campo `Name`, e o classificador reproduz o `layout` gravado a partir dos `nodes` (ida e volta).

### Matriz MTC-21 (manual, Windows 11, jogo visível)

O ID de cada linha é `MTC-21.<#>` (coluna `#`).

| # | Configuração / ação | Esperado |
|---|---|---|
| 1 | Barra **centralizada** (padrão), 6 apps fixados | 6 slots, torres sobre os ícones; `Alignment=Center`; zona livre à esquerda > 0 |
| 2 | Barra **alinhada à esquerda** | `Alignment=Left`; slots seguem os ícones; zona livre ≈ 0 (SDD-23 decide o widget) |
| 3 | Barra com **Widgets** ligado | Widgets não vira slot; `LeftFreeZone` respeita o botão |
| 4 | **Ocultação automática** da barra ligada | Fallback: 8 slots virtuais; faixa com margem de 2 px (SDD-05 RF-05.7) |
| 5 | **Ícones pequenos** / barra baixa | `HeightDip` reflete a nova altura; slots corretos |
| 6 | **Muitos apps** (> 12 botões) | Exatamente 12 slots |
| 7 | **Multi-monitor**, barra no monitor 2 e jogo no 2 / jogo no 1 sem barra | Layout da barra do monitor certo / fallback quando o monitor do jogo não tem barra |
| 8 | **Reiniciar o Explorer** (Gerenciador de Tarefas) | Volta a ler a barra em ≤ 5 s (`TaskbarCreated`) |
| 9 | **Fixar** e **desafixar** um app | Slots atualizam em ≤ 1 s; torres seguem o índice (RF-21.8) |
| 10 | Mudar a **escala** (125% → 150%) | Slots e faixa reposicionam em ≤ 1 s |
| 11 | Mover a barra para **cima** (se disponível na build) | Fallback; faixa na borda inferior da área de trabalho |
| 12 | Abrir/fechar apps (botões surgem e somem) | Sem nova torre/slot errado; sem atualizar mais de 2×/min em regime |

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-21.1 | `TaskbarClassifier` com nós sintéticos: C1–C4, retângulos vazios, ausência de `StartButton`, ordem por X, empate | Linux |
| T-21.2 | `TaskbarClassifier` com as fixtures reais (CA-21.1) | Linux |
| T-21.3 | `SlotMap`: reais, fallback (todas as condições), 12 máximo, centralização em larguras 1280, 1440, 1920 e 2560 | Linux |
| T-21.4 | Vínculo torre↔slot: `SEM SLOT` e retorno, em sequências de redução e aumento | Linux |
| T-21.5 | `TaskbarWatcher` com `FakeClock` e `FakeTaskbarProbe`: debounce de 500 ms, poll de 60 s só visível, `layoutDirty` oculto, timeout de 300 ms, nenhuma consulta simultânea | Linux |
| T-21.6 | Hook WinEvent: as consultas UIA não realimentam o hook; filtro descarta eventos de outros apps | WindowsInteractive |
| T-21.7 | 100 consultas seguidas com `TestFocusTarget` em foco: foreground e TextBox inalterados | WindowsInteractive |
| T-21.8 | Medição de memória e tempo do UIA (RNF-21.1 e RNF-21.3), com saída CSV | WindowsInteractive |
| T-21.9 | `--dump-taskbar` ida e volta (CA-21.9) | Linux (parte pura) + WindowsInteractive |
| MTC-21.1..12 | Matriz acima | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Atualizações do Windows 11 mudarem `ClassName` e `AutomationId` da barra | Alta | Médio | Lista centralizada (RF-21.4); fallback automático; fixtures por build; `--dump-taskbar` para recapturar |
| Os botões XAML **não emitirem** `EVENT_OBJECT_*` (WinEvent) | Média | Médio | O poll de 60 s e os demais gatilhos cobrem; validar na E1; alternativa: handler de eventos UIA (`StructureChanged`), a decidir com medição (DP-21.1) |
| Explorer travado deixar a thread `SideWar.Shell` presa numa chamada COM | Média | Médio | Timeout de 300 ms na T-Game, sem novas consultas até a anterior voltar; fallback; a thread é `IsBackground` e não impede o encerramento |
| Custo de memória e de CPU do UIA maior que o previsto | Média | Alto | Thread sob demanda, `CacheRequest`, consulta só por evento, interruptor `useUia`; medir na E1 (RNF-21.3) e abrir ADR se reprovar |
| Cada ajuste na barra deslocar os índices e "mover" torres de aplicativo | Alta | Médio | Comportamento definido (RF-21.8); DP-21.3 |
| Barra espelhada (idiomas RTL) inverter a ordem dos slots | Baixa | Baixo | Fora do escopo; fallback se o classificador encontrar `StartButton` à direita (a validar) |
| Antivírus ou política corporativa bloquearem UIA entre processos | Baixa | Médio | Fallback; o jogo continua funcional; registrar no diagnóstico |
| Fixtures com dados pessoais (nomes de apps) irem para o repositório | Média | Médio | `Name` nunca é gravado (RF-21.9); revisão manual do JSON antes de commitar |
| Retângulos UIA em DPI misto (monitor secundário com escala diferente) saírem com offset | Média | Médio | Converter sempre com o DPI do monitor da barra; T-21.2 com a fixture `secundaria`; MTC-21.7 |

### Decisões pendentes (lacunas reais; o Tech Lead decide ou delega)

| ID local | Questão | Recomendação | Bloqueia |
|---|---|---|---|
| DP-21.1 | Os botões XAML emitem WinEvent de criação, destruição e mudança de posição? Se não, usar handler de eventos UIA (mais custo) ou aceitar só o poll de 60 s | Validar na E1 com `--dump-taskbar` + log do hook; manter os gatilhos atuais se bastarem. **ADR-032:** o poll de 60 s só existe se os botões XAML não emitirem WinEvent; senão é removido | RF-21.6 |
| DP-21.2 | ~~Histerese na queda para o fallback~~ **Resolvida por ADR-023:** 3 falhas seguidas antes do fallback, com nova tentativa em 5 s (RF-21.3 e RF-21.7) | n/a | n/a |
| DP-21.3 | Torre presa ao **índice** (ADR-020) e não ao aplicativo: fixar/desafixar no meio desloca as torres | Manter o índice (simples, determinístico); avisar no Arsenal. Alternativa futura: vincular ao `AutomationId` do app | RF-21.8, SDD-14 |
| DP-21.4 | Zona livre com Widgets no canto esquerdo: o widget HUD (420 DIP) deve cobrir o botão Widgets ou ocupar só o que sobrar? Esta ficha entrega a zona **após** o Widgets | Decidir em SDD-23 | SDD-23 |
| DP-21.5 | Com a barra alinhada à esquerda, ou com muitos apps (zona livre < 420 DIP), o widget HUD não cabe | Decidir em SDD-23 (reduzir, mover ou cobrir) | SDD-23 |
| DP-21.6 | Altura da barra ≠ 48 DIP (ícones pequenos): o widget é 48 DIP fixo no design | Entregar `HeightDip`; SDD-23 decide a escala do widget | SDD-23 |
| DP-21.7 | Limiar de 16 DIP para `Alignment` e a classificação C2 ("ancestral da área de notificação") são hipóteses | Confirmar com as fixtures e ajustar a ficha | RF-21.5 |
| DP-21.8 | Centrar o fallback entre o widget e a bandeja virtual (e não no monitor inteiro) é **interpretação** de "8 slots centrados": reproduz o design (X=640 em 1440 DIP) | Manter | RF-21.7 |

### Fichas a revisar por causa desta ficha (não editadas aqui)

- **SDD-01 RF-01.4:** acrescentar a thread `SideWar.Shell` (MTA, sob demanda, só UIA) e o fluxo `TaskbarLayoutChanged`.
- **SDD-11 RF-11.9 e RF-11.10:** posicionamento e comandos de construção por `slotIndex`.
- **SDD-14:** `slotIndex` e o estado `SEM SLOT` no save.
- **SDD-15:** o arraste de torre da paleta deixa de existir; a construção passa por clique em slot (SDD-22) e Arsenal.
- **SDD-16:** novos contadores `taskbar.queries`, `taskbar.updates` e `taskbar.events.dropped`; nova linha de memória do UIA.
