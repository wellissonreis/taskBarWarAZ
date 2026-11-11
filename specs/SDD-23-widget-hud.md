# SDD-23: Widget HUD sobre a barra de tarefas

**Status:** Rascunho para revisão · **Etapa:** E5 · **Depende de:** SDD-20, SDD-21, SDD-04, SDD-03 (RF-03.9: janela *owned* pela faixa), SDD-15 (menu nativo) · **Bloqueia:** SDD-24 · E1: apenas RF-23.1, RF-23.2 e RF-23.10 (janela vazia acima da barra e z-order)

## 1. Identificador e título

SDD-23: Widget HUD de 420×48 DIP (janela layered `WS_EX_NOACTIVATE`, sempre clicável) posicionado sobre a zona livre à esquerda da barra de tarefas: créditos, onda, selo de presença e menu de 4 abas.

## 2. Objetivo

Dar ao jogador, sempre visível e sempre clicável, as quatro informações mínimas do idle (créditos e taxa, onda, presença) e o acesso às quatro abas do painel (SDD-24), **sem roubar foco**, sem consumir CPU quando nada muda e sem alterar a barra de tarefas do Windows.

## 3. Contexto técnico

- ADR-018/SDD-20: design e tokens oficiais. Toda cor, fonte, ícone e animação citada aqui é token de SDD-20.
- ADR-020: geometria. Faixa de cena de 96 DIP colada acima da barra; **widget HUD 420×48 sobre a zona livre à esquerda da barra**.
- ADR-021: o widget é uma janela layered Skia `WS_EX_NOACTIVATE`; nunca ativa; nunca WPF.
- ADR-023: a geometria da barra (zona livre, altura, borda) vem de UI Automation somente leitura, consultada por evento, com fallback. **O contrato de geometria é de SDD-21; esta ficha só o consome.**
- ADR-019: idle. O widget mostra o estado de presença (presente ×2,0 ou ausente ×0,25; vírgula decimal, ADR-029).
- SDD-03 RF-03.5 (não ativação: `MA_NOACTIVATE`, nunca `SetForegroundWindow`/`SetFocus`/`SetActiveWindow`) vale integralmente para o widget.
- SDD-04 RF-04.4 (alfa ≥ 1 recebe clique; alfa 0 atravessa), RF-04.6 (regiões clicáveis só pelo alfa desenhado) valem.
- SDD-08 RF-08.1 (`LayeredSurface`), RF-08.8 (zero alocação, sem blur) valem.
- SDD-10 `RenderPolicy`: render só em mudança.
- Design de referência: `Strip.dc.html`, bloco "widget de 420 px" da barra (coluna créditos 78, coluna onda 56, selo, `nav` de 4 botões).

## 4. Escopo e exclusões

**Escopo:** janela do widget, geometria e posição, layout interno, conteúdo das colunas, selo, nav (botões, hover, ativo, badge), hit-test, cursores, contagem de interação, render por versão do modelo, reafirmação de z-order, DPI e visibilidade.

**Exclusões:**

- geometria e detecção da barra de tarefas (SDD-21);
- a faixa de cena de 96 DIP (SDD-22);
- o painel e os cards (SDD-24);
- regras de presença, multiplicadores e taxa (SDD-25/26);
- formatação de números e textos (SDD-27);
- o conteúdo e a mecânica dos menus nativos (SDD-15 RF-15.7/15.8): esta ficha só define o **gatilho** (clique direito no widget, RF-23.8).

**Substitui (o design vence; as fichas antigas não são editadas):**

| Ficha antiga | Item | Situação |
|---|---|---|
| ADR-011 | HandleWindow (alça de 28×28 DIP, owned pela OverlayWindow) | **Substituída pelo widget** (420×H DIP, **owned pela janela da faixa**, SDD-03 RF-03.9; mantém o vínculo *owned* da alça) |
| SDD-03 | RF-03.3 (HandleWindow) | Substituído por RF-23.1 |
| SDD-03 | RF-03.6 (reafirmação de topmost, debounce 1 s) | Para o widget, debounce **100 ms** e gatilho adicional de foreground em `Shell_TrayWnd` (RF-23.10); o debounce de 1 s continua para as demais janelas, se SDD-22 não dispuser outro |
| SDD-04 | RF-04.1 (linha "HandleWindow clicável", indicador "olho/mão"), RF-04.2 (transições por "clique na HandleWindow") | A revisar: não há mais alça. O widget é sempre clicável; modos Passive/Interactive da faixa são de SDD-22 |
| SDD-05 | RF-05.4 (`HudWidth`, `HandleSize`, `HudRect`, `HandleRect`), RF-05.9 (menu da alça) | A revisar: geometria do widget é de SDD-21/RF-23.2 |
| SDD-08 | RF-08.1 último item ("mesma classe serve à HandleWindow, 28×28 DIP"), RF-08.2 (camada HUD = "painel esquerdo") | Substituídos: o widget tem a própria `LayeredSurface` |
| SDD-15 | **RF-15.1** (layout da HUD de 216×176 DIP com alça, núcleo, onda etc.), RF-15.8 (menu da alça), RF-15.10 ("JOGAR") | Substituídos pelo widget (RF-15.1) e por SDD-24/25 (demais) |

SDD-15 RF-15.2 (`HudModel` imutável com `Version`) é **reaproveitado como padrão** em RF-23.9, com o `WidgetModel` descrito abaixo.

## 5. Requisitos funcionais

### RF-23.1: Janela do widget

- Classe `SideWar.Widget`, criada na GameThread (ADR-002), `WS_POPUP`, **owned pela janela da faixa** (`SceneWindow`; SDD-03 RF-03.9, ADR-021). Como *owned*, o widget é ocultado e destruído junto com a faixa, nunca fica sozinho e herda o z-order relativo dela; continua sem `WS_EX_TRANSPARENT` e sem ativação.
- `dwExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`. **`WS_EX_TRANSPARENT` nunca é aplicado.** `dwStyle = WS_POPUP`, sem `WS_VISIBLE`.
- Mesmos `DwmSetWindowAttribute` de SDD-03 RF-03.2 (transições desligadas, sem cantos do DWM, excluída do Peek).
- WndProc: `WM_MOUSEACTIVATE → MA_NOACTIVATE`; `WM_NCHITTEST → HTCLIENT`; `WM_SETCURSOR` conforme RF-23.8; proibido `SetForegroundWindow`, `SetActiveWindow`, `SetFocus`, `BringWindowToTop`, `SwitchToThisWindow` (SDD-03 RF-03.5).
- Mostrar com `ShowWindow(SW_SHOWNOACTIVATE)` e `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)`.
- Superfície própria `LayeredSurface` (SDD-08 RF-08.1), `SourceConstantAlpha = 255` sempre (o widget **não** participa do "peek" e do esmaecimento da faixa; o alfa 230 está no pixel do fundo, RF-23.3).
- Visibilidade: o widget segue os mesmos `HiddenReason` da faixa (SDD-07, SDD-06): oculta junto, reaparece junto, nunca fica visível sozinho.

### RF-23.2: Geometria e posição

Entradas (contrato **de SDD-21 RF-21.1**, `TaskbarLayout`): `Edge`, `TaskbarRect` (DIP, origem no monitor), `HeightDip`, `LeftFreeZone` (`X0`..`X1` em DIP; largura = `X1 − X0`), `Source` e `Dpi`. Onde esta ficha diz `BarRect`/`FreeZoneLeft`, leia `TaskbarRect`/`LeftFreeZone`.

- **Largura:** 420 DIP.
- **Altura (DP-23.3, aceita por ADR-030):** `max(HeightDip da barra, 46)` DIP: o widget cresce para cima sobre a faixa em no máximo 6 DIP quando a barra é mais baixa que 46, para caber o botão de 44 DIP mais a borda. O conteúdo é centralizado verticalmente.
- **Caso normal:** barra inferior **e** `FreeZoneLeft.Width ≥ 420 DIP`. O widget é posicionado com `left = FreeZoneLeft.Left` e a base alinhada à base da barra (`top = BarRect.Bottom − altura`), **sobre** a barra.
- **Fallback:** barra não inferior (Top, Left, Right) **ou** `FreeZoneLeft.Width < 420 DIP` **ou** geometria indisponível. O widget é desenhado no **canto inferior esquerdo da faixa de 96 DIP** (SDD-22), sobre a cena, com `left = faixa.Left` e `bottom = faixa.Bottom`, mesmas dimensões. No fallback, o **núcleo passa para `x = 428`** no layout, via `LayoutChanged` (SDD-22 RF-22.2; DP-23.2, resolvida por ADR-032), para não ficar coberto.
- A escolha (normal ou fallback) é recalculada **apenas por evento** de SDD-21 (mudança de geometria da barra, de DPI ou de monitor), com debounce de 200 ms (padrão de SDD-05 RF-05.6). Nenhum polling.
- Tamanho em px físicos: `round(420 × dpi/96)` × `round(H × dpi/96)`; a geometria é arredondada uma vez por layout (SDD-05 seção 11).
- Em mudança de posição sem mudança de tamanho: apenas `SetWindowPos(NOSIZE|NOACTIVATE)`; sem novo render. Com mudança de tamanho: `LayeredSurface.Resize` e frame completo.

### RF-23.3: Aparência da janela e layout interno

- Fundo: `bg-painel` `#0b1a2e` com **alfa 230**; borda **1 DIP** `borda-painel` `#1f3350`; **raio 4 DIP** (SDD-20 RF-20.4). Esse fundo **não existe no design** (lá o widget é só conteúdo sobre a barra simulada); aprovação do usuário pendente em **D-17** (recomendação: aprovar).
- O fundo e a borda são desenhados **uma vez** em um `SKImage` de cache (por escala) e blitados por frame de conteúdo. Os cantos de raio 4 são rasterizados com antialias no cache; pixels fora do arco têm alfa 0 e **não recebem clique** (SDD-04 RF-04.4).
- Conteúdo em linha, alinhado ao centro vertical, na ordem:

| Elemento | Largura (DIP) | Observação |
|---|---|---|
| inset esquerdo | 8 | medido a partir da borda externa da janela |
| coluna créditos | 78 | RF-23.4 |
| gap | 12 | |
| coluna onda | 56 | RF-23.5 |
| gap | 12 | |
| selo de presença | natural (texto + padding 6+6 + borda 1+1) | RF-23.6 |
| espaço flexível | restante | empurra o nav para a direita (`margin-left: auto` do design) |
| nav | 182 (4 × 44 + 3 × 2) | RF-23.7 |
| inset direito | 8 | |

- **Verificação de encaixe (DP-23.1):** `8 + 78 + 12 + 56 + 12 + selo + 12 + 182 + 8 = 368 + selo`. Para caber em 420 DIP o selo precisa ter **≤ 52 DIP**. O selo "PRESENTE ×2,0" (Silkscreen 10 DIP, 13 caracteres) **tende a ser maior**; a largura real depende dos TTF e **precisa ser medida**. O design não resolve isso. **Escalada ao usuário como D-16 (ADR-032)**; recomendação: selo só com `×2,0`/`×0,25`, com cor e borda indicando o estado e o rótulo completo no nome acessível (UIA). Bloqueia o layout final.

### RF-23.4: Coluna de créditos (78 DIP)

- Linha 1: créditos atuais, **Silkscreen 14 DIP**, `ambar` `#f5b335`, line-height 1,1. Exemplo do design: `48.2K` (no jogo, `48,2K`, ADR-029).
- Linha 2: taxa, **JetBrains Mono 10 DIP**, `menta` `#5cf0b0`, formato `+<taxa>/s`. Exemplo do design: `+312/s` (presente) e `+39/s` (ausente).
- As duas linhas são centralizadas verticalmente como bloco; texto recortado em 78 DIP (sem quebra).
- Valor e formato: SDD-27. Taxa efetiva (já com multiplicador de presença): SDD-25/26.
- **Badge "+X" de ganhos pendentes:** enquanto houver ganhos pendentes do card "Bem-vindo de volta" recolhido (SDD-25 RF-25.9), o widget mostra o badge "+X" (`X` = créditos pendentes formatados, SDD-27 RF-27.2) **junto aos créditos**, como **pílula de 14 DIP** (Silkscreen 9 DIP, `ambar`) que **substitui a linha da taxa** enquanto houver pendência (ADR-032). Ele é **clicável** e **reabre o card** (SDD-24); o clique também conta como interação de presença (RF-23.7). Some quando o pendente é coletado. O visual do badge segue SDD-24; o `WidgetModel` ganha o campo do pendente exibido.

### RF-23.5: Coluna de onda (56 DIP)

- Linha 1: `<atual>/<total>`, **Silkscreen 12 DIP**, `txt-principal` `#e8f0ff`, line-height 1,1. Exemplo: `37/60`.
- Linha 2: rótulo `onda`, **JetBrains Mono 10 DIP**, `txt-widget-rotulo` `#a9b6c8`.

### RF-23.6: Selo de presença

- **Silkscreen 10 DIP**, `white-space: nowrap`, padding **4 DIP vertical × 6 DIP horizontal**, borda **1 DIP** na mesma cor do texto, sem fundo próprio, raio 0.
- Estados (valores do design):

| Estado | Texto | Cor do texto e da borda |
|---|---|---|
| Presente | `PRESENTE ×2,0` | `selo-presente` `#5cf0b0` |
| Ausente | `AUSENTE ×0,25` | `selo-ausente` `#a9b6c8` |

- O multiplicador (`×2,0`, `×1,5`, `×2,5`, `×0,25`) vem do modelo (SDD-25). O design usa ponto (`×2.0`); vale a vírgula (ADR-029, SDD-27 RF-27.2).
- **Chip do bônus ×3 (ADR-032):** quando o banner de chefe ocupa o slot de banner com o bônus de pico ativo (SDD-26 RF-26.3, RF-26.12), o widget mostra o chip **"×3 0:59"** (contagem regressiva de 60 s) **no lugar do selo de presença, no mesmo espaço** (decisão do Tech Lead na T5.2, ADR-032: o chip, ≈ 61 DIP no pior DPI, não cabe ao lado do selo; começa onde o selo começa e termina ≥ 2 DIP antes do nav); o **nome acessível continua com presença e pico** ("PRESENTE ×2,0, bônus ×3 0:59"); ao fim do bônus o selo volta. Entra no `WidgetModel`.
- O selo é informativo: não é alvo de clique próprio (mas o clique sobre ele conta como interação, RF-23.8).

### RF-23.7: Nav de 4 botões

- Contêiner: 4 botões lado a lado, **gap 2 DIP**, alinhados à direita (`margin-left: auto`), largura total 182 DIP.
- Botão: **44×44 DIP**, raio **4 DIP**, sem borda, fundo transparente, ícone centralizado, cor do ícone `txt-principal` `#e8f0ff`.
- Ícones de SDD-20 RF-20.5 (18 DIP, traço 1,5): `arsenal`, `fases`, `missoes`, `migrar`. Ordem: Arsenal, Fases, Missões, Migrar. Rótulos de acessibilidade do design: "Arsenal", "Fases", "Missões", "Migrar" (o rótulo "Fases" abre a aba cujo título no painel é **"CAMPANHA"**, como no design; ADR-031) (guardados em `Strings.cs` para tooltip/UIA futuros; **não** desenhados).
- **Hover:** fundo `hover-nav` `rgba(255,255,255,.08)` no retângulo do botão (raio 4).
- **Ativo** (aba aberta no painel): fundo `ativo-nav` `rgba(255,255,255,.12)` **e** barra âmbar `ambar` `#f5b335` de **16×3 DIP**, raio 2, posicionada a `bottom: 2 DIP`, `left: 14 DIP` (centralizada no botão).
- **Badge (somente Missões):** círculo de **14×14 DIP**, raio 7, `ambar`, em `top: 3 DIP`, `right: 3 DIP`; número em **JetBrains Mono 9 DIP**, `bg-trilho-fundo` `#071323`, centralizado. O design mostra `2`. O número é a contagem de missões coletáveis (SDD-26); contagem `0` oculta o badge. Acima de 9 mostra `9+` (DP-23.5, aceita por ADR-030).
- Hover e ativo não são exclusivos: ativo + hover usa o fundo do ativo (`.12`), sem soma.
- Altura do botão com barra baixa: ver RF-23.2 e DP-23.3.

### RF-23.8: Interação, hit-test e cursores

- **Clique no botão do nav** (`WM_LBUTTONDOWN` + `WM_LBUTTONUP` no **mesmo** botão): alterna a aba do painel (SDD-24): se a aba clicada já está ativa, o painel fecha; senão, abre ou troca para ela (comportamento de `openTab` do design). Emite `PanelCommand.ToggleTab(tab)`; o painel pertence a SDD-24.
- `WM_LBUTTONDOWN` faz `SetCapture(hwnd)` apenas sobre um botão do nav, para completar o clique, e `ReleaseCapture` no `WM_LBUTTONUP`/`WM_CAPTURECHANGED` (SDD-03 RF-03.5). Sem captura fora de botão.
- **Interação para presença (SDD-25):** todo `WM_LBUTTONDOWN` (e `WM_RBUTTONDOWN`) **em qualquer ponto do widget com alfa ≥ 1**, incluindo créditos, onda e selo, chama `IPresenceSink.NotifyInteraction()` (nome proposto; contrato é de SDD-25) **uma vez por pressionamento**. Movimento do mouse e hover **não** contam.
- **Hit-test:** cliente em px → DIP (`px / scale`, SDD-22 RF-22.2; SDD-05 RF-05.5 foi substituída) → retângulo do botão (tabela de 4 retângulos calculada no layout, sem alocação). Fora de botão: alvo `WidgetBackground` (consome o clique; não repassa à barra de tarefas).
- **Cursores:** mão (`IDC_HAND`) sobre botões do nav; seta (`IDC_ARROW`) no resto; aplicados em `WM_SETCURSOR` e retornando TRUE (SDD-03 RF-03.5).
- **Hover:** `WM_MOUSEMOVE` atualiza o índice do botão sob o cursor; se mudou, incrementa a versão do modelo (RF-23.9). `TrackMouseEvent(TME_LEAVE)` limpa o hover em `WM_MOUSELEAVE`. **Sem polling de cursor.**
- Sem teclado: o widget não recebe foco nem teclas.
- **Botão direito (clique direito) abre o menu nativo de SDD-15 RF-15.8** (`TrackPopupMenuEx`, ADR-010) com os itens vigentes de SDD-15 RF-15.8 (ADR-031 item 8): **Modo interativo** (✓ se ativo), **Pausar/Retomar**, **Ocultar**, **Monitor ▸**, **Configurações…**, **Sobre o SideWar…** e **Sair**, no ponto do clique, sem ativar o widget (a exibição usa o mesmo `ForegroundRestorer` de SDD-15). Isso **resolve DP-23.4** (antes: "não abre menu"). Continua contando **uma** interação de presença por pressionamento (acima); o item "Modo interativo" vem de SDD-04 RF-04.2 (o widget não tem controle de modo próprio nos 4 botões do nav).

### RF-23.9: Render apenas em mudança

- O widget lê um `WidgetModel` imutável (struct) com `Version`: créditos exibidos (texto formatado), taxa exibida, onda atual/total, estado e multiplicador de presença, contagem do badge, aba ativa, índice de hover. A versão sobe **somente quando algum campo exibido muda** (comparação do texto já formatado, não do valor bruto).
- Atualização de créditos e taxa (valores que mudam com o tempo) é **coalescida em no máximo 2 redesenhos por segundo** (deadline no host, SDD-10); se o texto exibido não mudou entre dois deadlines, **nenhum** Present.
- Cada redesenho é de **frame completo da janela** (janela ≤ 0,05 Mpx; independe de `RenderOptions.PartialUpdates`, SDD-08 RF-08.7).
- Sem mudança de versão: **0 frames, 0 `UpdateLayeredWindow`, 0 wakes** (SDD-08 CA-08.4).
- Sem animações no widget (o design não tem animação no widget). Nenhum timer enquanto estático.
- Ordem de desenho: fundo em cache → créditos → onda → selo → hover/ativo de cada botão → ícones (cache Alpha8 de SDD-20) → barra de ativo → badge.
- Zero alocação por frame (SDD-08 RF-08.8): textos estáticos como `SKTextBlob`; números por `Span<char>`.
- Saver (SDD-07 RF-07.5): sem efeito adicional; o widget já é estático.
- `reducedMotion`: sem efeito (sem animação).

### RF-23.10: Z-order acima da barra de tarefas

- O widget é `HWND_TOPMOST` e deve ficar **acima de `Shell_TrayWnd`**. Quando o usuário clica na barra, o Windows eleva `Shell_TrayWnd` e o widget pode ficar por baixo.
- Gatilho: o evento de foreground de SDD-06 (`EVENT_SYSTEM_FOREGROUND`) cuja janela de foreground é de classe `Shell_TrayWnd` (ou `Shell_SecondaryTrayWnd`, ou o host do Menu Iniciar/Pesquisa, conforme SDD-06/21). Ação: `SetWindowPos(widget, HWND_TOPMOST, 0,0,0,0, SWP_NOACTIVATE|SWP_NOMOVE|SWP_NOSIZE|SWP_NOOWNERZORDER)`.
- **Debounce de 100 ms** (deadline do host; coalesce rajadas). Não reafirmar quando oculto (SDD-03 RF-03.6).
- Ordem entre janelas do jogo ao reafirmar: faixa (SDD-22), widget, painel e cards (SDD-24), para que as de cima fiquem acima. Quem executa a sequência é o **`ZOrderCoordinator`** único em `Platform.Windows`, dono do z-order de faixa, widget, painel e cards (DP-23.6, ADR-032; SDD-03 RF-03.9).
- O widget nunca chama `SetForegroundWindow` para isso.

### RF-23.11: DPI, monitor e visibilidade

- Per-Monitor V2 (SDD-05 RF-05.1/05.6): em `WM_DPICHANGED`, ignorar o retângulo sugerido, recalcular a geometria (RF-23.2), recriar a superfície e o cache de fundo e de ícones (SDD-20), marcar frame completo.
- O monitor é o da barra de tarefas à qual o widget se acopla; em fallback, o da faixa (SDD-22).
- Fullscreen (SDD-06), ocultar (SDD-07) e fim de sessão: o widget segue a faixa.

### RF-23.12: Substituições registradas

Ver a tabela da seção 4. O RF-23.1 substitui SDD-03 RF-03.3; o RF-23.10 ajusta SDD-03 RF-03.6 para o widget; o conjunto RF-23.3 a RF-23.8 substitui SDD-15 RF-15.1.

## 6. Requisitos não funcionais

- **RNF-23.1:** Com o jogo em regime e nenhuma mudança de texto exibido, o widget gera **0 Presents** em 30 s (meta **não comprovada**).
- **RNF-23.2:** Com créditos mudando continuamente, o widget gera **≤ 2 Presents/s** (meta **não comprovada**).
- **RNF-23.3:** Composição do widget p95 ≤ 0,3 ms e `UpdateLayeredWindowIndirect` p95 ≤ 0,5 ms em 150 % (meta **não comprovada**; SDD-16 P-11/P-12 são do cenário pesado).
- **RNF-23.4:** Memória da superfície do widget ≤ 0,5 MB a 150 % e ≤ 1 MB a 200 % (DIB BGRA; meta **não comprovada**).
- **RNF-23.5:** Zero `WM_SETFOCUS` e zero mudanças de foreground causadas pelo processo em 100 cliques no widget (herda SDD-03 RNF-03.3).
- **RNF-23.6:** Latência clique → aba aberta/fechada (estado do botão visível) ≤ 50 ms (SDD-16 P-15; meta **não comprovada**).
- **RNF-23.7:** Reafirmação de z-order: ≤ 10 `SetWindowPos` por segundo no pior caso (debounce de 100 ms; meta **não comprovada**).
- **RNF-23.8:** Nenhuma consulta de cursor, de UIA ou de timer enquanto o widget estiver estático e sem eventos.

## 7. Estratégia de implementação

1. `WidgetLayout` (Runtime, puro): recebe largura/altura em DIP, largura do selo e devolve retângulos de colunas, selo e botões; testado em Linux por tabela (inclui o encaixe de DP-23.1).
2. `WidgetGeometry` (Runtime, puro): escolhe normal ou fallback e calcula `PixelRect` a partir do contrato de SDD-21.
3. `WidgetModel` e `WidgetModelBuilder` (Runtime): versão por texto exibido; coalescência de 2 Hz.
4. `WidgetRenderer` (Rendering): fundo em cache, textos (SDD-20 `Typography`), ícones (SDD-20 `IconCatalog`), estados de hover, ativo e badge.
5. `WidgetWindow` (Platform.Windows): estilos, WndProc, hit-test, cursor, captura, `NotifyInteraction`, `PanelCommand`.
6. Reafirmação de z-order integrada ao evento de foreground de SDD-06 (debounce 100 ms).
7. Testes (seção 10).

## 8. Dependências

SDD-20 (tokens, tipografia, ícones), SDD-21 (geometria da barra: zona livre, altura, borda, DPI), SDD-04 (contrato de alfa e hit-test), SDD-03 (janela, não ativação, laço, conjunto de janelas e ownership, RF-03.9), SDD-15 (menu nativo do clique direito), SDD-06 (evento de foreground e fullscreen), SDD-08 (superfície), SDD-10 (política de render). Consumidor: SDD-24 (recebe `PanelCommand`, informa a aba ativa e a contagem do badge). Referências sem dependência: SDD-22 (faixa, fallback de posição), SDD-25 (presença), SDD-26 (missões), SDD-27 (formatação).

## 9. Critérios de aceitação

- **CA-23.1:** `GetWindowLongPtr(GWL_EXSTYLE)` do widget contém `LAYERED|TOOLWINDOW|NOACTIVATE|TOPMOST` e **não** contém `TRANSPARENT`; `GetWindow(GW_OWNER)` devolve a **janela da faixa** (SDD-03 RF-03.9) (T-23.1, WindowsInteractive).
- **CA-23.2:** Com o Bloco de Notas em foco digitando, 100 cliques nos 4 botões e no resto do widget não mudam `GetForegroundWindow()` e o texto continua indo ao Bloco de Notas (T-23.2; mesmo método de SDD-03 CA-03.2; MTC-23.2).
- **CA-23.3:** Geometria: em barra inferior de 48 DIP com zona livre ≥ 420 DIP, o widget mede 420×48 DIP, com `left` na zona livre e base na base da barra; com zona < 420 DIP ou barra Top/Left/Right, aparece no canto inferior esquerdo da faixa de 96 DIP (T-23.3 por tabela, 100/125/150/175/200 %; MTC-23.3).
- **CA-23.4:** O conteúdo coincide com o design: valores de RF-23.3 a RF-23.7 (fontes, tamanhos, cores, larguras 78/56, botões 44×44, gap 2, barra de ativo 16×3, badge 14 DIP). Verificação por golden **sem texto** e por *bounding boxes* de texto (T-23.4, SDD-08 seção 11; MTC-23.1 lado a lado com o artefato).
- **CA-23.5:** Clicar em cada botão do nav emite `ToggleTab` da aba correta; clicar de novo na ativa fecha; o botão ativo mostra fundo `.12` e a barra âmbar; o badge de Missões some com contagem 0 (T-23.5).
- **CA-23.6:** Com o jogo parado, o contador de Presents do widget fica em 0 durante 30 s sem input (RNF-23.1). Com créditos subindo, ≤ 2 Presents/s (T-23.6).
- **CA-23.7:** Ao trazer `Shell_TrayWnd` ao foreground (clique na barra), o widget volta a ficar acima da barra em ≤ 150 ms e o número de `SetWindowPos` em rajada de 10 eventos em 100 ms é 1 (T-23.7; MTC-23.4).
- **CA-23.8:** Cada pressionamento de botão do mouse em qualquer ponto do widget chama `NotifyInteraction` exatamente uma vez; movimento e hover não chamam (T-23.8).
- **CA-23.9:** Pixels fora do arco de raio 4 (cantos) têm alfa 0 e deixam o clique passar à janela de baixo (T-23.9).
- **CA-23.10:** A soma de larguras de RF-23.3, com a largura **medida** do selo mais longo (`AUSENTE ×0,25`) nos TTF reais, não excede 420 DIP, ou a decisão DP-23.1 está registrada (T-23.11).
- **CA-23.11:** O clique direito no widget abre o menu nativo com os itens vigentes de SDD-15 RF-15.8 (Modo interativo, Pausar/Retomar, Ocultar, Monitor, Configurações, Sobre, Sair) sem mudar `GetForegroundWindow()` ao final e sem acionar o nav; "Modo interativo" alterna o modo da faixa (T-23.10, MTC-23.5).

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-23.1 | Estilos e owner do widget (CA-23.1) | WindowsInteractive |
| T-23.2 | `TestFocusTarget` + 100 cliques com `SendInput` | WindowsInteractive |
| T-23.3 | `WidgetGeometry` por tabela: barra Bottom/Top/Left/Right, zona 300/420/600 DIP, alturas 40/48/52, DPI 100–200 % | Linux |
| T-23.4 | Golden sem texto + *bounding boxes* de texto | Linux + Windows |
| T-23.5 | Hit-test dos 4 botões e transições de estado (hover, ativo, badge) | Linux |
| T-23.6 | Contador de Presents: estático (0) e créditos subindo (≤ 2/s) com FakeClock | Linux |
| T-23.7 | Rajada de eventos de foreground em `Shell_TrayWnd`; `SetWindowPos` coalescido; widget acima da barra | WindowsInteractive |
| T-23.8 | `NotifyInteraction` por pressionamento; hover não conta | Linux |
| T-23.9 | Alfa dos cantos e passagem de clique (`WindowFromPoint`, `SendInput`) | WindowsInteractive |
| T-23.10 | Clique direito: menu nativo (itens e estados), contagem de 1 interação, foreground restaurado (SDD-15 `ForegroundRestorer`) | WindowsInteractive |
| T-23.11 | `WidgetLayout` com larguras medidas dos TTF: soma ≤ 420 DIP em 100–200 % (CA-23.10) | Windows (precisa dos TTF) |
| MTC-23.1 | Comparar o widget com o artefato (100/150/200 %) | Manual |
| MTC-23.2 | Digitar no VS Code e clicar no widget | Manual |
| MTC-23.3 | Barra de 40 DIP (Windows 10), 48 DIP (Windows 11), barra à esquerda/topo, ocultação automática, 2 monitores | Manual |
| MTC-23.4 | Clicar na barra e no Menu Iniciar com o widget visível; observar cintilação e ordem | Manual |
| MTC-23.5 | Clicar com o botão direito no widget: menu nativo (Modo interativo, Pausar/Retomar, Ocultar, Monitor, Configurações, Sobre, Sair); o foco volta ao app anterior | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Conteúdo nominal (368 DIP + selo) não caber em 420 DIP com os TTF reais (DP-23.1) | Alta | Alto | Medir na E5 (T-23.11); decisão do Tech Lead antes de implementar o layout final |
| `Shell_TrayWnd` reassumir o topo a cada clique e causar cintilação do widget | Média | Médio | Debounce de 100 ms; reafirmar só em foreground da barra; medir em MTC-23.4 |
| Barra do Windows 11 alinhada à esquerda deixar zona livre 0 e o fallback ser o caso comum | Alta | Médio | Fallback obrigatório (RF-23.2); SDD-21 define a detecção; SDD-22 decide o que fazer com o núcleo (DP-23.2) |
| Altura de barra de 40 DIP não comportar botões de 44 DIP (DP-23.3) | Alta | Médio | Decisão pendente; botões não excedem a janela |
| Outra janela topmost (Teams, players) ficar acima do widget | Média | Baixo | Reafirmação; limitação documentada (SDD-03 seção 11) |
| UIA lento ou indisponível atrasar a posição do widget | Média | Baixo | Fallback de ADR-023; consulta por evento, nunca por frame |
| Miniaturas da barra (hover em um app) e *jump lists* cobrirem o widget | Média | Baixo | Comportamento do Windows; fora de escopo |
| Clique em área vazia do widget ser "engolido" e o usuário achar que a barra travou | Baixa | Baixo | Widget cobre só a zona livre; área vazia da barra continua clicável fora dele |
| Cantos de raio 4 sem AA ficarem serrilhados em escalas fracionárias | Baixa | Baixo | AA no cache do fundo (RF-23.3) |

### Decisões pendentes (lacunas reais; não decididas aqui)

| ID | Questão | Proposta / observação | Bloqueia |
|---|---|---|---|
| DP-23.1 | O design soma 368 DIP + largura do selo; o selo (≥ ~80 DIP, a medir) estoura os 420 DIP do widget | Medir com os TTF; se estourar, o Tech Lead escolhe entre reduzir gaps/insets, encurtar o texto do selo ou alargar o widget. Esta ficha **não** escolhe. **Escalada como D-16 (ADR-032)** | Layout final do widget |
| DP-23.2 | No fallback (canto inferior esquerdo da faixa) o widget de 420×48 cobre o sprite do núcleo (design: `left 10..94`, `top 40..96` na faixa) | **Resolvida por ADR-032:** o núcleo vai para `x = 428` no fallback (RF-23.2) | SDD-22 |
| DP-23.3 | ~~Altura de barra 40–45 DIP vs. botões de 44~~ **Aceita (ADR-030):** altura = `max(barra, 46)` (RF-23.2) | n/a | n/a |
| DP-23.4 | Menu de contexto do widget: a alça (SDD-15 RF-15.8) tinha menu "Mover para o monitor, Tamanho, Borda, Ancoragem"; o design não define substituto | **Resolvido:** clique direito abre o menu nativo de SDD-15 RF-15.8 (Modo interativo, Ocultar, Configurações, Sobre, Sair); a bandeja mantém o mesmo menu | — |
| DP-23.5 | ~~Badge com contagem > 9~~ **Aceita (ADR-030):** `9+` (RF-23.7) | n/a | n/a |
| DP-23.6 | A rotina única que reafirma z-order de faixa, widget, painel e cards não tem dono | **Resolvida por ADR-032:** `ZOrderCoordinator` em `Platform.Windows`; ordem fixada no RF-23.10 | SDD-22/24 |
| DP-23.7 | Estado pressionado dos botões do nav e do estado desabilitado não estão no design | Sem estilo próprio: usar o estado de hover durante o pressionamento | RF-23.7 |

Itens do design **ausentes** (não inventados): estilo de foco por teclado (o jogo não usa teclado), tooltip de botões, animação do widget, estado "pausado".
