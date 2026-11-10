# SDD-24: Painel de 4 abas e cards (pergunta do chefe e "Bem-vindo de volta")

**Status:** Rascunho para revisão · **Etapa:** E5 · **Depende de:** SDD-20, SDD-23 · **Bloqueia:** SDD-25, SDD-26 (visuais)

## 1. Identificador e título

SDD-24: Três janelas layered Skia `WS_EX_NOACTIVATE` (ADR-021): o **painel** de 400×548 DIP com as abas Arsenal, Campanha, Missões e Migrar; o **card de pergunta do chefe** de 520×242 DIP; e o card **"Bem-vindo de volta"** de 472×612 DIP. Layout, estados visuais, hit-test, rolagem, hover e render por evento.

## 2. Objetivo

Dar ao jogador as telas de gestão do idle (melhorar torres, acompanhar a campanha, coletar missões, migrar) e os dois cards de decisão (pergunta Azure do chefe e relatório de retorno) **exatamente com o visual do design aprovado**, sem nunca roubar o foco, sem teclado, sem animação contínua e com **zero frames** quando nada muda.

## 3. Contexto técnico

- **ADR-021:** painel, cards, banners, caixas e selo são janelas layered Skia `WS_EX_NOACTIVATE`, criadas e desenhadas na T-Game; WPF só para Configurações e Sobre. **Nunca roubam foco e não têm teclado** (SDD-03 RF-03.5 vale integralmente: `MA_NOACTIVATE`, proibido `SetForegroundWindow`, `SetFocus`, `SetActiveWindow`, `BringWindowToTop`, `SwitchToThisWindow`).
- **ADR-018 / SDD-20:** o design é oficial e **todo valor visual vem de SDD-20 por nome de token** (`bg-painel`, `ambar`, `esp-12` etc.). Os valores em DIP que **não** são token (tamanhos de linha, larguras de botão, alturas de barra) foram **transcritos** de `Main.dc.html`, `Presence.dc.html` e `Offline.dc.html` e são citados aqui como número. Hex só aparece nesta ficha quando o design usa um valor que **não existe** como token (nenhum caso hoje).
- **ADR-019/022:** idle; o card de retorno vem de SDD-25; o comportamento de jogo (custos, ondas, missões, perguntas) é de SDD-25/26/27. **Aqui só visual, layout, estados, hit-test, render e hover.**
- **SDD-23 RF-23.8/23.10:** o widget emite `PanelCommand.ToggleTab(tab)` e informa badge e aba ativa; o z-order é reafirmado na ordem faixa, widget, painel e cards.
- **SDD-22 RF-22.3:** clicar em torre ou slot vazio emite `OpenArsenal(slotIndex)`; o painel abre o Arsenal focado nesse slot.
- **SDD-04 RF-04.4/04.6:** alfa ≥ 1 recebe clique, alfa 0 atravessa. Painel e cards são **sempre clicáveis** (`WS_EX_TRANSPARENT` nunca aplicado), independente do modo Passivo da faixa.
- **SDD-08:** `LayeredSurface`, zero alocação por frame, sem blur, sem `SKImageFilter`, retângulos sem antialias. **SDD-10 `RenderPolicy`:** render só em mudança.
- **Unidades:** 1 CSS px do design = 1 DIP; px físico = `round(DIP × dpi/96)` (SDD-05, SDD-20). Todas as coordenadas abaixo são DIP a 100 %.
- **Coordenadas internas:** origem = canto superior esquerdo da **caixa visível** (borda incluída), sem a margem da sombra.
- **Formato de números e textos:** SDD-27 (`CreditFormatter`, pt-BR) e `Strings.cs` (SDD-15 RF-15.11). O design mistura ponto e vírgula decimal (ponto em multiplicadores e compactos, vírgula em `0,7s`); **vale a vírgula decimal em todo lugar** (ADR-029): `×2,0`, `+4,1K`, `×0,25`, `×1,5`. Os valores abaixo já estão em vírgula (DP-20.7, DP-24.15).
- **Fontes:** Silkscreen e JetBrains Mono, regras de render de texto de SDD-20 RF-20.3 (Silkscreen sem antialias, tamanho físico inteiro, letter-spacing por `TextCache`, glifos `★ ☆ → ×` conforme DP-20.2).

## 4. Escopo e exclusões

**Escopo:** as três janelas (estilos, geometria, posição, sombra dura, DPI, z-order), cromo do painel (cabeçalho, título, fechar), corpo com rolagem (roda do mouse e barra arrastável), conteúdo e estados das quatro abas, card de pergunta (normal, correto, errado, tempo esgotado, explicação, minimizar), card de retorno, hit-test, cursores, hover, contagem de interação, render por evento, visibilidade e ciclo de vida.

**Exclusões:**

- regras de custo, dano, ondas, missões, prestígio, regiões e formatação numérica (SDD-27);
- presença, multiplicadores, auto-escala, cálculo offline, pendências e regras do card de retorno (SDD-25);
- agendamento, sorteio, ledger, tempo e efeito da pergunta do chefe (SDD-26);
- geometria da barra e slots (SDD-21), faixa, banners e caixas (SDD-22), widget e nav (SDD-23);
- tokens, tipografia, ícones e animações (SDD-20), sprites (SDD-09);
- as janelas WPF Configurações e Sobre.

**Substitui (o design vence; as fichas antigas não são editadas):**

| Ficha antiga | Item | Situação |
|---|---|---|
| SDD-15 | RF-15.3 a RF-15.6 (paleta de torres arrastável, menus e demais painéis da HUD antiga) | Substituídos pelo painel de 4 abas. Arraste de torre não existe mais (SDD-21 RF-21.8, SDD-27 RF-27.5) |
| SDD-01 | RF-01.3 (`CampaignWindow`, `ResultWindow`, `QuestionWindow` em WPF) | Já substituído por ADR-021; aqui o equivalente em Skia |
| SDD-19 | RF-19.7 (visual do card de pergunta) | Substituído por RF-24.9; regras em SDD-26 |

## 5. Requisitos funcionais

### RF-24.1: Janelas, estilos e z-order

Três classes de janela, todas na T-Game, top-level, **sem owner**, criadas no primeiro uso e **reaproveitadas** (`SW_HIDE` / `SW_SHOWNOACTIVATE`); destruídas no encerramento (SDD-03 RF-03.7). Ocultas: 0 frames, 0 wakes, superfície liberada.

| Janela | Classe | Caixa visível (DIP) | Janela (DIP, com sombra) |
|---|---|---|---|
| Painel | `SideWar.Panel` | 400 × 548 | 406 × 554 |
| Card de pergunta | `SideWar.QuestionCard` | 520 × 242 | 526 × 248 |
| Card de retorno | `SideWar.OfflineCard` | 472 × 612 | 478 × 618 |

- `dwExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`; `dwStyle = WS_POPUP`; **`WS_EX_TRANSPARENT` nunca**. Mesmos `DwmSetWindowAttribute` de SDD-03 RF-03.2.
- WndProc: `WM_MOUSEACTIVATE → MA_NOACTIVATE`; `WM_NCHITTEST → HTCLIENT` em toda a janela; `WM_SETCURSOR` conforme RF-24.11. Mostrar com `ShowWindow(SW_SHOWNOACTIVATE)` e `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)`.
- `SourceConstantAlpha = 255` sempre (painel e cards **não** esmaecem em Ausente nem participam do peek da faixa).
- **Sem teclado:** nenhuma janela recebe foco nem teclas; não há Esc, Tab, Enter nem atalho para os cards.
- **Z-order** (SDD-23 RF-23.10): faixa, widget, painel, cards. Entre os dois cards: **pergunta acima de retorno** (DP-24.11). A rotina única de reafirmação não tem dono (DP-23.6); esta ficha só exige que painel e cards sejam incluídos nela, na ordem acima.
- Visibilidade segue o widget e a faixa (`HiddenReason` de SDD-06/SDD-07): ocultam juntos e nunca ficam visíveis sozinhos, **exceto** o card de retorno, que aparece sozinho ao ficar visível (SDD-25 RF-25.9, D-11), sem ativar a janela.

### RF-24.2: Geometria, posição e sombra dura

**Sombra dura** (SDD-20 RF-20.4): retângulo preenchido, do tamanho da caixa visível, deslocado **(+6, +6) DIP**, desenhado **antes** do corpo, sem blur. Cor `sombra-painel` (painel) e `sombra-card` (os dois cards). A janela tem **6 DIP de margem à direita e embaixo** (tabela de RF-24.1); a caixa visível não muda. Tamanho físico da sombra = `round(6 × dpi/96)`; tamanho físico da janela = `round(largura × s) + round(6 × s)` por eixo, com `s = dpi/96`, arredondado **uma vez por layout**.

**Referência vertical:** `stripTop` = topo da faixa de 96 DIP (`SceneLayout.Compute`, SDD-22 RF-22.1, já com a margem de 2 px da ocultação automática). No design, o painel e os cards ficam com a base da caixa visível **8 DIP acima do topo da faixa** (painel: `bottom 152` = 144 da faixa+barra + 8; card de pergunta: contêiner de 250 para caixa de 242).

| Janela | Posição (caixa visível) |
|---|---|
| Painel | `left = WorkArea.Left + 16 DIP`; `bottom = stripTop − 8 DIP` |
| Card de pergunta | centralizado horizontalmente na faixa: `left = faixa.Left + (W − 520)/2` DIP; `bottom = stripTop − 8 DIP` |
| Card de retorno | centralizado horizontalmente: `left = faixa.Left + (W − 472)/2`; `bottom = stripTop − 8 DIP` |

- `W` = largura da faixa em DIP. Monitor = o da faixa (SDD-22). Pixel físico: `round(DIP × s)` aplicado à origem e ao tamanho, uma vez por layout.
- **Altura insuficiente** (monitores pequenos ou escala alta: `stripTop − 8 − altura < WorkArea.Top`): `top = WorkArea.Top`, e a caixa passa a **cobrir** parte da faixa e do widget (aceito, são janelas efêmeras). Se `altura > WorkArea.Height`, o corpo rolável do painel encolhe (RF-24.4); o card de retorno não encolhe (DP-24.12).
- A posição é recalculada **só por evento** (`TaskbarLayoutChanged`, `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`, com o debounce de 200 ms de SDD-05 RF-05.6) e ao mostrar. Mudança de posição sem mudança de tamanho: apenas `SetWindowPos(NOSIZE|NOACTIVATE)`, sem render.

### RF-24.3: Painel: cromo e corpo

Caixa 400 × 548, `box-sizing: border-box`.

| Elemento | Valor |
|---|---|
| Sombra | RF-24.2, `sombra-painel` |
| Borda | **2 DIP** `borda-painel` (retângulo na cor da borda e, dentro, o corpo `bg-painel` recuado 2 DIP; sem AA) |
| Área interna | 396 × 544 (origem em (2, 2)) |
| Cabeçalho | altura **52 DIP** + filete inferior de **1 DIP** `borda-painel` = **53 DIP** (ADR-031: decidido; o design aplica `height: 52` com `border-bottom: 1px` em caixa de conteúdo; ver DP-24.18) |
| Padding do cabeçalho | `0 8 0 16` (`esp-8` à direita, `esp-16` à esquerda), itens centralizados na vertical, `space-between` |
| Título | Silkscreen **14**, letter-spacing **1**, `ambar`; texto por aba: `ARSENAL`, `CAMPANHA`, `MISSÕES & PRESENÇA`, `MIGRAR (PRESTÍGIO)` |
| Botão fechar | **44 × 44**, sem fundo, ícone `fechar` (SDD-20 RF-20.5; 16 DIP, traço 1,8) em `txt-rotulo`, centralizado; no design ocupa x = 346..390 (borda direita da área interna 398 − `esp-8`) |
| Corpo (viewport) | x 2..398, y 55..546: **396 × 491 DIP**, `bg-painel` |
| Padding do corpo | `14 16` (`esp-14` vertical, `esp-16` horizontal); **faz parte do conteúdo rolável** (como `overflow: auto` do design). Largura do conteúdo: **364 DIP** (x 18..382) |
| Gap entre cards e linhas | `esp-10` (aba Migrar: `esp-12`) |

- O conteúdo rola dentro do viewport (RF-24.4); tudo fora de `y ∈ [55, 546)` é **recortado** (clip) e **não é clicável** (RF-24.11).
- A aba é uma coluna: `flex-direction: column`. As alturas das linhas dependem da métrica de linha "normal" das fontes (`TextCache`); a altura total do conteúdo é calculada por `PanelLayout` (função pura, Runtime) a cada mudança de dados e de escala, **não por frame**. Alturas fixas conhecidas: linha de torre **74** (10 + 52 + 10 + borda 2), linha de fase mínima **44**, botões conforme RF-24.5 a RF-24.8.
- Trocar de aba: rola para o topo (offset 0); o painel mantém o offset enquanto a mesma aba estiver aberta.
- Comandos de entrada do painel: `PanelCommand.ToggleTab(tab)` (SDD-23), `OpenArsenal(slotIndex)` (SDD-22), `Close`. O painel informa ao widget a aba ativa (e ao fechar, "nenhuma"). Fechar no botão ou por `ToggleTab` da aba ativa; **clicar fora do painel não fecha** (o clique atravessa; sem foco não há "perder foco").

### RF-24.4: Rolagem do corpo

- **Roda do mouse:** `WM_MOUSEWHEEL` sobre o painel. Passo proposto: `delta/120 × 40 DIP` por detente (DP-24.1; não vem do design). Offset limitado a `[0, max(0, conteúdo − 491)]`. Cada mudança de offset sobe a versão do modelo (RF-24.13).
- **Risco:** o Windows só entrega `WM_MOUSEWHEEL` à janela sob o cursor se a opção **"Rolar janelas inativas ao passar o mouse sobre elas"** estiver ligada; como o painel nunca é ativado, com a opção **desligada** a roda vai para a janela com foco e o painel **nunca a recebe**. Mitigações: (1) a barra arrastável (abaixo) funciona **sempre**; (2) o app lê a opção (`SPI_GETMOUSEWHEELROUTING`, a confirmar na E5; DP-24.1) e, se desligada, mostra a barra sempre visível em vez de só em hover; (3) **não** instalar hook global de mouse para isso (custo e risco; alternativa registrada em DP-24.1).
- **Barra de rolagem (fallback), visual proposto** (o design usa a barra nativa do navegador, sem valores; tokens SDD-20; DP-24.2): só existe se `conteúdo > 491`. Trilho de **8 DIP** de largura em `x = 382 + 4 = 386` a `394` (dentro do padding direito de 16, a 4 DIP da borda interna), de `y = 59` a `y = 542`, cor `bg-trilho`. Polegar com altura `max(44, trilho × 491 / conteúdo)`, cor `txt-rotulo` (5,03:1 sobre `bg-trilho`, ≥ 3:1 de RF-20.8); em hover ou arraste, `txt-secundario`. Área de clique da barra: **16 DIP** de largura (x 382..398) para facilitar o alvo; polegar ≥ 44 DIP de altura.
- **Arrastar o polegar:** `WM_LBUTTONDOWN` sobre ele faz `SetCapture(hwnd)` apenas durante o arraste (como SDD-23 RF-23.8) e `ReleaseCapture` em `WM_LBUTTONUP`/`WM_CAPTURECHANGED`; o offset acompanha `y` do cursor proporcionalmente.
- **Clique no trilho** fora do polegar: rola uma página (`491 − 40` DIP) na direção do clique.
- Sem inércia, sem animação de rolagem: salto direto ao offset.

### RF-24.5: Aba Arsenal

Ordem vertical, gap `esp-10`:

**5.1 Card de auto-escala** (largura 364): padding `esp-12`, gap `esp-12`, itens centralizados; fundo `bg-card`, borda **1 DIP** `borda-forte`.

| Elemento | Valor |
|---|---|
| Coluna de texto | `flex: 1`, gap `esp-4` |
| Título | Silkscreen **11**, letter-spacing **1**, `txt-principal`: `AUTO-ESCALA (AUSENTE)` |
| Descrição | JetBrains Mono **11**, `txt-terciario`, line-height **1,45**: `Enquanto você trabalha em outra janela, usa 50% dos créditos para melhorar a torre mais barata a cada 15 min.` |
| Toggle | área de clique **44 de altura** (`min-height 44`), gap `esp-8`: caixa **20 × 20** + texto `ON` Silkscreen **10** `menta` |
| Caixa ON | **20 × 20**, preenchida com `menta` e marca de seleção; o design usa `<input type=checkbox accent-color: menta>` (marca do navegador, DP-24.6); proposta: marca em `bg-trilho-fundo` |

- O toggle ligado/desligado vem de `autoScaleOn` (SDD-25 RF-25.5, padrão ligado). Clique no toggle emite `SetAutoScale(bool)`. **O estado desligado não está no design** (DP-24.6): proposta de caixa 20×20 com borda 1 `txt-rotulo`, sem preenchimento, texto `OFF` em `txt-rotulo`.
- Área de clique ≥ 44 × 44 (largura natural ≈ 42 estendida a 44; SDD-20 RF-20.4).

**5.2 Linha de torre** (uma por torre desbloqueada na ordem do catálogo: Raio, Gelo, Canhão, Scale Set; largura 364): padding `10 12`, gap `esp-12`, itens centralizados; fundo `bg-item`, borda **1 DIP** `borda-painel`; opacidade da linha inteira = `1` (normal) ou **0,45** (bloqueada).

| Elemento | Valor |
|---|---|
| Slot do ícone | **52 × 52**, fundo `bg-trilho-fundo`, sprite centralizado em **44 × 44** com `object-fit: contain` (escala `min(44/w, 44/h)`, `Nearest`, do atlas pré-escalado de SDD-09) |
| Sprite por torre | Raio `tower-bolt` (44×44), Gelo `tower-frost` (39×44), Canhão `tower-cannon` (45×44), Scale Set bloqueado: `slot-empty` (42×26; SDD-22 RF-22.9, D-13) |
| Coluna de texto | `flex: 1`, `min-width 0`, gap **3** (valor do design, fora da escala `esp-*`) |
| Nome | Silkscreen **12**, `txt-principal`; ao lado, no mesmo baseline com gap `esp-8`, o nível em JetBrains Mono **10** `ambar` (`LV 14`; bloqueada: `BLOQ.`) |
| Tema Azure | JetBrains Mono **10**, `txt-rotulo` |
| Estatísticas | JetBrains Mono **10**, `txt-secundario` |
| Botão MELHORAR | largura **84** (mínimo do design), altura **44**, sem borda, raio 0; coluna centralizada com gap **2**: rótulo Silkscreen **10** + custo Silkscreen **9**; fundo `ambar`, texto `bg-trilho-fundo` |
| Botão bloqueado | mesmas dimensões; fundo `bg-trilho`, texto `txt-rotulo`; rótulo `FASE 09`, **sem** custo; sem hover e **sem clique** (DP-24.14) |

Texto por torre (transcrito do design; números são exemplos, valores vêm de SDD-27):

| Torre | Nome | Tema | Estatísticas | Custo (exemplo) |
|---|---|---|---|---|
| Raio | `RAIO` | `Máquina Virtual (Azure VM)` | `Dano 96 · Alc 3 · Cad 0,7s` | `1,2K` |
| Gelo | `GELO` | `WAF no Application Gateway` | `Lentidão 35% · Alc 2` | `860` |
| Canhão | `CANHÃO` | `Azure DDoS Protection` | `Dano 240 em área · Cad 2,1s` | `2,4K` |
| Scale Set (bloqueada) | `SCALE SET` | `VM Scale Sets` | `Desbloqueia na fase 09` | (nenhum) |

- O **custo** é o da próxima melhoria (SDD-27 RF-27.5), formatado por `CreditFormatter` (SDD-27 RF-27.2); o nível e as estatísticas também (SDD-27). A estatística do Scale Set não está no design (DP-24.5).
- Clique em MELHORAR (mouse down e up no mesmo botão) emite `UpgradeTower(id)` (SDD-27 RF-27.5). **Sem créditos suficientes**, o design não define estado (todos os botões do design são `ambar`): proposta em DP-24.3.
- Linha de torre não é clicável fora do botão.

**5.3 Linha "SEM SLOT"** (SDD-21 RF-21.8): torre com `slotIndex ≥ contagemDeSlots`; **não existe no design** (DP-24.4). Mesmo layout da linha de torre, com o selo `SEM SLOT` no lugar do nível (JetBrains Mono 10, `txt-widget-rotulo`), linha com opacidade **0,45** e botão no estilo do botão bloqueado com rótulo `SEM SLOT` (sem custo, sem hover, sem clique): a torre não é vendida nem perdida e volta ao slot sozinha quando o slot reaparecer. Ordem: após as torres com slot.

**5.4 Linha "construir em slot vazio"** (SDD-22 RF-22.3, SDD-27 RF-27.5): **não existe no design** (DP-24.5). Quando o Arsenal é aberto por `OpenArsenal(slotIndex)` com um slot **vazio**, o corpo começa com o rótulo `CONSTRUIR NO SLOT {n}` (JetBrains Mono **10**, `txt-rotulo`, letter-spacing **1**, como o rótulo `MISSÕES DE HOJE`) seguido de **uma linha por torre desbloqueada**, com o mesmo layout da linha de torre, nome/tema/estatísticas do nível 1, e botão **84 × 44** `ambar` com rótulo `CONSTRUIR` e o custo (`ceil(base × 1,5^n)`, SDD-27 RF-27.5). Clique emite `BuildTower(type, slotIndex)`. Sem foco em slot vazio, nenhuma linha de construir é mostrada (DP-24.5). Com `OpenArsenal` em torre existente, a linha da torre fica visível (rolagem automática até ela, uma vez, ao abrir).

### RF-24.6: Aba Campanha

Gap `esp-10`.

**6.1 Card da fase atual** (largura 364): padding `esp-14`, gap `esp-8`, coluna; fundo `bg-card`, borda **1 DIP** `azul`.

| Linha | Valor |
|---|---|
| Rótulo | JetBrains Mono **10**, `azul-claro`, letter-spacing **1**: `FASE ATUAL · 03` (número da fase com 2 dígitos) |
| Nome | Silkscreen **15**, `txt-principal`: `Controle da máquina` (título de `stages.json`, SDD-27 RF-27.9) |
| Barra | altura **8**, trilho `bg-trilho`, preenchimento `azul`, largura = `onda atual / total de ondas` da fase (design: 62 % para `37/60`), arredondada **uma vez** em px |
| Rodapé | linha `space-between`, JetBrains Mono **11**, `txt-secundario`: esquerda `Onda 37 de 60`; direita `Chefe na onda 40 · tema IaaS` |

- `Chefe na onda X` = próxima onda de chefe **≥ onda atual** (múltiplos de 20, SDD-27 RF-27.7); `tema Y` = `stageTopic` da fase (SDD-27 RF-27.9). Na onda de chefe vale a própria onda. Na fase concluída (loop, DP-27.11) o rodapé mostra a onda final e o chefe final.

**6.2 Lista de fases** (10 linhas, largura 364): padding `10 12`, gap `esp-12`, centralizadas, `min-height 44`; fundo `bg-item`, borda **1 DIP** `borda-painel`; opacidade `1`, ou **0,55** se bloqueada.

| Célula | Valor |
|---|---|
| Número | Silkscreen **11**, `txt-rotulo`, largura **22**: `01` … `10` |
| Título | JetBrains Mono **12**, `txt-principal`, `flex: 1`, **quebra de linha permitida** (a linha cresce) |
| Estrelas | Silkscreen **11**, `ambar`: `★★★`, `★★☆`, `☆☆☆` (estrelas de SDD-27 RF-27.9) ou `BLOQ.` (fase não desbloqueada) |

- Fase atual **não** recebe destaque na lista (o design não destaca; o card acima a representa).
- Linhas **não são clicáveis** (não há botão nem cursor no design; a partida avança sozinha, SDD-27 RF-27.9). Títulos do design (01 a 07) e provisórios (08 a 10, DP-27.10) vêm de `stages.json`.
- Glifos `★ ☆`: DP-20.2.

### RF-24.7: Aba Missões

Gap `esp-10`.

**7.1 Card de bônus de presença** (largura 364): padding `esp-14`, gap `esp-8`, coluna; fundo `menta-fundo`, borda **1 DIP** `menta-borda`.

| Elemento | Valor |
|---|---|
| Linha de título | `space-between`, alinhamento por baseline |
| Título | Silkscreen **12**, `menta`: `BÔNUS DE PRESENÇA` |
| Multiplicador | Silkscreen **16**, `menta`: `×2,0` (formato: DP-24.15) |
| Texto | JetBrains Mono **11**, `menta-texto`: `Você está presente há 42 min. Interaja ao menos uma vez a cada 5 min para manter o bônus.` (`42` = `PresentSeconds` em minutos, SDD-25 RF-25.3) |
| Faixas | grade de **3 colunas** iguais, gap `esp-6`; célula padding **6**, texto centralizado, JetBrains Mono **10**, duas linhas |

Faixas (valores do design): `0–15 min` / `×1,5`; `15–60 min` / `×2,0`; `60+ min` / `×2,5`.

- **Faixa atual** (a do tempo presente contínuo): fundo `menta`, texto `bg-trilho-fundo` (12,91:1). **Faixas inativas:** fundo `menta-faixa`, texto `menta-texto` (7,54:1). O par `bg-trilho-fundo` sobre `menta-faixa` é **proibido** (1,62:1, SDD-20 RF-20.8).
- Em **Ausente** ou Offline (×0,25): o design não define (DP-24.8); proposta: nenhuma faixa destacada e o multiplicador do título mostra o valor atual (`×0,25`) em `txt-widget-rotulo`.

**7.2 Rótulo de seção:** JetBrains Mono **10**, `txt-rotulo`, letter-spacing **1**, margem superior **4** (somada ao gap): `MISSÕES DE HOJE`.

**7.3 Linha de missão** (4 por dia, SDD-27 RF-27.11; largura 364): padding `10 12`, gap `esp-12`, centralizada; fundo `bg-item`, borda **1 DIP** `borda-painel`.

| Elemento | Valor |
|---|---|
| Coluna | `flex: 1`, gap `esp-6` |
| Título | JetBrains Mono **12**, `txt-principal`: ex. `Responda 3 perguntas de chefe` |
| Barra | altura **5**, trilho `bg-trilho`, preenchimento `ambar`, largura = progresso (ex. 100 %, 70 %, 40 %) |
| Linha de progresso | JetBrains Mono **10**, `txt-rotulo`: `{progresso} · {recompensa}`, ex. `42/60 min · +2 Reservas` |
| Botão COLETAR | só se a missão está **concluída e não coletada**; largura mínima **72**, altura **44**; fundo `ambar`, texto `bg-trilho-fundo`, Silkscreen **10**: `COLETAR` |

Exemplos do design: `Responda 3 perguntas de chefe` `3/3` `+1 baú raro` (concluída); `Escale 5 picos de demanda` `5/5` `+12K créditos` (concluída); `Fique presente por 60 min` `42/60 min` `+2 Reservas`; `Colete 10 caixas de suprimentos` `4/10` `+500 XP`.

- Clique em COLETAR emite `CollectMission(id)` (nome proposto; SDD-27 RF-27.11 entrega a recompensa só ao coletar). O **badge** do nav é de SDD-23.
- **Recompensa em renda** (SDD-27: `10 min de renda`) × design (`+12K créditos`): o design mostra um valor fixo; esta ficha exibe `+10 min de renda` (texto por missão em `Strings.cs`) para não variar a cada frame (DP-24.7). Missão **coletada**: não está no design; proposta em DP-24.7.

### RF-24.8: Aba Migrar

Gap **`esp-12`** (não 10).

**8.1 Card de migração** (largura 364): padding `esp-14`, gap `esp-8`, coluna; fundo `prestigio-fundo`, borda **1 DIP** `prestigio-borda`; título Silkscreen **13** `prestigio-claro`: `MIGRAR PARA NOVA REGIÃO`; corpo JetBrains Mono **11**, `prestigio-texto`, line-height **1,5**: `Reinicia ondas, torres e créditos da fase. Você mantém nível, estrelas e perguntas respondidas, e ganha Reservas que aumentam toda a produção para sempre.`

**8.2 Dois tiles** (grade de 2 colunas, gap `esp-8`): padding `esp-12`, gap `esp-4`, coluna; fundo `bg-item`, borda **1 DIP** `borda-painel`.

| Tile | Rótulo (10, `txt-rotulo`) | Número (Silkscreen **18**) | Sublinha (10, `menta`) |
|---|---|---|---|
| 1 | `RESERVAS ATUAIS` | `14` em `txt-principal` | `+14% produção` |
| 2 | `AO MIGRAR AGORA` | `+8` em `prestigio-claro` | `total +22%` |

- Valores (SDD-27 RF-27.10): atual = Reservas; produção = `1% × Reservas`; ao migrar = `floor(floor(ondaMax²/170) × multReservasRegião)`; total = `(Reservas + ganho)%`. Conferência do design: 14 + 8 = 22.

**8.3 Regiões:** coluna, gap `esp-6`, JetBrains Mono **11**, `txt-secundario`. Rótulo `REGIÕES` (10, `txt-rotulo`, letter-spacing 1). Linhas do design:

| Linha | Cor |
|---|---|
| `Brazil South · atual` | `txt-secundario` |
| `East US · inimigos +40% vida, Reservas ×1,5` | `txt-rotulo` |
| `West Europe · desbloqueia com 30 Reservas` | `txt-rotulo` |

- > **Ajustado por ADR-031.** **Escolha da região ao migrar:** na aba Migrar, as linhas de região são **selecionáveis (só as desbloqueadas)** e **definem a região da próxima migração** (altura de clique 44; a escolhida ganha o prefixo `→ ` e `txt-principal`; sem escolha, vale a região atual). Resolve DP-24.9 (parte da escolha).

**8.4 Botão MIGRAR:** `margin-top: auto` (encostado no fim do corpo quando o conteúdo é mais curto que o viewport; senão, após o conteúdo com gap `esp-12`), altura **48**, largura total 364, sem borda, fundo `prestigio`, texto `prestigio-tinta`, Silkscreen **12**, letter-spacing **1**: `MIGRAR · +8 RESERVAS` (o número é o ganho de 8.2). Hover: `prestigio-hover` (design: `brightness(1.08)`).

**Confirmação por segundo clique em 5 s** (**não está no design**; requisito desta ficha):

1. Primeiro clique (mouse up no botão): **não migra**. O botão entra em **armado** por **5 s** (deadline único do host, SDD-10) e o rótulo vira `CONFIRMAR · REINICIA TORRES E ONDAS`; fundo `perigo`, texto `perigo-tinta` (6,49:1), mesmas dimensões e tipografia (cores propostas, DP-24.9).
2. Segundo clique em até 5 s: emite `Migrate(regionId)`.
3. Passados 5 s sem segundo clique, **ou** troca de aba, **ou** fechar o painel, **ou** clique em qualquer outro ponto do painel que não seja o botão: volta ao estado normal. Rolar não cancela.
4. Cada transição (armar, desarmar por tempo, desarmar por clique) é **um** evento de render. Nenhum timer por segundo, nenhuma contagem visível.

**Indisponível** (`ondaMax` da fase < 15, SDD-27 RF-27.10): não está no design; proposta em DP-24.9 (botão no estilo bloqueado, rótulo `DISPONÍVEL NA ONDA 15`, sem clique nem hover).

### RF-24.9: Card de pergunta do chefe

Caixa **520 × 242**, `box-sizing: border-box`; fundo `bg-painel`; borda **2 DIP** `perigo`; sombra `sombra-card` (RF-24.2); padding `esp-16`; coluna com gap `esp-12`. Posição: RF-24.2. Conteúdo (origem do conteúdo em (18, 18)):

| Elemento | Valor |
|---|---|
| Cabeçalho | linha `space-between`, centralizada |
| Título | Silkscreen **11**, letter-spacing **1**, `perigo-claro`: `CHEFE · ONDA 40 · IAAS` (onda e tema da pergunta) |
| Timer | Silkscreen **11**, `perigo-claro`, sem letter-spacing: `0:45` (contagem regressiva, atualizada 1×/s) |
| Pergunta | JetBrains Mono **13**, line-height **1,45**, `txt-principal` |
| Alternativas | grade de **2 colunas** iguais, gap `esp-8`; cada opção `min-height 44`, padding `8 10`, texto à esquerda, JetBrains Mono **11**; texto `A · A Microsoft, sempre` (letra, ` · `, texto) |
| Rodapé | JetBrains Mono **10**, `txt-rotulo`: `Pergunta minimizada não pausa o trabalho: o chefe espera na entrada até o tempo acabar.` |

Pergunta do design: `Numa Máquina Virtual do Azure, quem aplica os patches do sistema operacional?` com `A · A Microsoft, sempre`, `B · Você (cliente)`, `C · Ninguém, é automático`, `D · O provedor de rede`.

**Estados das alternativas** (valores do design onde existem):

| Estado | Fundo | Borda 1 DIP | Texto |
|---|---|---|---|
| Normal | `bg-item` | `borda-forte` | `txt-principal` |
| Correta (design: B) | `menta-fundo` | `menta` | `txt-principal` |
| Errada, escolhida | `perigo-fundo` (proposta, DP-24.10) | `perigo` | `txt-principal` |

**Estados do card:**

| Estado | Comportamento visual |
|---|---|
| **Normal** (aguardando) | todas as opções normais e clicáveis; timer regressivo em `perigo-claro`; clique numa opção emite `AnswerBossQuestion(optionIndex)` (SDD-26 RF-26.15) |
| **Correto** | a opção escolhida em estado "correta"; as outras **desabilitadas** (sem hover, sem clique, estado normal); o design mostra este estado com a opção B |
| **Errado** | a escolhida em "errada" e a **certa** em "correta" (SDD-26 RF-26.6, "destaque da alternativa certa") |
| **Tempo esgotado** | timer `0:00`; nenhuma escolhida; a certa em "correta"; opções desabilitadas (DP-24.10) |

- **Explicação após responder** (não está no design; SDD-26 RF-26.6, DP-24.10): o rodapé é **substituído** por um bloco de explicação em JetBrains Mono **11**, `txt-secundario`, line-height **1,45**, com o texto de `explanation` da alternativa escolhida e o da correta (SDD-19 RF-19.10, aviso de analogias mantido). A caixa **cresce para cima** mantendo a base (`bottom = stripTop − 8`): altura = `242 − altura do rodapé + altura do bloco`, no máximo **3 linhas** de explicação (excedente cortado com `…`). O título/timer mostra o resultado em vez do timer: `ACERTOU` em `menta`, `ERROU` ou `TEMPO ESGOTADO` em `perigo-claro`, Silkscreen 11.
- **Botão minimizar** (requisito desta ficha; **não existe no design**, DP-24.10): ícone de 16 × 16 (um traço horizontal `M3 12h10`, traço 1,8, `txt-rotulo`; path **novo**, a incluir em SDD-20) no canto superior direito, à direita do timer (gap `esp-12`); área de clique **44 × 44** limitada ao interior do card (`x ∈ [W−46, W−2]`, `y ∈ [2, 46]`), sem alterar o layout do cabeçalho. Clique emite `MinimizeBossCard`; o card some, o banner rosa do chefe (SDD-22/26) **continua** e reabre o card (`OpenBossQuestion`). Minimizar **não pausa** o tempo (SDD-26 RF-26.5).
- Depois de respondido ou esgotado, o ícone vira `fechar` (SDD-20, `txt-rotulo`) e o card fecha ao clique ou **sozinho após 20 s** (deadline do host; valor proposto, DP-24.10). Fechar não altera o resultado.
- **Texto longo:** a altura de 242 do design comporta pergunta de até 2 linhas e alternativas de 1 linha (largura útil da alternativa ≈ 216 DIP). O validador do banco (SDD-26 RF-26.9) deve rejeitar o que não couber; sem validador, o texto excedente é cortado com `…` (DP-24.10).
- O mesmo card serve à pergunta do **COLETAR ×2** de SDD-25 (DP-24.11).

### RF-24.10: Card "Bem-vindo de volta"

Caixa **472 × 612** (o quadro do design tem 520 × 660 com padding 24 e fundo `bg-abismo`, que **não** é desenhado: a janela é transparente ao redor; SDD-20 RF-20.1). Fundo `bg-painel`; borda **2 DIP** `ambar`; sombra `sombra-card`; padding `esp-22`; coluna com gap **16**. Posição: RF-24.2.

| # | Bloco | Conteúdo |
|---|---|---|
| 1 | Cabeçalho (gap `esp-6`) | rótulo JetBrains Mono **10**, letter-spacing **1**, `txt-rotulo`: `RELATÓRIO DO MODO GERENCIADO`; título Silkscreen **24**, peso 400, `ambar`: `BEM-VINDO DE VOLTA`; texto JetBrains Mono **12**, `txt-secundario`: `Você esteve fora por ` + tempo em `txt-principal` (`3h 12min`) + `. A auto-escala segurou o núcleo.` |
| 2 | Tempo acumulado (gap `esp-6`) | linha `space-between` JetBrains Mono **10**, `txt-rotulo`: `TEMPO ACUMULADO` e `3h 12min / 12h máx.`; barra de altura **8**, trilho `bg-trilho`, preenchimento `azul`, largura = `tempo / 12 h` (design 27 %) |
| 3 | Grade 2 × 2, gap `esp-8` | quatro tiles (abaixo) |
| 4 | Aviso de perdas | padding `esp-12`, gap `esp-12`, centralizado; fundo `perigo-fundo`, borda **1 DIP** `perigo-borda`; ícone `alerta` **20 × 20** (traço 1,6, `perigo-claro`); texto JetBrains Mono **11**, `perigo-texto`, line-height **1,45**: `Perdido por estar ausente: 1 chefe (pergunta Azure), 4 picos de demanda e 3 caixas. Esses só aparecem com você na tela.` |
| 5 | Botões (`margin-top: auto`, gap `esp-8`) | **COLETAR ×2:** altura **52**, sem borda, fundo `ambar`, texto `bg-trilho-fundo`, Silkscreen **13**, letter-spacing **1**, coluna centralizada gap **2**: `COLETAR ×2` e, abaixo, JetBrains Mono **10**, letter-spacing **0**: `responda 1 pergunta Azure da fase`. **COLETAR +X:** altura **44**, borda **1 DIP** `borda-forte`, fundo transparente, texto `txt-botao-neutro`, Silkscreen **11**, letter-spacing **1**: `COLETAR +186K` |

**Tiles** (padding `esp-12`, gap `esp-4`, coluna, fundo `bg-item`, borda **1 DIP** `borda-painel`; rótulo 10 `txt-rotulo`; número Silkscreen **20**; sublinha 10):

| Tile | Rótulo | Número | Sublinha |
|---|---|---|---|
| 1 | `CRÉDITOS` | `+186K` em `ambar` | `a ×0,25` em `txt-rotulo` |
| 2 | `ONDAS` | `22 → 37` em `txt-principal` | `sem perder vida` em `txt-rotulo` |
| 3 | `XP` | `+4,1K` em `txt-principal` | `LV 22 → 23` em `menta` |
| 4 | `AUTO-ESCALA` | `6 melhorias` em `txt-principal` | `Raio 12→14 · Gelo 8→9` em `txt-rotulo` |

- Todos os campos vêm do `OfflineReport` (SDD-25 RF-25.8). Se houver quedas (`Quedas > 0`), a sublinha do tile 2 mostra o número de quedas no lugar de `sem perder vida` (SDD-25 RF-25.9).
- **Botões:** COLETAR ×2 abre a pergunta Azure (RF-24.9, SDD-25 RF-25.9: acerto dobra, erro ou tempo esgotado = ×1); COLETAR +X coleta ×1 na hora. O `X` é o pendente de créditos formatado (SDD-27 RF-27.2). Comandos: `CollectOffline(multiplicador)` (nome proposto, contrato de SDD-25).
- **Recolhimento:** com o card sem ação por **60 s** (deadline do host), ele fecha e vira o **badge `+X` no widget**; clicar no badge reabre o card (SDD-25 RF-25.9). Esta ficha entrega os eventos `OfflineCardCollapsed` e `OfflineCardReopened` e o texto `+X`; o **desenho do badge no widget não existe em SDD-23 nem no design** (DP-24.12, conflito registrado). "Sem ação" = nenhum pressionamento de botão do mouse no card; hover e movimento não contam; o contador reinicia a cada clique e **pausa** enquanto o card de pergunta do COLETAR ×2 está aberto.
- Frases condicionais não definidas no design: `A auto-escala segurou o núcleo.` só com auto-escala ligada e `NMelhorias > 0`; o aviso de perdas é omitido se tudo for 0; lista de melhorias do tile 4 mostra quantas entradas couberem em uma linha (≈ 30 caracteres a 10 DIP) e termina com `· +N` (DP-24.12).
- O card **não ativa** a janela e **não pausa** a simulação (SDD-25 RF-25.9). Abrir, recolher e clicar no card contam como interação (SDD-25 RF-25.1).

### RF-24.11: Hit-test, cursores e interação

- **Alvo de clique:** a tabela de retângulos de cada janela é calculada pelo `PanelLayout`/`CardLayout` (funções puras, sem alocação por clique); `WM_LBUTTONDOWN` registra o alvo e `WM_LBUTTONUP` no **mesmo** alvo dispara o comando (padrão de SDD-23 RF-23.8). Alvos do corpo do painel são **recortados** pelo viewport (RF-24.3): um botão parcialmente rolado para fora só responde na parte visível.
- **Alvos (enum `PanelHit`/`CardHit`):** `Close`, `ScrollThumb`, `ScrollTrack`, `AutoScaleToggle`, `UpgradeTower(id)`, `BuildTower(type)`, `MissionCollect(id)`, `RegionRow(i)`, `Migrate`, `QuestionOption(i)`, `QuestionMinimize`, `QuestionClose`, `OfflineCollectX2`, `OfflineCollect`, `Background`.
- **Fundo e sombra:** qualquer ponto da janela com alfa ≥ 1 (inclui a **sombra**, alfa de `sombra-painel` ≈ 89) **consome** o clique e não o repassa ao app de baixo; fora de alvo é `Background` (sem ação). `HTTRANSPARENT` não resolve entre processos (DP-24.13). A margem de 6 DIP da sombra, portanto, é uma faixa morta.
- **Alvo mínimo 44 × 44** (SDD-20 RF-20.4): fechar 44×44, toggle 44, MELHORAR 84×44, COLETAR 72×44, linha de fase e opção de resposta `min-height 44`, MIGRAR 48, COLETAR ×2 52, COLETAR +X 44, minimizar 44×44.
- **Cursores** (`WM_SETCURSOR`, retorna TRUE): mão (`IDC_HAND`) sobre botões, toggle, opções habilitadas, região selecionável e polegar/trilho; seta nos demais; seta sobre botões bloqueados e opções desabilitadas.
- **Interação para presença** (SDD-25 RF-25.1): todo `WM_LBUTTONDOWN` e `WM_RBUTTONDOWN` em **qualquer ponto** das três janelas com alfa ≥ 1 chama `IPresenceSink.NotifyInteraction()` **uma vez por pressionamento** (nome proposto em SDD-23). Hover e movimento **não** contam. A roda do mouse **não** conta (DP-24.1).
- **Captura:** `SetCapture` só durante clique em botão (para completar o clique) e durante o arraste do polegar; `ReleaseCapture` em `WM_LBUTTONUP`/`WM_CAPTURECHANGED`.
- Botão direito: sem menu (conta como interação).
- Sem teclado e sem foco; nenhuma janela chama `SetFocus`.

### RF-24.12: Hover e estados de botão

O design só define hover nos botões com a classe `ad-act`: `filter: brightness(1.08)`. Esta ficha o transcreve com os tokens pré-calculados de SDD-20 (`ambar-hover`, `prestigio-hover`):

| Botão | Normal | Hover |
|---|---|---|
| MELHORAR, CONSTRUIR, COLETAR (missão) | `ambar` | `ambar-hover` |
| MIGRAR | `prestigio` | `prestigio-hover` |
| Botão bloqueado (`FASE 09`, `SEM SLOT`) | `bg-trilho` | **sem hover** (no design a classe `ad-act` também o afeta; esta ficha o desliga, DP-24.14) |

- **Sem hover no design:** fechar, toggle, opções de resposta, COLETAR ×2 e COLETAR +X do card de retorno, minimizar. **Não inventar:** ficam sem hover (DP-24.14 propõe `ambar-hover` em COLETAR ×2).
- **Pressionado, desabilitado, foco:** inexistentes no design (SDD-20 seção final). Pressionado usa o estado de hover (padrão de DP-23.7).
- `WM_MOUSEMOVE` atualiza o alvo sob o cursor; se mudou, a versão do modelo sobe (RF-24.13); `TrackMouseEvent(TME_LEAVE)` limpa o hover em `WM_MOUSELEAVE`. **Sem polling de cursor.** Hover só é recalculado se o alvo mudou (e quando o conteúdo rola sob o cursor, uma vez por evento de rolagem).

### RF-24.13: Render apenas em mudança

- Cada janela lê um modelo imutável com `Version` (padrão SDD-15 RF-15.2, como `WidgetModel` de SDD-23): aba, offset de rolagem, alvo em hover, estado armado do MIGRAR, textos já formatados (a versão sobe **só quando um texto ou estado exibido muda**, comparação do texto formatado), estado do card, segundos do timer.
- Sem mudança de versão: **0 frames, 0 `UpdateLayeredWindow`, 0 wakes**, para as três janelas (SDD-08 CA-08.4). **Sem animações** no painel e nos cards (o design não tem); abrir, trocar de aba e fechar são instantâneos. Nenhum timer enquanto estático.
- **Deadlines do host** (SDD-10): coalescer atualizações de dados exibidos (créditos que afetam custo, barras de progresso, tempo presente, onda atual) em **≤ 2 redesenhos por segundo** e só se o texto/largura exibido mudou; timer do card de pergunta: 1 redesenho por segundo; MIGRAR armado: 1 redesenho ao expirar; auto-fechar de 20 s e recolhimento de 60 s: 1 evento cada.
- Cada redesenho é **frame completo da janela** (independe de `RenderOptions.PartialUpdates`, SDD-08 RF-08.7); cromo (sombra, borda, cabeçalho de fundo) em `SKImage` de cache por escala; linhas fora do viewport **não** são desenhadas.
- **Ordem de desenho do painel:** sombra, borda, fundo, conteúdo do corpo com clip (cards, linhas, barras, textos, ícones do atlas), barra de rolagem, cabeçalho (título, fechar) por último sobre o filete.
- Zero alocação por frame (SDD-08 RF-08.8): textos estáticos em `SKTextBlob`, números por `Span<char>`, `SKPaint`/`SKFont` pré-criados, sprites do atlas pré-escalado (`Nearest`).
- Saver e `reducedMotion`: sem efeito (nada anima).

### RF-24.14: DPI, monitor e ciclo de vida

- Per-Monitor V2: em `WM_DPICHANGED`, ignorar o retângulo sugerido, recalcular geometria e layout (RF-24.2/24.3), recriar superfícies e caches (SDD-20 ícones, SDD-09 atlas) e marcar frame completo.
- A superfície (DIB) de cada janela é criada ao mostrar e liberada ao ocultar; estado lógico (aba, offset, MIGRAR desarmado, card recolhido) fica no modelo, não na janela.
- Fullscreen (SDD-06), ocultar (SDD-07), sessão bloqueada, sono: as janelas ocultam junto da faixa. Ao reexibir, o painel volta à última aba aberta, com offset 0 (DP-24.17); o card de retorno segue SDD-25.
- Mudança de monitor/barra (SDD-21): reposiciona (RF-24.2) sem fechar.

### RF-24.15: Substituições e conflitos registrados

Ver seção 4 e o REPORT desta ficha. Em resumo: SDD-23 não define o badge `+X` do card de retorno (DP-24.12); SDD-27 define recompensa em renda e o design mostra créditos fixos (DP-24.7); SDD-27 exige escolha de região e o design não tem seletor (DP-24.9); `esp-3` (gap de 3 do design) não está no mapeamento semântico de SDD-20 RF-20.4.

## 6. Requisitos não funcionais

Todas as metas são **não comprovadas** até haver medição em `specs/evidencias/` (SDD-16).

- **RNF-24.1:** Painel aberto e estático: **0 Presents** em 30 s (e card de pergunta/card de retorno idem). Com dados mudando (créditos, progresso), **≤ 2 Presents/s** por janela; timer do card de pergunta: 1/s.
- **RNF-24.2:** Composição do painel p95 ≤ 1 ms e `UpdateLayeredWindowIndirect` p95 ≤ 1 ms a 150 % (corpo de 406×554 DIP = 609×831 px).
- **RNF-24.3:** Memória das superfícies (DIB BGRA), enquanto visíveis: painel ≤ 2,1 MB a 150 % e ≤ 3,7 MB a 200 %; card de retorno ≤ 2,8 MB a 150 % e ≤ 4,8 MB a 200 %; card de pergunta ≤ 1,2 MB a 150 % e ≤ 2,1 MB a 200 %. Ocultas: 0 MB.
- **RNF-24.4:** Zero alocação gerenciada por frame (SDD-08 RF-08.8).
- **RNF-24.5:** Zero `WM_SETFOCUS` e zero mudanças de foreground causados pelo processo em 100 cliques no painel e nos cards (herda SDD-03 RNF-03.3).
- **RNF-24.6:** Latência clique → estado visível (aba aberta, toggle, opção marcada) ≤ 50 ms (SDD-16 P-15).
- **RNF-24.7:** `PanelLayout` para os 4 conteúdos em ≤ 0,2 ms por recálculo (sem alocação em regime).
- **RNF-24.8:** Nenhuma consulta de cursor, de UIA ou timer periódico com painel e cards estáticos.

## 7. Estratégia de implementação

1. `PanelLayout` e `CardLayout` (Runtime, puros): retângulos de cada elemento a partir de largura/altura em DIP, escala, dados já formatados e larguras de texto medidas; altura do conteúdo por aba; retângulos de hit-test; testados em Linux por tabela (valores de RF-24.3 a RF-24.10).
2. `PanelModel`, `QuestionCardModel`, `OfflineCardModel` (Runtime): imutáveis com `Version`; construtores a partir de SDD-25/26/27.
3. `PanelRenderer`, `QuestionCardRenderer`, `OfflineCardRenderer` (Rendering): cromo em cache, linhas, barras, ícones (SDD-20 `IconCatalog`, atlas SDD-09), texto (SDD-20 `Typography`), barra de rolagem.
4. `PanelWindow`, `QuestionCardWindow`, `OfflineCardWindow` (Platform.Windows): estilos, WndProc, hit-test, cursor, captura, rolagem, `NotifyInteraction`, comandos.
5. Integração com SDD-23 (`ToggleTab`, aba ativa, badge), SDD-22 (`OpenArsenal`, banner reabre o card), SDD-25 (pendências, 60 s), SDD-26 (pergunta, tempo, ledger) e com o z-order compartilhado.
6. Testes (seção 10); evidência em `specs/evidencias/`.

## 8. Dependências

SDD-20 (tokens, tipografia, ícones), SDD-23 (widget: `PanelCommand`, aba ativa, badge), SDD-08 (superfície), SDD-09 (sprites), SDD-03/04 (janela, não ativação, alfa), SDD-10 (`RenderPolicy`, deadlines), SDD-22 (faixa: `stripTop`, `OpenArsenal`, banners), SDD-21 (slots e `SEM SLOT`), SDD-27 (números e textos), SDD-25 e SDD-26 (comportamento dos cards). Consumidores: SDD-25 (card e badge) e SDD-26 (card de pergunta).

## 9. Critérios de aceitação

- **CA-24.1:** As três janelas têm `LAYERED|TOOLWINDOW|NOACTIVATE|TOPMOST`, **não** têm `TRANSPARENT` nem owner, e `WM_MOUSEACTIVATE` retorna `MA_NOACTIVATE` (T-24.1).
- **CA-24.2:** Com o Bloco de Notas em foco digitando, 100 cliques no painel (cabeçalho, linhas, botões, sombra) e nos cards não mudam `GetForegroundWindow()` e o texto continua no Bloco de Notas (T-24.2; MTC-24.2).
- **CA-24.3:** Geometria: painel 400×548 (janela 406×554), `left = WorkArea.Left + 16`, base = `stripTop − 8`; card de pergunta 520×242 e de retorno 472×612, centralizados e com base em `stripTop − 8`; sombra (+6,+6) DIP; em 100/125/150/175/200 % (T-24.3 por tabela; MTC-24.1).
- **CA-24.4:** Cabeçalho do painel: título Silkscreen 14, letter-spacing 1, `ambar`; fechar 44×44; filete 1 DIP `borda-painel`; viewport 396×491 (T-24.4).
- **CA-24.5:** Arsenal: card de auto-escala; linhas de torre (52×52 de slot, MELHORAR 84×44, opacidade 0,45 na bloqueada); linha `SEM SLOT`; linha de construir em slot vazio; valores de RF-24.5 (T-24.4, golden sem texto + bounding boxes).
- **CA-24.6:** Campanha: card da fase (barra de 8 DIP, `Chefe na onda X · tema Y`) e lista com estrelas e `BLOQ.` a 0,55 (T-24.4).
- **CA-24.7:** Missões: card de presença com 3 faixas e a atual destacada em `menta`; linhas com barra de 5 DIP; COLETAR 72×44 só em missão concluída não coletada (T-24.4, T-24.5).
- **CA-24.8:** Migrar: card roxo, 2 tiles, regiões, botão MIGRAR de 48 DIP; primeiro clique arma por 5 s e **não** emite `Migrate`; segundo clique em ≤ 5 s emite `Migrate`; 5 s sem clique, troca de aba, fechar ou clique fora do botão desarma (T-24.6 com `FakeClock`).
- **CA-24.9:** Rolagem: `WM_MOUSEWHEEL` move o offset em passos de 40 DIP com limites; arrastar o polegar e clicar no trilho movem o offset; botão fora do viewport não é clicável (T-24.7).
- **CA-24.10:** Card de pergunta: os quatro estados (normal, correto, errado, tempo esgotado), explicação após responder, minimizar com 44×44 e reabertura pelo banner; tempo não pausa ao minimizar (T-24.8; T-26.2 em SDD-26).
- **CA-24.11:** Card de retorno: campos do `OfflineReport`, COLETAR ×2 e COLETAR +X, recolhimento em 60 s com evento para o badge (T-24.9; T-25.8).
- **CA-24.12:** Cada pressionamento em qualquer ponto das três janelas chama `NotifyInteraction` exatamente uma vez; hover, movimento e roda não chamam (T-24.10).
- **CA-24.13:** Com as três janelas estáticas, o contador de Presents fica em 0 por 30 s; com dados mudando, ≤ 2/s por janela (T-24.11).
- **CA-24.14:** Hover: `ambar-hover` em MELHORAR/CONSTRUIR/COLETAR, `prestigio-hover` em MIGRAR, nenhum nos demais; sem hover no botão bloqueado (T-24.12).
- **CA-24.15:** Contraste dos pares usados conforme SDD-20 RF-20.8 (T-20.3); o par proibido `bg-trilho-fundo`/`menta-faixa` não ocorre (T-24.4).
- **CA-24.16:** Pixels da sombra (margem de 6 DIP) consomem o clique sem ação e sem repassar ao app de baixo (T-24.13, WindowsInteractive).

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-24.1 | Estilos e owner das três janelas (CA-24.1) | WindowsInteractive |
| T-24.2 | `TestFocusTarget` + 100 cliques por `SendInput` em painel e cards | WindowsInteractive |
| T-24.3 | `PanelGeometry`: tabela de posição e tamanho (RF-24.2), escalas 100–200 %, `stripTop` com ocultação automática, clamp em tela pequena | Linux |
| T-24.4 | `PanelLayout`/`CardLayout` e golden **sem texto** + bounding boxes de texto (CA-24.4 a CA-24.7, CA-24.15) | Linux + Windows |
| T-24.5 | Hit-test por aba: alvos, recorte pelo viewport, alvo mínimo 44 | Linux |
| T-24.6 | MIGRAR: armar, expirar 5 s, cancelar por aba/fechar/outro clique, confirmar | Linux (`FakeClock`) |
| T-24.7 | Rolagem: roda, polegar, trilho, limites, offset por aba | Linux |
| T-24.8 | Card de pergunta: estados, explicação, minimizar, tempo esgotado, fechar em 20 s | Linux |
| T-24.9 | Card de retorno: campos, quedas > 0, recolher em 60 s, pausa durante a pergunta | Linux |
| T-24.10 | `NotifyInteraction` por pressionamento; hover/roda não contam | Linux |
| T-24.11 | Contador de Presents: estático (0) e dados mudando (≤ 2/s) com `FakeClock` | Linux |
| T-24.12 | Hover: tabela de botões e cores | Linux |
| T-24.13 | Clique na sombra consumido (`WindowFromPoint`, `SendInput`) | WindowsInteractive |
| T-24.14 | Z-order: faixa, widget, painel, cards após clique em `Shell_TrayWnd` | WindowsInteractive |
| MTC-24.1 | Comparar painel e cards lado a lado com o artefato (100/150/200 %); medir alturas de linha com os TTF reais | Manual |
| MTC-24.2 | Digitar no VS Code e clicar no painel e nos cards | Manual |
| MTC-24.3 | Roda do mouse sobre o painel com a opção "rolar janelas inativas" ligada e **desligada**; barra arrastável nos dois casos | Manual |
| MTC-24.4 | Retorno de 3 h (card aparece sozinho sem tirar o foco), esperar 60 s, reabrir pelo badge | Manual |
| MTC-24.5 | Tela de 1366×768 a 125 % e 150 %: painel e cards cabem e ficam clicáveis | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Opção "rolar janelas inativas" desligada: o painel nunca recebe a roda | Média | Médio | Barra arrastável sempre funcional (RF-24.4); detectar a opção; sem hook global; MTC-24.3 |
| Alturas reais das linhas (métrica "normal" de Silkscreen e JetBrains Mono) divergirem do design | Alta | Baixo | `PanelLayout` mede com os TTF; MTC-24.1; golden sem texto |
| Texto longo (perguntas, explicações, lista de melhorias) estourar o card de pergunta de 242 DIP ou o tile do card de retorno | Média | Médio | Validador do banco (SDD-26 RF-26.9); corte com `…`; DP-24.10 e DP-24.12 |
| Card de retorno (612 DIP) e painel não caberem em monitor pequeno com escala alta | Média | Médio | Clamp ao topo da área de trabalho (RF-24.2); DP-24.12; MTC-24.5 |
| Faixa morta de 6 DIP (sombra) engolir cliques destinados ao app de baixo | Baixa | Baixo | Margem só à direita e abaixo; DP-24.13 |
| Glifos `★ ☆ → ×` ausentes na Silkscreen | Média | Médio | Fallback (SDD-20 RF-20.3 item 7); DP-20.2 |
| Superfícies grandes (card de retorno a 200 %) custarem memória e Present | Baixa | Médio | Criar ao mostrar, liberar ao ocultar; frame completo só em mudança; RNF-24.3 |
| Painel e card de pergunta/retorno sobrepostos (COLETAR ×2) causarem confusão visual | Média | Baixo | Ordem de z fixa; DP-24.11 |
| `HTTRANSPARENT` não repassar cliques da sombra a outro processo | Alta | Baixo | Aceito (DP-24.13) |
| Falta de feedback quando o jogador clica MELHORAR sem créditos | Média | Baixo | DP-24.3 |

### Decisões pendentes (lacunas do design ou entre fichas; **não decididas aqui**)

| ID | Questão | Proposta / observação | Bloqueia |
|---|---|---|---|
| DP-24.1 | Rolagem por roda depende da opção "rolar janelas inativas" do Windows; passo de rolagem não está no design; roda não conta como interação | Passo de 40 DIP por detente; detectar a opção (`SPI_GETMOUSEWHEELROUTING`, a confirmar); barra sempre visível se desligada. Alternativa não recomendada: Raw Input com `RIDEV_INPUTSINK` ou hook de baixo nível apenas com o cursor sobre o painel | RF-24.4 |
| DP-24.2 | A barra de rolagem não existe no design (barra nativa do navegador) | Trilho 8 DIP `bg-trilho`, polegar `txt-rotulo`, hover `txt-secundario`, polegar ≥ 44 DIP, área de clique 16 DIP | RF-24.4 |
| DP-24.3 | MELHORAR/CONSTRUIR **sem créditos suficientes**: o design só tem o botão `ambar` | Sem estado novo: botão `ambar` normal e o clique devolve `NotAvailable` sem efeito; alternativa: estilo do botão bloqueado (`bg-trilho`/`txt-rotulo`, sem opacidade) enquanto faltar crédito, com a versão do modelo sobrescrita a ≤ 2 Hz | RF-24.5 |
| DP-24.4 | Linha **SEM SLOT** não existe no design (SDD-21 RF-21.8 só manda o selo) | Layout da torre, opacidade 0,45, botão no estilo bloqueado com `SEM SLOT`; melhorar uma torre sem slot **não** é permitido | RF-24.5 |
| DP-24.5 | Linha de **construir** em slot vazio não existe no design; Scale Set sem estatística no design | Ao abrir por slot vazio, uma linha por torre desbloqueada com `CONSTRUIR` + custo; sem foco, nenhuma linha de construir; estatística do Scale Set por analogia (`Dano · Alc · Cad`) | RF-24.5, SDD-22 |
| DP-24.6 | Estado **desligado** do toggle e a marca de seleção da caixa (nativa no design) | OFF: caixa com borda `txt-rotulo` e texto `OFF`; marca em `bg-trilho-fundo` | RF-24.5 |
| DP-24.7 | Recompensa em renda: SDD-27 diz `10 min de renda`, o design mostra `+12K créditos`; missão coletada não está no design | Exibir `+10 min de renda`; missão coletada: barra cheia, sem botão e linha de progresso `COLETADA` em `menta` | RF-24.7, SDD-27 |
| DP-24.8 | Faixa de bônus destacada em Ausente/Offline (×0,25) | Nenhuma faixa destacada; título com o multiplicador atual em `txt-widget-rotulo` | RF-24.7 |
| DP-24.9 | **Escolha de região resolvida por ADR-031.** Escolha de região ao migrar, estado indisponível (`ondaMax` < 15) e estado "armado" do MIGRAR não estão no design | Regiões desbloqueadas clicáveis (`→ ` e `txt-principal` na escolhida); indisponível = botão `bg-trilho`/`txt-rotulo` `DISPONÍVEL NA ONDA 15`; armado = `perigo`/`perigo-tinta` `CONFIRMAR · REINICIA TORRES E ONDAS` | RF-24.8, SDD-27 |
| DP-24.10 | Card de pergunta: estado errado (fundo), tempo esgotado, explicação, minimizar (ícone novo `M3 12h10`), fechar após responder (20 s), texto longo | Fundo da errada `perigo-fundo`; explicação no lugar do rodapé com crescimento para cima (≤ 3 linhas); resultado no lugar do timer; auto-fechar em 20 s; incluir o ícone em SDD-20 | RF-24.9, SDD-26 |
| DP-24.11 | Pergunta do **COLETAR ×2** (SDD-25): cabeçalho, tempo e sobreposição ao card de retorno | Reusar o card de pergunta com cabeçalho `COLETAR ×2 · {TEMA}` e 45 s; ocultar o card de retorno enquanto a pergunta estiver aberta e restaurá-lo depois | SDD-25, SDD-26 |
| DP-24.12 | Card de retorno: badge `+X` no widget (**agora em SDD-23 RF-23.4, ADR-031**); telas pequenas; frases condicionais e lista truncada | Estender SDD-23 com o badge (visual a definir); clamp ao topo e, se altura > área útil, aviso em MTC; frases e `· +N` como em RF-24.10 | SDD-23, SDD-25 |
| DP-24.13 | Clique na sombra (alfa ≥ 1) consome o evento; `HTTRANSPARENT` não atravessa processos | Aceitar a faixa morta de 6 DIP (direita e embaixo) | RF-24.11 |
| DP-24.14 | Hover do botão bloqueado e dos botões dos cards (COLETAR ×2, COLETAR +X, opções) | Botão bloqueado sem hover; demais sem hover, exceto proposta `ambar-hover` em COLETAR ×2 | RF-24.12 |
| DP-24.15 | Formato dos números do design (ponto) × ADR-029 (vírgula); `LV 14` (painel) × `LV14` (faixa) | **Resolvida por ADR-029 e ADR-032:** vírgula decimal em todas as tabelas | SDD-27 |
| DP-24.16 | Rotina única de reafirmação de z-order de faixa, widget, painel e cards (DP-23.6) | Incluir painel e cards na ordem de RF-24.1 | SDD-22/23 |
| DP-24.17 | O painel mantém a aba aberta ao ocultar/reexibir? | Sim, última aba com offset 0 | RF-24.14 |
| DP-24.18 | **Resolvida (ADR-031: 52 + filete de 1 DIP).** Cabeçalho do design: `height 52` com `border-bottom 1px` em caixa de conteúdo = 53 DIP; o enunciado cita 52 | Usar 52 + filete 1 = 53; viewport do corpo 491 | RF-24.3 |

Itens do design **ausentes** (não inventados nesta ficha, apenas propostos acima como DP): estado desabilitado detalhado e pressionado dos botões, hover dos cards, foco por teclado (o jogo não usa teclado), animação de abertura, barra de rolagem, seletor de região, botão minimizar, bloco de explicação, estado errado/tempo esgotado da pergunta, linha SEM SLOT, linha de construir.
