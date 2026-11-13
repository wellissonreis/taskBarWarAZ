# SDD-21: Integração com a barra de tarefas (TaskbarLayout, zonas de construção e fallback)

**Status:** Rascunho para revisão (rev. ADR-034) · **Etapa:** E1 (MVP técnico; faz parte dele como **risco a validar**) · **Depende de:** SDD-03, SDD-05 · **Bloqueia:** SDD-22, SDD-23

> **Revisada por ADR-034 (2026-11-13; substitui ADR-020).** As torres ficam **dentro da barra de tarefas**, em **posição livre em X**, nas zonas livres ao lado dos ícones. O `SlotMap` (slots ordinais = botões de apps) é substituído pelo **`BuildZoneMap`** (zonas livres e bloqueadas da barra, RF-21.7), e o vínculo torre↔índice de slot pelo vínculo torre↔**posição X** (âncora + deslocamento, RF-21.8). O que continua: `TaskbarLayout`, localização, consulta UIA na thread `SideWar.Shell`, classificação C1–C4, gatilhos por evento, `--dump-taskbar` e as 3 falhas antes do fallback (ADR-023). As questões abertas estão na seção 11 como **DP-33.x (Aceita (Tech Lead, 2026-11-13))**. Os textos marcados "Histórico (ADR-020)" ficam só como registro.

## 1. Identificador e título

SDD-21: Obtenção da geometria da barra de tarefas do Windows (`TaskbarLayout`), classificação dos botões, bandeja, **zonas livres e bloqueadas de construção** (`BuildZoneMap`), posição livre das torres e fallback.

## 2. Objetivo

Descobrir, **por evento e sem polling contínuo**, onde estão a barra de tarefas e os botões dos aplicativos, para que:

- o jogo saiba quais trechos da barra estão **bloqueados** (widget do Windows, Iniciar + pesquisa + ícones de apps, bandeja + relógio) e quais estão **livres** para torres (ADR-034);
- as torres fiquem em **posição livre em X** dentro das zonas livres, sem sobrepor ícones nem outras torres (RF-21.8);
- o portal (SDD-22) fique sobre o relógio, à direita.

*(Histórico, ADR-020: cada botão de app virava um slot; o widget ocupava a zona livre à esquerda.)*

Quando a barra não puder ser lida com segurança, o jogo continua funcionando no **fallback** (**DP-33.5, aceita**/DP-33.6: "faixa de torres" no topo da cena). Esta ficha também define o comando de diagnóstico que grava fixtures reais do Windows 11 do usuário para testes no Linux.

## 3. Contexto técnico

- **Decisões:** ADR-018 (design oficial, tokens em SDD-20), **ADR-034** (torres dentro da barra, posição livre em X, zonas bloqueadas; substitui ADR-020: faixa de 96 DIP, widget 420×48 à esquerda, slots = botões de apps), ADR-026 (layout e posições como estado da simulação), ADR-023 (UI Automation **somente leitura** para a geometria da barra, consultada por evento, com fallback). Respeita ADR-005 (CsWin32), ADR-014 (sem rede) e ADR-015 (nunca roubar foco).
- **Distinção importante:** ADR-009/SDD-15 dizem que a HUD do jogo **não** expõe UI Automation. Aqui o UIA é usado no sentido inverso: o jogo **lê** a árvore da barra do Explorer. Não há `SetFocus`, `Invoke`, `Select` nem qualquer padrão de controle. Só `BoundingRectangle`, `ClassName`, `AutomationId` e `ControlType`.
- No Windows 11, a barra de tarefas é uma ilha XAML hospedada em `Shell_TrayWnd`. Os botões **não são janelas Win32 filhas**, então a geometria vem do UIA. Os nomes de classe e de `AutomationId` abaixo vêm da especificação do Tech Lead e **precisam ser confirmados pelas fixtures reais** (RF-21.9). A lista de classificação fica centralizada (RF-21.4) por esse motivo.
- As coordenadas do UIA (`BoundingRectangle`) e de `GetWindowRect` são **físicas** (processo PMv2, SDD-05). O `TaskbarLayout` converte tudo para DIP relativo à origem do monitor (RF-21.1).
- Os requisitos de baixo consumo continuam valendo (SDD-03 RNF-03.1, SDD-06 RNF-06.2, SDD-07 RNF-07.1): nenhum timer periódico curto, nenhuma consulta com o overlay oculto.
- O design de referência (canvas atualizado, `Strip.dc.html`, composição de **1920×152 DIP**: cena `y` 0–102, barra `y` 104–152) mostra: widget do Windows em [0, 192), Iniciar em 690, pesquisa em [740, 940), 8 ícones em `x = 948 + 48·i` (botão 44), bandeja a partir de 1712 e relógio à direita. As zonas bloqueadas desenhadas no modo construção são **[0, 192)** "WIDGET", **[686, 1332)** "INICIAR · PESQUISA · ÍCONES", **[1696, 1920)** "BANDEJA · RELÓGIO" e **toda a área acima da barra** ("ROTA DOS INIMIGOS, BASE E PORTAL"); livres: **[192, 686)** e **[1332, 1696)**. Esses números são a fixture sintética de referência de T-21.3 (CA-21.5). *(Histórico: o design anterior, 1440 DIP, tinha ícones em `640 + 48·i`, widget em [0, 420) e bandeja em [1240, 1440).)*

### Substituições e conflitos com fichas anteriores (o design vence)

| Ficha antiga | O que muda | Tratamento |
|---|---|---|
| SDD-05 RF-05.7 | Detecção da borda e de `ABS_AUTOHIDE` pela própria ficha SDD-05 | **Não substituído.** SDD-21 consome o mesmo `ABM_GETSTATE` e **acrescenta** os botões. A margem de 2 px com ocultação automática de RF-05.7 continua valendo (SDD-22) |
| SDD-01 RF-01.4 (modelo de threads) | Nova thread `SideWar.Shell` (MTA) | **Substitui SDD-01 RF-01.4** apenas por acréscimo de uma linha na tabela de threads (ver seção 11, "Fichas a revisar") |
| SDD-11 RF-11.9 (`PlacementValidator`, posição livre x,y) e SDD-15 (arraste de torre da paleta) | ~~Torre passa a ser presa a um **índice de slot**~~ **ADR-034:** torre em **posição livre em X** dentro das zonas livres da barra (1D, sem `y`), validada por `PlacementValidator` 1D | SDD-11 RF-11.9/11.10 reescritos por ADR-034; SDD-14 troca `slotIndex` por `anchor`/`offsetDip`/`placed` (migração DP-33.7); SDD-15 RF-15.3 volta a ter arraste (modo construção) |

## 4. Escopo e exclusões

**Escopo:** modelo `TaskbarLayout`, localização das janelas da barra por monitor, consulta UIA em thread dedicada, `TaskbarClassifier` (puro), cálculo de zonas bloqueadas e livres, bandeja e alinhamento, gatilhos de atualização, `BuildZoneMap` (zonas da barra e fallback), regra de validade da posição X das torres, interruptores de configuração, comando `--dump-taskbar` e fixtures. *(Histórico: `SlotMap` e vínculo torre↔índice de slot, ADR-020.)*

**Exclusões:**

- geometria e desenho da janela de cena (SDD-22);
- posição e conteúdo do widget HUD (SDD-23; com ADR-034 o widget não depende mais da zona livre à esquerda);
- regras de combate e comandos (SDD-11) e persistência da posição das torres (SDD-14);
- o modo construção (desenho, arraste, ESC: SDD-22 RF-22.11, SDD-15 RF-15.3, SDD-24 RF-24.5);
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
    (float X0, float X1) LeftFreeZone,    // intervalo livre à esquerda, em DIP desde a borda esquerda do monitor (RF-21.5; informativo após ADR-034)
    IReadOnlyList<BlockedSpan> Blocked,   // ADR-034: trechos bloqueados da barra, ordenados por X (RF-21.5)
    float TrayLeftX,                      // menor X dos elementos da bandeja (RF-21.5)
    TaskbarAlignment Alignment,
    TaskbarSource Source,
    uint Dpi);                            // DPI efetivo do monitor, para converter de volta a pixels
```

- Conversão: `dip = px_físico / (Dpi / 96)`, medida a partir de `MonitorInfo.Bounds` (SDD-05). Como a barra inferior não altera `WorkArea.Left`, o X em DIP do `TaskbarLayout` coincide com o X da faixa de cena de SDD-22.
- Igualdade para decidir "mudou?": comparar `Edge`, `AutoHide`, `Source`, número de botões e cada retângulo com tolerância de **0,5 DIP**. Se nada mudou, **não** notificar (nenhum redesenho, RF-21.6).
- Em `Source = Fallback`: `AppButtons` e `Blocked` vêm vazios, e o `BuildZoneMap` (RF-21.7) usa o hospedeiro de fallback.
- **ADR-034:** `public enum BlockReason { WindowsWidget, StartSearchApps, TrayClock, Edge }` e `public readonly record struct BlockedSpan(float X0, float X1, BlockReason Reason);` (DIP, mesma origem de `DipRect`). A igualdade de "mudou?" passa a comparar também `Blocked` (tolerância de 0,5 DIP).

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
- ~~Se a largura de `LeftFreeZone` for menor que 420 DIP (widget HUD), a decisão de posicionar o widget é de SDD-23.~~ *(Obsoleto por ADR-034: o widget não depende da zona livre.)*

**Zonas bloqueadas (ADR-034, vigente):** o classificador produz `Blocked` assim, em DIP, e depois funde trechos que se tocam ou se sobrepõem (a razão do trecho fundido é a do primeiro, por ordem de X):

| Trecho | Intervalo | Razão |
|---|---|---|
| Widget do Windows (`WidgetsButton` no canto esquerdo) | `[TaskbarRect.Left, WidgetsButton.Right + mIcone)` | `WindowsWidget` |
| Cada elemento C1 (exceto Widgets no canto) e C3 | `[Rect.Left − mIcone, Rect.Right + mIcone)` | `StartSearchApps` |
| Bandeja e relógio | `[TrayLeftX − mBandeja, TaskbarRect.Right)` | `TrayClock` |
| Bordas | `[TaskbarRect.Left, TaskbarRect.Left + mIcone)` quando não há Widgets no canto | `Edge` |

- **`mIcone = 4 DIP`** e **`mBandeja = 16 DIP`**: transcritos do canvas (Iniciar em 690 → bloqueio a partir de 686; último ícone termina em 1328 → bloqueio até 1332; primeiro ícone da bandeja em 1712 → bloqueio a partir de 1696). Ficam em `TaskbarClasses.cs` como constantes, a confirmar pelas fixtures (DP-33.3).
- Com a barra **centralizada** (padrão do Windows 11), o grupo Iniciar + pesquisa + ícones cresce **para os dois lados** quando apps abrem; com a barra **à esquerda**, a zona livre da esquerda some e só resta a da direita. Os dois casos são normais (não são fallback); o efeito sobre torres já posicionadas é DP-33.1.
- A **área acima da barra** é sempre bloqueada para torres (rota, base e portal, ADR-034); ela não entra em `Blocked` porque não é parte da barra, e o `BuildZoneMap` simplesmente não a oferece como hospedeiro.

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
- **Notificação:** o resultado só vira `TaskbarLayoutChanged` se o layout mudou (RF-21.1). Quando muda, SDD-22 recalcula a janela de cena e o `BuildZoneMap` (antes: `SlotMap`), a T-Game enfileira `LayoutChanged` (ADR-026) e o próximo frame é completo (SDD-08 RF-08.7).
- **Contador:** `Meter` `taskbar.queries` (consultas UIA executadas) e `taskbar.updates` (layouts que mudaram), exibidos na tela de diagnóstico (SDD-16).

### RF-21.7: Zonas de construção (`BuildZoneMap`, puro, em Runtime) e fallback

> **Substitui o `SlotMap` (ADR-034).** O texto antigo está no fim desta seção como histórico.

```csharp
public enum TowerHost { Taskbar, Band }                      // Band = "faixa de torres" de fallback (DP-33.5/DP-33.6)
public readonly record struct FreeZone(int X0, int X1);      // DIP inteiros, [X0, X1), relativo à borda esquerda da faixa (SDD-22 RF-22.2)
public readonly record struct BuildZones(TowerHost Host, IReadOnlyList<FreeZone> Free, int HostTopDip, int HostHeightDip);
// BuildZoneMap.From(TaskbarLayout layout, int stripWidthDip, BuildZoneOptions opt) → BuildZones
```

- **Hospedeiro `Taskbar`** (todas as condições): `Source = Uia`, `Edge == Bottom`, `!AutoHide`, `StartButton` presente, `settings.taskbar.useUia == true` e `settings.taskbar.towersInBar == true` (novo interruptor, padrão `true`; existe para desligar as torres na barra se o risco de z-order de DP-33.4 se confirmar). Então:
  - `Free` = complemento de `Blocked` (RF-21.5) dentro de `[TaskbarRect.Left, TaskbarRect.Right)`, convertido para a origem da faixa, com `X0 = ceil`, `X1 = floor` (DIP inteiros, para o determinismo de RF-21.8);
  - descarta zonas com largura `< larguraColisão` (44 DIP, DP-33.3): nelas nenhuma torre cabe;
  - no máximo **8** zonas (as excedentes, da direita, são descartadas e registradas em Debug);
  - `HostTopDip` = topo da barra na janela de cena (104, SDD-22 RF-22.2) e `HostHeightDip` = `HeightDip` da barra.
- **Hospedeiro `Band`** (qualquer outra condição: `Source = Fallback` após 3 falhas do UIA, `Edge != Bottom`, `AutoHide`, `StartButton` ausente, `useUia == false` ou `towersInBar == false`): **DP-33.5/DP-33.6, aceitas**. Faixa de torres no **topo da cena** (`HostTopDip = 2`, `HostHeightDip = 44`, como a "band row" de 44 DIP em `top 2` registrada no script do canvas), com zonas livres `[96, W/2 − 240)` e `[W/2 + 240, W − 560)` (fora do núcleo, do slot central de banners/selo e do widget HUD à direita, SDD-23 RF-23.2), aplicando o mesmo descarte de zonas estreitas.
- Mistura é proibida: num dado layout, todas as zonas são de um único hospedeiro.
- O `BuildZoneMap` é **entrada** do comando `LayoutChanged` (SDD-11 RF-11.3): a simulação recebe `Host`, `Free` e `W` já quantizados e nunca lê a barra.
- `settings.taskbar.useUia` (padrão `true`) continua sendo o **interruptor de segurança**: com `false`, o UIA nunca é chamado.

### RF-21.8: Posição livre das torres (contrato; a regra vive em SDD-11 RF-11.9)

> **Substitui o vínculo torre↔índice de slot (ADR-034).**

- A torre guarda **`anchor ∈ { Start, End }`** e **`offsetDip`** (inteiro ≥ 0), não um índice. O centro X na faixa é `x = offsetDip` (Start) ou `x = W − offsetDip` (End). A âncora é escolhida **no momento da construção**: `Start` se `x < W/2`, senão `End` (**DP-33.2, aceita**).
- **Caixa de colisão:** `[x − 22, x + 22)` DIP (**44 DIP**, a célula de realce do canvas; **DP-33.3, aceita**). Uma posição é **válida** se a caixa cabe inteira numa `FreeZone` do layout atual **e** não intersecta a caixa de nenhuma outra torre **posicionada** (bordas que se tocam não contam como sobreposição).
- **Estado `SEM POSIÇÃO`** (substitui `SEM SLOT`): após um `LayoutChanged`, as torres são reavaliadas **em ordem crescente de `Id`**; a torre cuja caixa deixou de ser válida (fora de zona livre ou sobrepondo uma torre de `Id` menor já reavaliada) fica `SEM POSIÇÃO`. Regras herdadas de ADR-032 item 3: **não atira**, **não é desenhada**, **não entra** no bônus do Scale Set nem no DPS offline, **conta** no `n` do custo de construção, **não** é vendida nem perdida, aparece no Arsenal com o selo `SEM POSIÇÃO`.
- **Retorno:** a cada `LayoutChanged`, uma torre `SEM POSIÇÃO` cuja posição gravada voltou a ser válida **volta sozinha**, sem custo (mesma ordem por `Id`). Ela também pode ser reposicionada pelo jogador (`PlaceTower(id, x)`, **DP-33.1, aceita**). O que fazer quando a zona encolhe (apps abertos, barra centralizada crescendo para os dois lados) é **DP-33.1**.
- **Limite:** no máximo **24 torres** (incluindo as `SEM POSIÇÃO`; **DP-33.3, aceita**), limitado ainda pelo espaço livre.
- Esta ficha define o contrato; a validação e os comandos ficam em SDD-11 (RF-11.9, RF-11.10), o save em SDD-14 e a UI em SDD-22/SDD-24.

#### Histórico (ADR-020): RF-21.7 Slots (`SlotMap`) e fallback virtual

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

#### Histórico (ADR-020): RF-21.8 Torres presas ao índice ordinal do slot

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
  - `layout`: o `TaskbarLayout` que o classificador produziu (para comparação com o esperado), **incluindo `Blocked`** e o `BuildZones` resultante (ADR-034).
- Sai com código 0 e imprime o caminho do arquivo. Sai com código 2 se o UIA falhar (e grava o que obteve).
- É uma ferramenta de desenvolvimento: disponível em Release, mas só por linha de comando.
- **Conjunto mínimo de fixtures** (capturadas pelo usuário no Windows 11 dele): `taskbar-centro`, `taskbar-esquerda`, `taskbar-com-widgets`, `taskbar-muitos-apps` (mais de 12 botões), `taskbar-pequena` (ícones pequenos / barra baixa), e, se houver segundo monitor, `taskbar-secundaria`. Complemento: `taskbar-autohide`. **ADR-034:** acrescentar `taskbar-centro-apps-abertos` (o mesmo `centro` com 4 apps a mais abertos, para medir o crescimento do grupo central para os dois lados, DP-33.1).

## 6. Requisitos não funcionais

Todas as metas abaixo são **META NÃO COMPROVADA** até haver medição registrada em `specs/evidencias/E1.md` (mesma regra de SDD-16).

- **RNF-21.1:** O tempo entre a chegada do resultado da consulta e o `TaskbarLayoutChanged` aplicado (classificação + `BuildZoneMap` + notificação, **sem** contar a consulta UIA) é ≤ 30 ms na T-Game. A consulta UIA em si tem timeout de 300 ms (RF-21.3); o p95 medido é registrado, sem meta fixa na primeira medição.
- **RNF-21.2:** Em regime (overlay visível, barra estável), ≤ 2 atualizações (consultas UIA) por minuto, contando o poll de 60 s. Com o overlay oculto: 0.
- **RNF-21.3:** O custo de memória do UIA é **medido na E1**: working set e private bytes antes, durante e após 20 consultas seguidas (e após `GC.Collect` forçado), com a thread `SideWar.Shell` viva. Meta provisória: ≤ 5 MB retidos depois de liberar os objetos COM. Se não for atingida, abrir ADR com as medições (SDD-16, "Revisão de metas").
- **RNF-21.4:** Zero mudanças de foreground e zero `WM_SETFOCUS` causados pelo processo durante 100 consultas seguidas.
- **RNF-21.5:** O custo de CPU do `explorer.exe` durante uma consulta é registrado (delta sobre o repouso) e **não** pode gerar um segundo ciclo de eventos (o hook de RF-21.6 não pode se auto-alimentar: consultas UIA não geram `EVENT_OBJECT_*` na árvore da barra; verificado em T-21.6).
- **RNF-21.6:** O hook WinEvent descarta, no callback, ≥ 95% dos eventos fora da árvore da barra sem alocar (contador `taskbar.events.dropped`).

## 7. Estratégia de implementação

1. Em Runtime, sem dependência de Windows: `TaskbarLayout`, `UiaNode`, `TaskbarClassifier` (com `Blocked`), `BuildZoneMap` e a regra de validade da posição X (ADR-034; antes `SlotMap` e vínculo torre↔slot), com testes de tabela no Linux, **incluindo as fixtures reais** (T-21.1 a T-21.3). Com um `FakeTaskbarProbe` para os testes de gatilho e fallback.
2. Em Platform: `TaskbarLocator` (RF-21.2), `TaskbarProbe` + thread `SideWar.Shell` (RF-21.3), `TaskbarWatcher` (WinEvent e mensagens, RF-21.6).
3. Em App: o comando `--dump-taskbar` (RF-21.9). **Primeira tarefa da E1 para esta ficha**: capturar as fixtures antes de escrever o classificador, para que as regras C1–C4 e o limiar de alinhamento nasçam das árvores reais.
4. Integrar no host: deadline único de 500 ms, poll de 60 s e `layoutDirty` quando oculto.
5. Medir RNF-21.1 a RNF-21.3 e registrar em `specs/evidencias/E1.md`. **Se o UIA se mostrar inviável (custo, instabilidade ou falta de eventos), o jogo segue com o fallback virtual**, e o risco é reportado ao Tech Lead.

## 8. Dependências

SDD-03 (laço da T-Game, janela e WndProc, `WM_SETTINGCHANGE`/`WM_DISPLAYCHANGE`), SDD-05 (`MonitorInfo`, DPI, `ABM_GETSTATE`, debounce e `layoutDirty`). SDD-06 e SDD-07 definem o padrão de hooks, o `Hide`/`Show` e o que acontece em sono e desbloqueio. Consumidores: SDD-22 (cena, torres na barra, modo construção), SDD-11 (`LayoutChanged`, validação de posição) e SDD-24 (Arsenal).

## 9. Critérios de aceitação

- **CA-21.1:** Os testes de tabela do `TaskbarClassifier` passam com as fixtures reais `centro`, `esquerda`, `com-widgets`, `muitos-apps` e `pequena`: contagem e ordem dos botões de app, `TrayLeftX`, `LeftFreeZone`, **`Blocked`**, `Alignment` e `Source` conforme a coluna "esperado" (calculada e fixada no teste a partir de conferência manual com captura de tela).
- **CA-21.2:** No Windows 11 do usuário, a matriz MTC-21 (abaixo) se comporta 100% como esperado, com registro em `specs/evidencias/E1.md`.
- **CA-21.3:** Com o overlay visível e a barra estável por 5 min, o contador `taskbar.queries` fica em ≤ 6 (1 inicial + 5 de segurança); com o overlay oculto por 5 min, em 0 (T-21.5, com `FakeClock`).
- **CA-21.4:** Fixar ou desafixar (ou abrir e fechar) um aplicativo na barra atualiza as zonas em ≤ 1 s (debounce de 500 ms + consulta) (MTC-21.9), e as torres cuja caixa deixou de caber ficam `SEM POSIÇÃO` e voltam quando o espaço reaparece (RF-21.8; ADR-034).
- **CA-21.5 (ADR-034):** Com a fixture sintética do canvas (largura 1920; `WidgetsButton` [0, 188); `StartButton` [690, 734); pesquisa [740, 940); 8 apps em `[948 + 48·i, 992 + 48·i)`; bandeja a partir de 1712), `BuildZoneMap` devolve `Host = Taskbar` e exatamente `Free = [192, 686)` e `[1332, 1696)` (os mesmos realces do canvas). Com as fixtures reais, os valores esperados são calculados e fixados no teste. Em cada condição de fallback de RF-21.7 (borda ≠ inferior, ocultação automática, 3 timeouts/falhas seguidos do UIA simulados, `StartButton` ausente, `useUia=false`, `towersInBar=false`; com 1 ou 2 falhas o último layout `Uia` é mantido), devolve `Host = Band` com as zonas propostas em DP-33.6. *(Histórico: 8 slots virtuais, primeiro centro em 662 com largura 1440.)*
- **CA-21.6 (ADR-034):** Validade da posição: caixa de 44 DIP inteira numa zona livre e sem sobreposição; reavaliação em ordem de `Id` após encolher e alargar zonas; `SEM POSIÇÃO` e retorno automático; limite de 24 torres (T-21.4). *(Histórico: 12 slots no máximo; `SEM SLOT`.)*
- **CA-21.7:** Reiniciar o `explorer.exe` (MTC-21.8) recupera o layout em ≤ 5 s sem reiniciar o jogo e sem exceção não tratada.
- **CA-21.8:** Toda chamada UIA acontece na thread de nome `SideWar.Shell` (verificado por asserção do nome da thread dentro do `TaskbarProbe` real em T-21.7, e por `FakeTaskbarProbe` que registra a thread chamadora em T-21.5) e 100 consultas não alteram o foreground (T-21.7).
- **CA-21.9:** `--dump-taskbar` gera JSON válido, sem nenhum campo `Name`, e o classificador reproduz o `layout` gravado a partir dos `nodes` (ida e volta).

### Matriz MTC-21 (manual, Windows 11, jogo visível)

O ID de cada linha é `MTC-21.<#>` (coluna `#`).

| # | Configuração / ação | Esperado |
|---|---|---|
| 1 | Barra **centralizada** (padrão), 6 apps fixados | 2 zonas livres (esquerda e direita do grupo central); realces do modo construção batem com a tela; `Alignment=Center` |
| 2 | Barra **alinhada à esquerda** | `Alignment=Left`; só a zona livre da direita (entre o último ícone e a bandeja) |
| 3 | Barra com **Widgets** ligado | O widget do Windows fica bloqueado (`WindowsWidget`) |
| 4 | **Ocultação automática** da barra ligada | Fallback `Band` (DP-33.5); cena com margem de 2 px (SDD-05 RF-05.7); nenhuma torre desenhada sobre a barra |
| 5 | **Ícones pequenos** / barra baixa | `HeightDip` reflete a nova altura; torres centralizadas na barra; zonas corretas |
| 6 | **Muitos apps** (> 12 botões) | Zona central cresce; torres atingidas ficam `SEM POSIÇÃO` e voltam ao fechar os apps (DP-33.1) |
| 7 | **Multi-monitor**, barra no monitor 2 e jogo no 2 / jogo no 1 sem barra | Layout da barra do monitor certo / fallback quando o monitor do jogo não tem barra |
| 8 | **Reiniciar o Explorer** (Gerenciador de Tarefas) | Volta a ler a barra em ≤ 5 s (`TaskbarCreated`) |
| 9 | **Fixar** e **desafixar** um app | Zonas atualizam em ≤ 1 s; nenhuma torre fica sobre ícone; `SEM POSIÇÃO` e retorno (RF-21.8) |
| 10 | Mudar a **escala** (125% → 150%) | Slots e faixa reposicionam em ≤ 1 s |
| 11 | Mover a barra para **cima** (se disponível na build) | Fallback; faixa na borda inferior da área de trabalho |
| 12 | Abrir/fechar apps (botões surgem e somem) | Nenhuma torre sobre ícone; sem atualizar mais de 2×/min em regime |
| 13 | **ADR-034:** clicar num ícone da barra, no Iniciar e na bandeja com torres visíveis | Torres voltam acima da barra em ≤ 150 ms (DP-33.4); o clique chegou ao Windows |

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-21.1 | `TaskbarClassifier` com nós sintéticos: C1–C4, retângulos vazios, ausência de `StartButton`, ordem por X, empate | Linux |
| T-21.2 | `TaskbarClassifier` com as fixtures reais (CA-21.1) | Linux |
| T-21.3 | `BuildZoneMap` (ADR-034): bloqueios com margens 4/16, fusão, descarte de zonas < 44, máximo de 8 zonas, hospedeiro `Taskbar` × `Band` em todas as condições, larguras 1280, 1440, 1920 e 2560, fixture sintética do canvas | Linux |
| T-21.4 | Posição livre (ADR-034): validade da caixa, sobreposição, âncora `Start`/`End`, `SEM POSIÇÃO` e retorno em sequências de encolher e alargar, ordem por `Id`, limite de 24 | Linux |
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
| ~~Cada ajuste na barra deslocar os índices~~ (obsoleto por ADR-034) Abrir apps encolher as zonas livres e deixar torres `SEM POSIÇÃO` com frequência | Alta | Médio | Âncora pela borda externa (DP-33.2), retorno automático (DP-33.1), aviso no Arsenal |
| **ADR-034:** `Shell_TrayWnd` (topmost) subir ao ser clicada e cobrir as torres desenhadas sobre a barra | Alta | Alto | Reafirmação de z-order pelo `ZOrderCoordinator` (SDD-22 RF-22.1), spike obrigatório na E1 (ficha TA.1 do PLANO), interruptor `towersInBar` e fallback `Band` (DP-33.4) |
| Barra espelhada (idiomas RTL) inverter a ordem dos slots | Baixa | Baixo | Fora do escopo; fallback se o classificador encontrar `StartButton` à direita (a validar) |
| Antivírus ou política corporativa bloquearem UIA entre processos | Baixa | Médio | Fallback; o jogo continua funcional; registrar no diagnóstico |
| Fixtures com dados pessoais (nomes de apps) irem para o repositório | Média | Médio | `Name` nunca é gravado (RF-21.9); revisão manual do JSON antes de commitar |
| Retângulos UIA em DPI misto (monitor secundário com escala diferente) saírem com offset | Média | Médio | Converter sempre com o DPI do monitor da barra; T-21.2 com a fixture `secundaria`; MTC-21.7 |

### Decisões pendentes (lacunas reais; o Tech Lead decide ou delega)

| ID local | Questão | Recomendação | Bloqueia |
|---|---|---|---|
| DP-21.1 | Os botões XAML emitem WinEvent de criação, destruição e mudança de posição? Se não, usar handler de eventos UIA (mais custo) ou aceitar só o poll de 60 s | Validar na E1 com `--dump-taskbar` + log do hook; manter os gatilhos atuais se bastarem. **ADR-032:** o poll de 60 s só existe se os botões XAML não emitirem WinEvent; senão é removido | RF-21.6 |
| DP-21.2 | ~~Histerese na queda para o fallback~~ **Resolvida por ADR-023:** 3 falhas seguidas antes do fallback, com nova tentativa em 5 s (RF-21.3 e RF-21.7) | n/a | n/a |
| DP-21.3 | ~~Torre presa ao **índice** (ADR-020)~~ **Obsoleta por ADR-034:** a torre tem posição X própria (RF-21.8) | n/a | n/a |
| DP-21.4 | ~~Widget HUD × botão Widgets na zona esquerda~~ **Obsoleta por ADR-034:** o widget HUD fica à direita, acima da barra; o widget do Windows é zona bloqueada | n/a | n/a |
| DP-21.5 | ~~Widget HUD sem espaço na zona livre~~ **Obsoleta por ADR-034** | n/a | n/a |
| DP-21.6 | Altura da barra ≠ 48 DIP (ícones pequenos) | Entregar `HeightDip`; **ADR-034:** a torre (36 DIP) é centralizada na barra (`top = barTop + round((H − 36)/2)`, SDD-22 RF-22.5); o widget HUD não depende mais da barra | SDD-22 |
| DP-21.7 | Limiar de 16 DIP para `Alignment` e a classificação C2 ("ancestral da área de notificação") são hipóteses | Confirmar com as fixtures e ajustar a ficha | RF-21.5 |
| DP-21.8 | ~~Centrar os 8 slots virtuais~~ **Obsoleta por ADR-034:** o fallback passa a ser a faixa de torres (DP-33.6) | n/a | n/a |

### Decisões de ADR-034 (DP-33.x: Aceita (Tech Lead, 2026-11-13))

**Status: Aceita (Tech Lead, 2026-11-13)** para DP-33.1 a DP-33.9, como propostas no texto. Cada linha traz a proposta do redator da ficha SPEC-ADR033 e a justificativa. Restrição comum: **determinismo (ADR-026)**; tudo o que afeta a simulação entra por comando entre ticks, no save e no `StateHash`.

| ID | Questão | Decisão (Aceita (Tech Lead, 2026-11-13)) | Justificativa | Bloqueia |
|---|---|---|---|---|
| DP-33.1 | **Zona livre que encolhe** (apps abertos/fechados; a barra centralizada do Windows 11 cresce para os **dois** lados; bandeja ganha ícones) | (a) **Nada se move sozinho:** a torre cuja caixa deixa de caber fica `SEM POSIÇÃO` (RF-21.8) e **volta sozinha** quando o espaço reaparece, reavaliada em ordem de `Id` a cada `LayoutChanged`; (b) novo comando **`PlaceTower(id, x)`**, sem custo, permitido **só** para torres `SEM POSIÇÃO` (botão `REPOSICIONAR` no Arsenal, SDD-24 RF-24.5); (c) sem "empurrar para a vaga mais próxima" automático | Abrir e fechar apps é transitório: mover torres sozinho perderia a escolha do jogador a cada app aberto e geraria cascatas de colisão; o retorno automático preserva a escolha. A regra é pura e determinística (função do layout + posições, ordem por `Id`). Restringir `PlaceTower` a `SEM POSIÇÃO` evita reabrir "mover torre" (DP-27.3) | SDD-11 RF-11.9/11.10, SDD-24 RF-24.5 |
| DP-33.2 | **Referência de ancoragem do X** (borda da zona × absoluto) | Âncora pela **borda externa da barra**: `Start` (distância à borda esquerda da faixa) se `x < W/2` na construção, `End` (distância à borda direita) caso contrário; `offsetDip` inteiro. Não ancorar na borda da zona | A borda **interna** das zonas (lado dos ícones) é a que se move quando apps abrem; ancorar nela empurraria torres para dentro dos ícones. As bordas **externas** (widget do Windows à esquerda, bandeja/relógio à direita) são estáveis; ancorar nelas mantém as torres "perto do relógio" mesmo quando a resolução ou a largura da barra mudam. Absoluto puro falharia ao trocar de resolução (torres da direita sairiam da barra) | SDD-11, SDD-14 |
| DP-33.3 | **Sobreposição mínima / largura de colisão** e limite | Caixa de colisão de **44 DIP** centrada no X (célula de realce do canvas), sem sobreposição (encostar é permitido: passo mínimo de 44 entre centros); X em **DIP inteiros**; margens de bloqueio `mIcone = 4` e `mBandeja = 16`; zonas mais estreitas que 44 são descartadas; **máximo de 24 torres** | 44 é o realce verde do canvas e o alvo mínimo de clique (SDD-20 RF-20.4); usar a largura do sprite (32–37) deixaria torres coladas e difíceis de clicar. DIP inteiros evitam `float` no save/hash. 24 cobre as duas zonas de uma barra de 1920 (≈ 11 + 8 caixas) e mantém os cenários de desempenho (antes 12) num teto conhecido | SDD-11, SDD-16, SDD-22 |
| DP-33.4 | **Z-order e clique da janela sobre a barra** (risco: `Shell_TrayWnd` é topmost e sobe ao ser clicada) | (a) A janela de cena entra no `ZOrderCoordinator` com a mesma reafirmação do widget: evento de foreground em `Shell_TrayWnd`/`Shell_SecondaryTrayWnd` e `EVENT_OBJECT_REORDER`/`LOCATIONCHANGE` da barra (hook já existente de RF-21.6), debounce de 100 ms, `SWP_NOACTIVATE`; (b) em Passivo a janela inteira é `WS_EX_TRANSPARENT` (todo clique vai à barra); em Interativo só pixels de torre recebem clique e o resto (alfa 0) atravessa para os ícones; (c) **spike obrigatório na E1** (ficha TA.1) antes de qualquer outra ficha de render: se a torre ficar coberta por mais de 150 ms ou cintilar de forma visível em MTC-21.13, desligar `towersInBar` por padrão e usar o hospedeiro `Band` | Sem reafirmação as torres somem atrás da barra a cada clique; o padrão já existe e foi medido para o widget (SDD-23 RF-23.10). O Menu Iniciar e a pesquisa ficam numa banda de z acima de qualquer topmost: aceitar que cubram as torres enquanto abertos. O interruptor evita bloquear o jogo se o Windows não permitir | SDD-22 RF-22.1, SDD-03 RF-03.9 |
| DP-33.5 | **Ocultação automática, barra vertical/no topo e vários monitores** | Ocultação automática, `Edge ≠ Bottom` → hospedeiro `Band` (faixa de torres no topo da cena), janela só com a cena (sem cobrir a barra) e margem de 2 px com ocultação; vários monitores: a barra do monitor escolhido em "Monitor ▸" (ADR-031 item 8), barras secundárias lidas pelo mesmo classificador; trocar de monitor = `LayoutChanged`, as torres mantêm âncora e deslocamento | Desenhar sobre uma barra que aparece e some (ou vertical) não tem lugar estável; a faixa de torres mantém o jogo funcional sem tocar a barra. Âncora + deslocamento torna a troca de monitor trivial e determinística | RF-21.7, SDD-22 RF-22.1 |
| DP-33.6 | **Fallback sem UIA** (falha, timeout, `useUia = false`, árvore desconhecida) | Hospedeiro `Band`: zonas `[96, W/2 − 240)` e `[W/2 + 240, W − 560)` no topo da cena (`top 2`, sprite de 44 DIP); **nunca** estimar a posição dos ícones | Sem UIA não se sabe onde estão os ícones; qualquer estimativa arriscaria desenhar torres sobre ícones, o que o usuário bloqueou explicitamente. As zonas evitam o núcleo, o slot central de banners/selo e o widget HUD | RF-21.7, SDD-22 |
| DP-33.7 | **Migração de saves com `slotIndex`** | Schema de sessão +1 (SDD-14 RF-14.10): toda torre migrada nasce `SEM POSIÇÃO` (`anchor = Start`, `offsetDip = 0`, `placed = false`) e o primeiro `LayoutChanged` após o restore é seguido do comando **`AutoPlaceUnpositioned()`** (só uma vez, marcado no save por `migratedSlots = true`): percorre as torres `SEM POSIÇÃO` em ordem de `Id` e coloca cada uma na primeira posição válida, da esquerda para a direita, no passo de 44 DIP, começando pela zona livre mais à esquerda; as que não couberem continuam `SEM POSIÇÃO` | O X antigo do slot é exatamente um ícone de app, agora bloqueado, então reaproveitá-lo deixaria tudo `SEM POSIÇÃO`. Fazer a colocação como **comando** entre ticks mantém o determinismo (o resultado depende só do layout gravado e dos `Id`), e nada se perde (créditos, níveis e `n` do custo intactos) | SDD-14, SDD-11 |
| DP-33.8 | **ESC sem foco** (o jogo nunca recebe teclado) | Enquanto o modo construção está ativo, `RegisterHotKey(VK_ESCAPE, MOD_NOREPEAT)` na janela de cena, desregistrado ao sair do modo; também cancelam: clique direito, `WM_CAPTURECHANGED`, `HiddenReason` e 30 s sem movimento do mouse | ESC global só durante um modo explícito e curto, iniciado pelo próprio usuário, é o menor desvio de ADR-015; hook de teclado de baixo nível seria mais caro e mais invasivo. Se o registro falhar (outro app usa ESC global), o modo continua cancelável por clique direito | SDD-15 RF-15.3/15.6 |
| DP-33.9 | **Diferenças visuais do canvas além das decisões 1–4** (núcleo em (4, 46) com barra de vida roxa 64×5; barras de vida de 3 DIP em todos os inimigos; drone 20 DIP acima do chão; barra do chefe com a largura do sprite; rótulo de nível só com o número no canto da torre; brilho azul nas torres; sem a linha de borda de estado; feixes e clarões de tiro) | Adotar como transcrito em SDD-22 RF-22.5, **exceto**: feixes e clarões (não implementar; ADR-027 continua), brilho (pré-cozido no cache do sprite, sem filtro por frame, SDD-08 RF-08.8) e linha de borda de estado (mantida, agora no vão de 2 DIP entre o chão e a barra, `y ∈ [102, 104)`, que existe no canvas) | O canvas é a fonte visual atualizada; ADR-027 e SDD-08 são decisões vigentes que a ficha não pode reabrir; a linha de estado já foi implementada (T4.9) e cabe no vão do canvas sem cobrir a barra | SDD-22 RF-22.5/22.6, SDD-20 |

### Fichas a revisar por causa desta ficha (não editadas aqui)

- **SDD-01 RF-01.4:** acrescentar a thread `SideWar.Shell` (MTA, sob demanda, só UIA) e o fluxo `TaskbarLayoutChanged`.
- **SDD-11 RF-11.9 e RF-11.10:** ~~posicionamento e comandos de construção por `slotIndex`~~ **ADR-034:** posição livre em X (`BuildTower(type, x)`, `PlaceTower(id, x)`), feito nesta revisão.
- **SDD-14:** ~~`slotIndex` e o estado `SEM SLOT` no save~~ **ADR-034:** `anchor`, `offsetDip`, `placed` e migração (DP-33.7).
- **SDD-15:** ~~o arraste de torre da paleta deixa de existir~~ **ADR-034:** o arraste volta como modo construção a partir do Arsenal (RF-15.3).
- **SDD-16:** novos contadores `taskbar.queries`, `taskbar.updates` e `taskbar.events.dropped`; nova linha de memória do UIA.
