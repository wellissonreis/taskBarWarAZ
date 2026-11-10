# SDD-22: Faixa de batalha (janela de cena sobre a barra de tarefas, mundo 1D e sprites do design)

**Status:** Rascunho para revisão · **Etapa:** E1 (janela) e E4 (cena) · **Depende de:** SDD-20, SDD-21, SDD-08, SDD-11 · **Bloqueia:** SDD-25, SDD-26

## 1. Identificador e título

SDD-22: Janela layered da cena (96 DIP de altura), mundo 1D, posições e sprites exatos do design, modo ausente, janelas hotspot (banners e caixas), política de fps e orçamento de desempenho.

## 2. Objetivo

Desenhar, **colada acima da barra de tarefas**, a faixa onde acontece a batalha do idle: o núcleo à esquerda, uma torre por slot (SDD-21), o portal ao lado da bandeja e os inimigos marchando em linha reta até o núcleo. A cena **não atrapalha** o uso do computador (click-through por padrão), **nunca rouba foco** e **não consome CPU quando nada muda**.

## 3. Contexto técnico

- **Decisões:** ADR-018 (design oficial; tokens de cor, tipografia e espaçamento em **SDD-20**), ADR-019 (gênero idle), ADR-020 (geometria: faixa de 96 DIP acima da barra, slots = botões da barra), ADR-021 (banners, caixas, painel e cards são janelas layered Skia NOACTIVATE), ADR-025 (fontes **Silkscreen** e **JetBrains Mono**). Respeita ADR-001 (layered + Skia raster), ADR-002 (T-Game dona das janelas), ADR-004, ADR-013 e ADR-015.
- **Fonte dos números:** o artefato "Azure Defense — Idle de barra", componente `Strip.dc.html` (1440×144 DIP: cena de 96 + barra de tarefas de 48), mais `Presence.dc.html` (estados). Todos os valores abaixo foram **transcritos do design**. Quando o design não define algo, a lacuna está marcada em "Decisões pendentes" (seção 11). Sprites e posições estão em **DIP a 100%**; na tela, multiplique por `scale = dpi / 96` (SDD-05).
- Janelas layered com alfa por pixel: pixels com alfa 0 não recebem mouse; `WS_EX_TRANSPARENT` ignora a janela inteira; `SourceConstantAlpha` aplica opacidade global sem redesenho (SDD-04 seção 3).
- Pixel art é desenhada com `SKFilterMode.Nearest` a partir de cache pré-escalado (SDD-08 RF-08.8, SDD-09 RF-09.5).

### Substituições e conflitos com fichas anteriores (o design vence)

| Ficha antiga | O que muda | Tratamento |
|---|---|---|
| SDD-05 **RF-05.4** (layout da faixa 1920×200 + HUD lateral, `DockLayout`, S/M/L) | Faixa de **96 DIP × largura da área de trabalho**; sem HUD lateral nem tamanhos S/M/L | **Substitui SDD-05 RF-05.4** (e, por consequência, RF-05.5 `ClientPx → World/HudDip` e RF-05.9 "Tamanho P/M/G"). RF-05.1, 05.2, 05.3, 05.6, 05.7 continuam valendo |
| SDD-12 **RF-12.2** (mapa `map.strip-a.json`: 1920×200, `guide` com curvas, `PathLength` 1900–2100) | Mundo **1D** em DIP, caminho reto portal→núcleo, comprimento dependente do monitor | **Substitui SDD-12 RF-12.2** |
| SDD-11 **RF-11.3** (`PathTable` Catmull-Rom) | Distância **1D** `|x_inimigo − x_torre|` | **Substitui SDD-11 RF-11.3** para este mapa. RF-11.5 (mira euclidiana, `PathDist`) e RF-11.9 (`PlacementValidator`) também ficam superados e devem ser revisados em SDD-11 |
| SDD-08 **RF-08.4** (desenho de torre com variantes costas/frente, inimigo com sombra, anel de tipo, barra de vida, bob em unidades, elevação do drone, luz da base, brilho do portal) | Um sprite por torre e por inimigo, alinhados à base; só o **chefe** tem barra de vida; bob de 2 DIP | **Substitui SDD-08 RF-08.4** |
| SDD-08 **RF-08.5** (efeitos `fx-impact`, `fx-frost`, `fx-explosion`, anéis, pool de 128) | Só **números de dano flutuantes** (pool de 24) | **Substitui SDD-08 RF-08.5** (ver DP-22.8 sobre projéteis) |
| SDD-08 RF-08.2/08.3 (camadas e ordem de composição com HUD e estrada) | Cena sem estrada, sem HUD (o widget é SDD-23) | **Substitui SDD-08 RF-08.3** (nova ordem em RF-22.4); RF-08.1, 08.6 (texto, com as fontes de ADR-025), 08.7 e 08.8 continuam valendo |
| SDD-09 **RF-09.1** (importação dos 24 PNG de `sideWarAZ/public/assets/azure/atlas/`) e **RF-09.2** (ids `tower.bolt.back/front`, `enemy.grunt.a`…) | Os sprites oficiais vêm do design (13 blobs) | **Substitui SDD-09 RF-09.1**; RF-09.2 deve ser reescrito com os ids de RF-22.9 |
| SDD-04 **RF-04.1** (opacidade passiva padrão 180 ≈ 70%), **RF-04.4** (fundo com alfa 150, HUD com alfa ≥ 200), **RF-04.6** (`HitTarget` de HUD/paleta/campo) | Cena sem fundo, opacidade 100% presente e 50% ausente; alvos de clique são torre e slot vazio | **Substitui SDD-04 RF-04.4 e RF-04.6** para a cena; **RF-04.1** só na linha de opacidade. RF-04.2, 04.3 e 04.7 continuam |
| SDD-03 **RF-03.3** e ADR-011 (HandleWindow/alça) e SDD-04 (ícone da alça) | O design **não tem alça**. A troca de modo é por atalho (Ctrl+Alt+G) e pelo widget (SDD-23) | Ver DP-22.4. **Revisar SDD-03 RF-03.3, SDD-04 RF-04.1/04.2 (clique na alça) e ADR-011** |
| SDD-07 **RF-07.4** (inatividade pausa a onda, selo "PAUSADO: AUSENTE", render 0 fps) | Ausente = **modo gerenciado** (continua, renda ×0.25, selo "MODO GERENCIADO · AUTO-ESCALA ATIVA", 8 fps) | **Substitui SDD-07 RF-07.4** na parte visual (o comportamento de jogo é de SDD-25/26). RF-07.2 (`PauseReason.Idle`) deve ser revisada |
| SDD-10 **RF-10.7** (`RenderPolicy`) | Fps por presença (30 presente, 8 ausente) | Estende RF-10.7 (a revisar); a regra "`!visible` → 0 fps" continua |
| SDD-15 (atalhos, arraste de torre da paleta) | Atalho Ctrl+Alt+G para o modo Interativo; clique em torre/slot abre o Arsenal | Revisar a tabela de atalhos de SDD-15 |

## 4. Escopo e exclusões

**Escopo:** geometria e estilos da janela de cena, modos Passivo e Interativo na cena, mundo 1D e coordenadas, posições e tamanhos exatos de todos os elementos da cena, números flutuantes, linha de borda de estado, modo ausente (opacidade, selo), janelas hotspot (geometria, ciclo de vida e hit-test dos banners de pico/chefe e das caixas de suprimentos), política de fps na cena, mapeamento de sprites e exportação dos blobs do design, orçamento de desempenho e golden images.

**Exclusões:**

- **comportamento** de picos, chefes, caixas, perguntas Azure e missões (SDD-26): aqui só geometria, ciclo de vida das janelas e hit-test;
- widget HUD de 420×48 na barra, menu do jogo e contadores (SDD-23);
- painel, cards, banners "Bem-vindo de volta" e caixas de diálogo (janelas layered de ADR-021; outras fichas);
- regras de combate, dano, balanceamento e economia (SDD-11, SDD-12, SDD-25);
- leitura da barra de tarefas e dos slots (SDD-21): aqui só se **consome** `TaskbarLayout` e `SlotMap`;
- tokens de cor e tipografia (SDD-20). Os valores de cor desta ficha são os do design e devem coincidir com os tokens; em caso de divergência, vale SDD-20 e abre-se um registro.

## 5. Requisitos funcionais

### RF-22.1: Janela de cena (`SceneWindow`, Platform, T-Game)

Classe de janela `SideWar.Scene`. Substitui, para a cena, a `OverlayWindow` de SDD-03 RF-03.2 (mesmos estilos e atributos DWM).

- `dwExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`, `dwStyle = WS_POPUP`; no modo Passivo acrescenta `WS_EX_TRANSPARENT` (RF-22.3).
- Não ativação conforme SDD-03 RF-03.5 (`WM_MOUSEACTIVATE → MA_NOACTIVATE`; proibido `SetForegroundWindow` etc.). Reafirmação de topmost conforme SDD-03 RF-03.6.
- **Geometria** (`SceneLayout.Compute(MonitorInfo m, TaskbarLayout t) → PixelRect`, função pura em Runtime):

```
scale   = m.Dpi / 96
widthPx = m.WorkArea.Width                       // largura da área de trabalho do monitor da barra
heightPx= round(96 * scale)                      // 96 DIP
x       = m.WorkArea.Left
y       = m.WorkArea.Bottom - heightPx           // barra embaixo: WorkArea.Bottom = topo da barra, a faixa fica colada acima dela
                                                 // barra em outra borda (ou fallback): borda inferior da área de trabalho
y      -= 2   se t.AutoHide                      // margem mínima de 2 px físicos (SDD-05 RF-05.7), para não bloquear a ativação da barra
```

- Com a barra embaixo, `WorkArea.Bottom` já exclui a barra, então a fórmula é a mesma nos dois casos. Os dois casos do enunciado ficam como duas linhas de teste (T-22.1).
- **Nenhum pixel fora da `WorkArea`** (SDD-05 RNF-05.3).
- **Resolução da superfície:** `widthPx × heightPx` pixels (BGRA, pré-multiplicado). **Sem alfa de fundo:** pixels sem entidade têm alfa 0 (a cena não tem fundo nem estrada).
- **Reação a mudanças:** mesma tabela de SDD-05 RF-05.6 (`WM_DPICHANGED`, `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`) + `TaskbarLayoutChanged` (SDD-21). Debounce e `layoutDirty` quando oculto, como em SDD-05. A recomputação marca frame completo (SDD-08 RF-08.7), refaz o bake dos sprites (SDD-09 RF-09.5) e a camada estática.
- **Ocultação:** SDD-06 e SDD-07 inalterados (`Hide` oculta a cena e **todas as janelas hotspot**, ver RF-22.8).

### RF-22.2: Mundo 1D e coordenadas

- **Unidade:** DIP. **X** cresce para a direita, de 0 (borda esquerda da faixa) a `W` (largura da faixa em DIP = `widthPx / scale`). **Y** cresce para baixo, de 0 (topo da faixa) a 96. A **linha de base** é `y = 96`: núcleo, torres, slots, inimigos e portal têm o **pé** (borda inferior do sprite) em `y = 96`.
- **Caminho:** reta horizontal do **portal** ao **núcleo**. Os inimigos **marcham para a esquerda**. Não há curvas, estrada ou faixas (lanes).
  - `spawnX` (centro X do inimigo ao nascer) = `portal.Left + portal.Width/2`, com `portal.Left = TrayLeftX + 16` (RF-22.5). O spawn no **centro do portal** (no design a borda esquerda nasce em `TrayLeftX + 10`) é **aceito** (ADR-032): o portal cobre o nascimento;
  - `leakX` (ponto em que o inimigo atinge o núcleo) = borda direita do núcleo = `10 + 84 = 94` (DP-22.9);
  - `PathLength = spawnX − leakX` (em DIP), recalculado quando `TrayLeftX` ou `W` mudam.
- **Posição de um inimigo** = X do **centro** do sprite. O sprite é desenhado com `left = x − largura/2`, `top = 96 − altura`.
- **Distância (substitui `PathTable`):** entre inimigo e torre é `|x_inimigo − x_torre|`, onde `x_torre` = `CenterXDip` do slot (SDD-21 RF-21.7). O alcance da torre é um intervalo `[x_torre − alcance, x_torre + alcance]` em DIP. Mira, ordem e dano continuam em SDD-11 (a revisar), agora com distância escalar.
- **Velocidade:** a velocidade de cada inimigo é fixa em **DIP/s**, conforme SDD-27 RF-27.6 (que prevalece); não há `traversalSeconds`.
- **Determinismo (ADR-026, resolve DP-22.1):** `W`, `PathLength`, as posições X dos slots, do núcleo e do portal fazem parte do **estado da simulação**. A cena **nunca** os escreve direto: ao receber `TaskbarLayoutChanged` (SDD-21) ou mudança de monitor/DPI, a T-Game enfileira o comando `LayoutChanged` (comprimento, `SlotMap`, núcleo, portal), aplicado **entre ticks**, gravado no save e no `StateHash` (SDD-11, SDD-14). O render lê o layout do estado da simulação. Inimigos com `X` além do novo spawn são limitados a ele (ADR-031 item 3). **Widget em fallback (ADR-032):** o `LayoutChanged` posiciona o **núcleo em `x = 428`** (em vez de 10) para não ficar sob o widget de 420 DIP (SDD-23 RF-23.2); `leakX` acompanha a borda direita do núcleo.
- **Conversões:** `ClientPx → Dip` = `px / scale`; `Dip → ClientPx` = `round(dip * scale)`. Uma única escala, arredondada uma vez por layout (SDD-05 risco de DPI fracionário).

### RF-22.3: Modos de interação da cena

- **Passivo (padrão ao iniciar e ao reaparecer; SDD-04 RF-04.1):** `WS_EX_TRANSPARENT` ligado. Todo clique atravessa a faixa para o aplicativo de baixo.
- **Interativo** (atalho global **Ctrl+Alt+G** alterna; menu e widget SDD-23 também): `WS_EX_TRANSPARENT` desligado. Como a cena não tem fundo (alfa 0), só os pixels de **torre** e **slot vazio** recebem cliques; o resto atravessa (SDD-04 RF-04.4 com `backgroundAlpha = 0`).
  - Clique na **torre** ou no **slot vazio** abre o painel **Arsenal focado** naquele `slotIndex` (comando `OpenArsenal(slotIndex)`; a abertura é ação explícita do usuário, compatível com ADR-015; a janela do Arsenal é de ADR-021 e não é definida aqui).
  - `HitTarget` desta ficha: `SceneTower(slotIndex) | SceneEmptySlot(slotIndex) | None`. Retângulo de teste: `[cellLeft, cellLeft + 44] × [top do sprite/rótulo, 96]`, com `cellLeft = CenterXDip − 22`. Torre: top 38 (rótulo) até 96; slot vazio: top 54 (o "+") até 96.
  - Cursor: mão sobre torre/slot (`WM_SETCURSOR`), seta nos demais.
  - Retorno automático a Passivo, cancelamento, `TrackMouseEvent` e `autoPassiveSeconds` (8 s): **SDD-04 RF-04.2 e RF-04.3, inalterados**.
- **Peek** (SDD-04 RF-04.5): ver DP-22.3.
- **Opacidade global** (`SourceConstantAlpha`): **presente = 255 (1,0)**, **ausente = 128 (0,5)** (RF-22.7). Isso substitui o padrão de 180 de SDD-04 RF-04.1.

### RF-22.4: Ordem de desenho (de baixo para cima) e camadas

Tudo na janela de cena, **na superfície única**, na seguinte ordem (o design usa esta ordem de empilhamento):

1. Linha de borda de estado (RF-22.6), 2 DIP na base. Fica **sob** os sprites.
2. Barra de HP do núcleo (borda, trilho, preenchimento), depois o sprite do **núcleo**.
3. Para cada slot: torre + rótulo de nível, ou slot vazio + "+".
4. Inimigos comuns (cada um com bob, RF-22.5), na ordem de nascimento (o mais novo por cima).
5. Números de dano flutuantes (RF-22.5).
6. **Portal** (por cima dos inimigos comuns; por isso é desenhado na camada **dinâmica**, depois dos inimigos).
7. **Chefe** (sprite) e, acima dele, sua barra de HP (por cima do portal).

O selo de modo ausente, os banners e as caixas **não** são desenhados na superfície da cena (RF-22.7 e RF-22.8).

- **Camada estática** (`SKImage` raster do tamanho da superfície, refeita quando layout, escala, torres, níveis, slots, HP do núcleo ou estado de borda mudam): linha de borda, núcleo e sua barra, torres, rótulos, slots vazios.
- **Camada dinâmica** (direto na superfície, a cada frame com mudança), nesta ordem: inimigos, números flutuantes, **portal**, chefe e sua barra (o portal precisa ficar acima dos inimigos, o que a camada estática não permitiria).
- Regras de custo: SDD-08 RF-08.8 (zero alocação por frame, `Nearest` para sprites, sem filtros de imagem, `SKPaint`/`SKFont` pré-criados).

### RF-22.5: Posições e tamanhos exatos (transcritos de `Strip.dc.html`)

Coordenadas em DIP, relativas ao canto superior esquerdo da faixa de 96. "Base" = alinhado a `y = 96`.

**Núcleo**

| Elemento | X | Y (top) | Tamanho | Observação |
|---|---|---|---|---|
| Sprite do núcleo | 10 | 40 | 84 × 56 | base = 96 |
| Barra de HP do núcleo | 22 | 32 | 60 × 5 (conteúdo) | borda 1 px `#071323` (tamanho externo 62 × 7), trilho `#16304d`, preenchimento `#5cf0b0`; largura do preenchimento = HP% × 60 |

**Slots e torres** (`cellLeft = CenterXDip − 22`, célula de 44 de largura)

| Elemento | X | Y (top) | Tamanho | Observação |
|---|---|---|---|---|
| Torre Raio | `cellLeft + round((44 − 44)/2)` = `cellLeft` | 52 | 44 × 44 | base = 96 |
| Torre Canhão | `cellLeft + round((44 − 45)/2)` | 52 | 45 × 44 | `round` com meio para cima (como o `Math.round` do design); resulta em `cellLeft + 0` |
| Torre Gelo | `cellLeft + round((44 − 39)/2)` = `cellLeft + 3` | 52 | 39 × 44 | |
| Rótulo de nível ("LV14") | `cellLeft` | 38 | caixa de 44 de largura, texto centralizado | Silkscreen **9**, `#ffffff`, sombra `1px 1px 0 #000` |
| Slot vazio (sprite) | `cellLeft + 1` | 70 | 42 × 26 | opacidade **0,85**; base = 96 |
| "+" do slot vazio | `cellLeft` | 54 | caixa de 44, centralizado | Silkscreen **10**, `#ffffff`, sombra `1px 1px 0 #000` |

- A torre é **centrada no centro X do slot** (SDD-21), ou seja, `CenterXDip`, usando as larguras por tipo acima.
- O nível aparece como `"LV" + n` (sem espaço), por exemplo `LV14`.

**Portal**

| Elemento | X | Y (top) | Tamanho |
|---|---|---|---|
| Sprite do portal | `TrayLeftX + 16` (fallback: `W − 184`, pois `TrayLeftX = W − 200`) | 44 | 68 × 52 (base = 96) |

**Inimigos** (todos com a base em `y = 96`; sem sombra, sem anel e **sem barra de vida**, exceto o chefe)

| Tipo (SDD-12) | Sprite do design | Largura × altura | Top |
|---|---|---|---|
| `grunt` | scout | 23 × 26 | 70 |
| `runner` | drone | 38 × 22 | 74 |
| `swarm` | crawler | 36 × 22 | 74 |
| `armored` | shield | 32 × 30 | 66 |
| `tank` | brute | 43 × 34 | 62 |
| `boss` | chefe | 68 × 52 | 44 |

- **Chefe:** barra de HP **60 × 5** (conteúdo), borda 1 px `#071323`, trilho `#3a0d1d`, preenchimento `#ff5a7a`, posicionada **acima** do sprite: `left = boss.Left + 4`, `top = 36` (o sprite está em `top = 44`, portanto `barTop = spriteTop − 8`). Largura do preenchimento = HP% × 60 (o design mostra 72%).
- **Marcha:** para a esquerda; sem espelhamento de sprite.
- **Bob:** deslocamento vertical do sprite de **0 → −2 → 0 DIP** em **0,5 s** por ciclo, `steps(2)` por trecho; na prática, as posições são `0, −1, −2, −1` DIP, trocando a cada 125 ms. Em pixels: `round(offset × scale)`. O bob é **global** (mesma fase para todos, como no design: sem atraso por inimigo). Desligado com `reducedMotion`.

**Números de dano flutuantes**

- Fonte Silkscreen **10**, sombra `1px 1px 0 #000`. Texto: o dano inteiro com sinal, como no design (`-96`, `-240`).
- Cores: **Raio `#9fd3ff`**, **Canhão `#f5b335`**, **Gelo**: texto **`LENTO`** na cor **`#7fe7ff`**, **somente na primeira lentidão** do inimigo (SDD-11).
- Animação: sobe **16 DIP** em **1,4 s**, `ease-out` (`cubic-bezier(0, 0, 0.58, 1)`), com fade de opacidade 1 → 0 (a mesma curva). Termina e libera o slot do pool.
- **Pool de 24**; com o pool cheio, o mais antigo é substituído (como SDD-08 RF-08.5).
- Origem: o design mostra `top = 32` (e `34`) como início da subida. Regra: X = centro do inimigo atingido, `top` **fixo em 32** (sem variação por `Id`; DP-22.10, resolvida por ADR-032).
- `reducedMotion`: o número aparece **parado** e some depois de 1,4 s (sem subida).

### RF-22.6: Linha de borda de estado

- Altura **2 DIP**, largura total da faixa, na **base** (linhas `y ∈ [94, 96)` da faixa), desenhada **sob** os sprites (RF-22.4, item 1).
- Cor: **presente `#f5b335`**, **ausente `#3a4558`**, **chefe `#ff5a7a`**. Em **pico**, vale a de presente.
- No design a linha é a borda superior (`border-top: 2px`) da barra de tarefas, ou seja, **logo abaixo** da faixa de 96 (`y ∈ [96, 98)` da composição de 144). O jogo não pode desenhar sobre a barra do Windows; por isso a linha fica nos 2 últimos DIP da faixa. Ver DP-22.2.

### RF-22.7: Modo ausente

- Entrada e saída vêm do estado de presença do jogo (`IdlePolicy`, SDD-07 RF-07.4: 5 min sem input; limiar **não editável**, ADR-031 item 7; a transição é de SDD-25). Esta ficha só define o **visual**.
- **Opacidade da cena = 0,5:** `SourceConstantAlpha = 128` via `LayeredSurface.SetOpacity` (SDD-04 RNF-04.2, SDD-08 RF-08.1), **sem redesenhar**. A troca presente↔ausente não redesenha sprites: no máximo **1 frame** para a linha de borda (camada estática), mais o `SetOpacity`.
- **Linha de borda** muda para `#3a4558` (esta parte exige redesenhar a camada estática, uma vez).
- **Selo central** (janela própria, ver abaixo), conteúdo e estilo do design:
  - posição: centralizado na faixa (`left = 50%`, deslocado −50% da própria largura), `top = 4`;
  - contêiner: layout em linha, `gap 10`, `padding 7px 12px`, fundo `#0d1c30`, borda 1 px `#2c4468`, Silkscreen **11**, `letter-spacing 1px`, cor `#b8c8e0`, sem quebra de linha;
  - quadrado de **8 × 8**, `#8aa0c0`;
  - texto 1: **`MODO GERENCIADO · AUTO-ESCALA ATIVA`**;
  - texto 2 (cor `#8aa0c0`): **`ausente há Xh Ymin`** (exemplo do design: `ausente há 1h 48min`). Para menos de 1 h, omitir `Xh` (formato inferido; DP-22.11). Atualiza a cada minuto.
- **Janela própria do selo** (`SceneSealWindow`): como a opacidade 0,5 é aplicada à janela inteira e, no design, **só a cena fica a 0,5** (o selo, banners e a barra ficam a 1,0), o selo **não** pode ser desenhado na janela da cena. Ele é uma janela layered `WS_EX_LAYERED | TOOLWINDOW | NOACTIVATE | TOPMOST | TRANSPARENT` (nunca clicável), dona = janela da cena, `SourceConstantAlpha = 255`. Redesenha a cada minuto. DP-22.12.
- **Fps ausente = 8** (RF-22.10). Inimigos continuam marchando.
- Os números flutuantes na cena ausente são **suprimidos** (padrão de DP-22.13, aceito por ADR-030): só o movimento dos inimigos continua.

### RF-22.8: Janelas hotspot (geometria, ciclo de vida e hit-test)

Janelas pequenas, **separadas** da cena, **sempre clicáveis** (nunca `WS_EX_TRANSPARENT`), mesmo com a cena em Passivo. O comportamento (quando aparecem, o que o clique faz, temporização) é de **SDD-26**.

- **Estilos:** `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`, `WS_POPUP`, `WM_MOUSEACTIVATE → MA_NOACTIVATE`. Dona = janela da cena (mesma thread T-Game; ADR-021/028), para ficarem acima dela e ocultarem junto com ela.
- **Ciclo de vida:** criadas no primeiro uso, **reaproveitadas** (`SW_HIDE`/`SW_SHOWNOACTIVATE`) e destruídas no encerramento (SDD-03 RF-03.7). Limite simultâneo: 1 banner (pico **ou** chefe) e **1** caixa de suprimentos (SDD-26 RF-26.11 define o máximo de 1; DP-22.14 resolvida). Ocultas = 0 frames e 0 wakes.
- **Clique:** `WM_LBUTTONUP` dentro do retângulo do controle posta `HotspotClicked(kind, id)` para o host. Sem `SetCapture`, sem foco.
- **Banner de pico/chefe** (`HotspotKind.Banner`):
  - **Bônus ×3 com o banner de chefe no slot (ADR-032):** o banner de chefe prevalece; a contagem do bônus ativo aparece no widget como chip "×3 0:59" (SDD-23 RF-23.3, SDD-26 RF-26.3). O banner "DEFESA ESCALADA ×3" (SDD-26 RF-26.3) usa a mesma geometria do banner de pico, não clicável; a string fica em `Strings` (SDD-15 RF-15.11);
  - controle com **36 de altura**, no **topo da faixa** (`top = 0`), **centralizado** horizontalmente na faixa, largura = a do conteúdo (medida com o `TextCache`);
  - janela = controle + **margem de 6 DIP em todos os lados** para o anel pulsante (altura 48, largura do controle + 12). Posição: `x = centroDaFaixa − larguraJanela/2`, `y = topoDaFaixa − 6` (a janela se estende 6 DIP acima da faixa, o que é permitido por ser janela separada);
  - **pico:** fundo `#f5b335`, texto `#071323`; conteúdo: ícone SVG 16×16 (seta para cima: `M8 13V3M3 8l5-5 5 5`, traço 2, cor do texto), texto **`PICO DE DEMANDA · ESCALAR ×3`**, e um chip de tempo restante (`padding 2px 6px`, fundo `#071323`, texto `#f5b335`, exemplo `0:24`); Silkscreen **12**, `letter-spacing 1px`, `gap 12`, `padding 0 16px`;
  - **chefe:** fundo `#ff5a7a`, texto `#1a0610`; texto **`CHEFE · RESPONDER PERGUNTA AZURE`**, chip com fundo `#1a0610` e texto `#ff9bb0` (exemplo `0:45`); mesma tipografia; `gap 10`, `padding 0 16px`;
  - **anel pulsante** (nos dois): contorno de 0 a **6 DIP** para fora, `rgba(245,179,53,.7)` → `rgba(245,179,53,0)`, ciclo de **1,2 s**, `ease-in-out`. O design usa a cor **âmbar** também no banner do chefe (rosa); mantido como está (DP-22.15). Com `reducedMotion`: **anel fixo de 2 DIP** em `rgba(245,179,53,.7)` (SDD-20 RF-20.6, DP-20.4), sem animação.
  - O chip de tempo muda 1×/s.
- **Caixa de suprimentos** (`HotspotKind.SupplyBox`):
  - controle **52 × 44**, base em `y = 96` (top 52), sem margem; sprite **45 × 24**, centralizado horizontalmente e alinhado à base do controle (`x = left + 3,5`, `y = 72`); **pisca** em opacidade 1 ↔ 0,45 em **1,1 s**, `steps(2)` por trecho (valores discretos 1; 0,725; 0,45; 0,725, trocando a cada 275 ms);
  - posição X: definida por SDD-26 (o design mostra `left = 1060` apenas como exemplo); a janela acompanha o X pedido, dentro da faixa;
  - `reducedMotion`: **sem piscar** (opacidade 1).
- **Hit-test:** o retângulo do controle (e, no banner, também o anel) vale como alvo; pixels de alfa 0 da margem atravessam para a janela de baixo (alfa por pixel, SDD-04 seção 3).

### RF-22.9: Sprites do design, ids dos blobs e exportação

| Elemento | Id do blob (asset store do artefato) | Nome canônico | Tamanho exibido (DIP) |
|---|---|---|---|
| Torre Raio | `7876da3e6e78fb4707a23e01e62f6bfe` | `tower-bolt.png` | 44 × 44 |
| Torre Gelo | `a6621b80f8e0703e2b1589c2f29ffb9f` | `tower-frost.png` | 39 × 44 |
| Torre Canhão | `dd161558f3ec662a35934c4b3ef2ba70` | `tower-cannon.png` | 45 × 44 |
| Slot vazio | `268ba6a35d360775ffe7c94341033a1d` | `slot-empty.png` | 42 × 26 |
| Núcleo | `19329f44a48eaf461324b83ac27582d5` | `core.png` | 84 × 56 |
| Portal | `b825e950b868cc0d3e51f91e73e38462` | `portal.png` | 68 × 52 |
| Chefe | `5208805dc38aec3f98f1e0af4861a263` | `enemy-boss.png` | 68 × 52 |
| Caixa de suprimentos | `ad2f6943f64c22b20bef7d6f3a7d8fac` | `supply-crate.png` | 45 × 24 |
| Scout | `2a01dfce08f7cf4828fa9d3f09f82316` | `enemy-scout.png` | 23 × 26 |
| Drone | `77ebb775d3628025a44ef8f79c2ff41e` | `enemy-drone.png` | 38 × 22 |
| Crawler | `b96d1508c27374dfbbe1f29736f2876d` | `enemy-crawler.png` | 36 × 22 |
| Shield | `dc8cc7e7f1039d49e39b2ccb15bf7b26` | `enemy-shield.png` | 32 × 30 |
| Brute | `2c41c2be426c5c2fb97dec204b4b3316` | `enemy-brute.png` | 43 × 34 |

**Mapeamento tipo → sprite:** `grunt = scout`, `runner = drone`, `swarm = crawler`, `armored = shield`, `tank = brute`, `boss = boss` (chefe).

**Scale Set:** o design ainda **não tem sprite próprio** para a torre "Scale Set" (o card do Arsenal usa o sprite do slot vazio, `268ba6a3…`, como substituto, com o item bloqueado: "Desbloqueia na fase 09"). **Pendência D-13**: usar um **placeholder** (SDD-09 RF-09.6) e registrar em `assets/source/design/MANIFEST.json` com `"placeholder": true`.

**Tarefa de exportação (executada por um worker, uma vez):**

1. Ler cada blob do asset store do artefato "Azure Defense — Idle de barra" (ação `read` do artefato, com `path` igual ao id de 32 caracteres; o worker não altera o artefato).
2. Gravar em `assets/source/design/<nome canônico>` (a pasta é nova; **não** reutilizar `assets/source/sprites/`).
3. Gerar `assets/source/design/MANIFEST.json`: `{ "id": "...", "file": "...", "displayDip": [w, h], "pixels": [w, h], "sha256": "...", "role": "..." }` para cada arquivo e uma linha de Scale Set com `placeholder: true`.
4. Registrar origem, data e licença em `assets/source/ATTRIBUTION.md` (a licença é do próprio autor; D-03).
5. **Não** baixar nada da rede (ADR-014) e **não** alterar a origem. Os PNG exportados só são lidos; o `AssetPipeline` (SDD-09 RF-09.3) gera o atlas premultiplicado.
- Esta tarefa **substitui SDD-09 RF-09.1**.

### RF-22.10: Política de fps e redesenho

| Estado | fps da cena | Observações |
|---|---|---|
| Presente | **30** (configurável 20/30/60, SDD-15; ADR-031 item 7) | `RenderPolicy` com `simRunning` (SDD-10 RF-10.7) |
| Ausente | **8** (configurável 4/8; padrão 8) | cena a 0,5; o bob (125 ms por passo) coincide com 8 fps |
| `reducedMotion` | igual ao estado | **sem bob** e **sem animação do pulso** (anel fixo de 2 DIP, SDD-20); números flutuantes sem subida |
| Perfil Saver (SDD-07 RF-07.5) | `min(fps do estado, 15)` | animações ambientes desligadas |
| Oculto (SDD-06/07) | **0** | 0 frames, 0 wakes; hotspots também ocultos |

- Os banners e caixas redesenham só quando o estado visual **discreto** muda (chip 1×/s; piscar a cada 275 ms) ou, no pulso, no máximo **15 vezes por segundo** (valor proposto; meta não comprovada).
- Frame **ignorado** (sem `UpdateLayeredWindow`) quando nada mudou (SDD-08 RF-08.7). A troca de opacidade usa só `SetOpacity`.
- A simulação continua em 30 Hz (ADR-004), independentemente do fps de render.

## 6. Requisitos não funcionais

Todas as metas abaixo são **META NÃO COMPROVADA** até haver medição em `specs/evidencias/` (SDD-16). Vale a regra de baixo consumo: nenhum timer periódico além dos deadlines do host.

**Área de renderização** (`largura física × 96·scale`, BGRA, 4 B/px; atualização total):

| Monitor / escala | Superfície (px) | Mpx | MB (DIB) |
|---|---|---|---|
| 1366×768 @100% | 1366 × 96 | 0,13 | 0,52 |
| 1920×1080 @100% | 1920 × 96 | 0,18 | 0,74 |
| 1920×1080 @125% | 1920 × 120 | 0,23 | 0,92 |
| 1920×1080 @150% | 1920 × 144 | 0,28 | 1,11 |
| 3440×1440 @100% | 3440 × 96 | 0,33 | 1,32 |
| 2560×1440 @150% | 2560 × 144 | 0,37 | 1,47 |
| 3840×2160 @150% | 3840 × 144 | 0,55 | 2,21 |
| 3840×2160 @200% | 3840 × 192 | 0,74 | 2,95 |

- **RNF-22.1 (composição):** p95 ≤ 2 ms por frame no cenário `strip-present` (24 inimigos, 12 torres, 24 números flutuantes, 1 banner, 1 caixa) em 1920×1080 @150% (meta; SDD-16 P-11 é de 4 ms para a faixa antiga, mais pesada).
- **RNF-22.2 (Present):** `UpdateLayeredWindowIndirect` p95 ≤ 1,5 ms no mesmo cenário (SDD-16 P-12 usa 2 ms) (meta).
- **RNF-22.3 (atualização parcial por faixa vertical):** como todas as entidades ocupam a altura inteira da faixa (96), a granularidade é **horizontal**. O renderizador mantém uma lista de **bandas verticais** (intervalos de X, em px, alinhados a múltiplos de 4, expandidos em 2 px, de cada entidade, número flutuante ou barra que mudou, posição atual ∪ anterior) e funde as que se tocam ou distam < 16 px. A rasterização limpa e redesenha **só dentro das bandas** (copiando a camada estática por banda). Como `UPDATELAYEREDWINDOWINFO` aceita **um** `prcDirty`, o `Present` usa a **união** das bandas; se a união cobrir > 60% da largura, usa frame completo. É o mecanismo de SDD-08 RF-08.7, **continua atrás de `RenderOptions.PartialUpdates`** (desligado na E1; ligado na E7 se o benchmark mostrar ganho ≥ 20% de CPU no cenário `strip-present`).
- **RNF-22.4 (cena ausente):** `strip-away` (8 fps, mesma carga de inimigos, opacidade 0,5): CPU do processo ≤ 0,4% e DWM Δ ≤ 0,3% (meta). Presentes por segundo = 8 ± 1.
- **RNF-22.5 (cena presente):** `strip-present` a 30 fps: CPU do processo ≤ 1,2% e DWM Δ ≤ 0,8% (meta; SDD-16 P-04 é 1,5% e 1%).
- **RNF-22.6 (parado):** com `reducedMotion` e sem inimigos na tela, 0 Presents durante 30 s sem input.
- **RNF-22.7 (alocação):** ≤ 64 B por frame (E1–E6), 0 B (E7+), como SDD-08 RNF-08.3.
- **RNF-22.8 (memória):** DIB + camada estática ≤ 2 × 3 MB em 4K @200% (tabela acima), mais o atlas pré-escalado ≤ 1 MB (SDD-09 RF-09.5).
- **RNF-22.9 (foco):** zero `WM_SETFOCUS` e zero mudanças de foreground em 100 cliques na cena (Interativa) e nos hotspots (SDD-03 CA-03.2).
- **RNF-22.10 (latência):** troca presente↔ausente em ≤ 16 ms (só `SetOpacity`, como SDD-04 RNF-04.1); clique em torre/slot até o Arsenal visível ≤ 50 ms (SDD-16 P-15).

## 7. Estratégia de implementação

1. **E1 (janela):** `SceneLayout.Compute` (puro, Runtime) e a `SceneWindow` com quadro de teste (retângulo do tamanho exato da faixa, 96 DIP, com a linha de borda e marcas nas posições do design). Integrar `TaskbarLayout` de SDD-21 (o `SlotMap` já pode vir do fallback). Medir presentes e CPU do quadro de teste (`e1-strip-synthetic`).
2. Exportar os sprites (RF-22.9) para `assets/source/design/` e atualizar o pipeline de assets (SDD-09) para ler a nova pasta. **Sem os PNG**, o `PlaceholderAtlas` (SDD-09 RF-09.6) cobre todos os ids de RF-22.9.
3. **E4 (cena):** `SceneRenderer` (camada estática, camada dinâmica, `FloatingNumbers`), `SceneSealWindow` e `HotspotWindow` com seus ciclos de vida. Hit-test `SceneHitTester` (puro) com os retângulos de RF-22.3.
4. Ligar a política de fps (RF-22.10) à `RenderPolicy` (SDD-10) e testar com `FakeClock`.
5. Golden images (seção 9, CA-22.7) e medições (seção 6) em `specs/evidencias/`.

## 8. Dependências

SDD-20 (tokens: cores, Silkscreen e JetBrains Mono, ADR-025), SDD-21 (`TaskbarLayout`, `SlotMap`, `TrayLeftX`), SDD-08 (superfície layered, `FrameComposer`, invalidação, texto), SDD-11 (estado da simulação e eventos de dano; a revisar para o mundo 1D). SDD-03, SDD-04, SDD-05, SDD-06, SDD-07 e SDD-10 fornecem janela, modos, monitor, ocultação, ciclo de vida e política de render. Consumidores: SDD-25 e SDD-26.

## 9. Critérios de aceitação

- **CA-22.1:** Os testes de tabela de `SceneLayout.Compute` passam para 1366×768@100%, 1920×1080@100%/125%/150%, 2560×1440@150%, 3840×2160@150%/200% e 3440×1440@100%, com barra embaixo, barra em outra borda e ocultação automática (margem de 2 px). Valores esperados calculados e fixados no teste.
- **CA-22.2:** Os testes de geometria da cena (função pura `SceneGeometry`) confirmam **todos** os valores da tabela de RF-22.5 para `W = 1440` (núcleo em (10,40) 84×56; barra do núcleo em (22,32) 60×5; portal em X = 1256 com `TrayLeftX = 1240`; 8 slots em `cellLeft = 640 + 48·i`; torres Raio/Canhão/Gelo com X `cellLeft`, `cellLeft`, `cellLeft + 3`; rótulos em top 38; slot vazio em top 70, X `cellLeft + 1`; "+" em top 54; barra do chefe em top 36, X = `boss.Left + 4`).
- **CA-22.3:** No Windows 11 do usuário, a faixa fica colada acima da barra, sem sobrepor a barra nem sair da `WorkArea`, e em Passivo um clique em qualquer ponto da faixa chega ao aplicativo de baixo (`WindowFromPoint` devolve a janela de baixo; SDD-04 CA-04.1) (MTC-22.1 e MTC-22.2).
- **CA-22.4:** Em Interativo, clicar numa torre ou num slot vazio abre o Arsenal focado naquele `slotIndex`; clicar em área vazia da faixa **atravessa**. Sem mudança de foreground (T-22.5, MTC-22.3).
- **CA-22.5:** Banners e caixas respondem ao clique **com a cena em Passivo**, sem roubar foco (T-22.6). O anel do banner não é cortado (margem de 6 DIP).
- **CA-22.6:** Presente → ausente em ≤ 16 ms, sem redesenho de sprites (apenas `SetOpacity` e 1 frame da linha de borda) e com o selo visível a 100% de opacidade (MTC-22.4); o contador de Presents confirma 8 ± 1 por segundo em ausente (T-22.7).
- **CA-22.7 (golden images, Rendering.Tests, SDD-17 RF-17.3):** cenas `strip-present`, `strip-away` (cena com a opacidade 0,5 aplicada na comparação), `strip-peak`, `strip-boss`, `strip-fallback` e `strip-empty-slots`, em largura de 1440 DIP a 100% e a 150%, com `PlaceholderAtlas` e com o atlas real. Diferença ≤ 0,5% de pixels com ΔE > 8. Variante **sem texto** para comparação estrita entre sistemas. Comparação visual manual com as capturas dos modos `presente`, `ausente`, `pico` e `chefe` do componente `Strip` do design, anexada à evidência.
- **CA-22.8:** `reducedMotion` desliga bob, pulso, piscar e a subida dos números (T-22.8). Com o perfil Saver, o fps nunca passa de 15.
- **CA-22.9:** Ocultar por fullscreen (SDD-06) oculta a cena, o selo e todos os hotspots, com 0 frames e 0 wakes (SDD-07 RNF-07.1), e reexibir os restaura em ≤ 1 s com o conteúdo correto no primeiro frame (MTC-22.10; contagem de frames e wakes em T-22.7).
- **CA-22.10:** Os PNG exportados de RF-22.9 existem em `assets/source/design/`, com `MANIFEST.json` (13 entradas + Scale Set como placeholder), cada `sha256` confere e os ids batem com a tabela (T-22.11).

### Matriz MTC-22 (manual, Windows 11)

O ID de cada linha é `MTC-22.<#>` (coluna `#`).

| # | Ação | Esperado |
|---|---|---|
| 1 | Iniciar o jogo com a barra embaixo (100%, 125%, 150%) | Faixa de 96 DIP colada acima da barra, largura do monitor |
| 2 | Clicar e rolar sobre a faixa em Passivo (Chrome e VS Code embaixo) | O clique e a rolagem chegam ao app de baixo |
| 3 | Ctrl+Alt+G, clicar em torre e em slot vazio | Arsenal abre focado; clicar entre as torres atravessa |
| 4 | Ficar 5 min sem input | Cena a 50%, selo central com o tempo, borda cinza |
| 5 | Voltar a usar o PC | Cena a 100%, borda âmbar, sem o selo |
| 6 | Mostrar banner de pico e de chefe (modo de teste) | Banner de 36 de altura no topo, centralizado, anel pulsando, clicável com a cena em Passivo |
| 7 | Mostrar a caixa de suprimentos | 52×44 com o sprite piscando, clicável |
| 8 | Barra com ocultação automática | Faixa na borda inferior com margem de 2 px; a barra continua ativável |
| 9 | Mudar a escala e o monitor | Nitidez mantida; faixa reposiciona em ≤ 1 s |
| 10 | Chrome em F11 | Cena, selo e hotspots ocultos |

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-22.1 | `SceneLayout.Compute`: tabela de CA-22.1, incluindo origem negativa de monitor e ocultação automática | Linux |
| T-22.2 | `SceneGeometry`: todas as posições de RF-22.5 (CA-22.2), fallback `W − 184` e arredondamento `round((44 − w)/2)` | Linux |
| T-22.3 | Mundo 1D: `spawnX`, `leakX`, `PathLength`, distância `|x_inimigo − x_torre|` e conversão px↔DIP | Linux |
| T-22.4 | `SceneHitTester`: retângulos de torre e slot vazio, borda das células, nenhum alvo fora | Linux |
| T-22.5 | Cena Interativa: 100 cliques via `SendInput` em torre/slot/vazio com `TestFocusTarget` em foco: foreground inalterado e só os cliques nos alvos chegam ao jogo | WindowsInteractive |
| T-22.6 | Hotspots: clique com a cena em Passivo chega ao host; `WM_MOUSEACTIVATE`; criação, ocultação e reuso 100× sem vazamento de GDI/USER | WindowsInteractive |
| T-22.7 | Política de fps com `FakeClock`: 30/8/15, `reducedMotion`, oculto = 0; contagem de Presents | Linux |
| T-22.8 | Animações: bob (`0, −1, −2, −1`), número flutuante (16 DIP, 1,4 s, fade, pool de 24), piscar (1; 0,725; 0,45; 0,725) e pulso, com e sem `reducedMotion` | Linux |
| T-22.9 | Golden images (CA-22.7) | Linux + Windows |
| T-22.10 | `RenderInvalidation` por bandas verticais: fusão (< 16 px), alinhamento a 4, limiar de 60% | Linux |
| T-22.11 | Exportação dos sprites: `MANIFEST.json` consistente com a tabela de RF-22.9 | Linux |
| T-22.12 | Benchmarks `strip-present`, `strip-away`, `strip-idle` (RNF-22.1 a RNF-22.6), com saída CSV | WindowsInteractive |
| MTC-22.1..10 | Matriz acima | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| A faixa cobre a parte de baixo de janelas maximizadas (barra de status e terminal do VS Code) | Alta | Médio | Click-through (Passivo), opacidade, `peek` (DP-22.3); modo AppBar de SDD-05 RF-05.8 é pós-MVP e **conflita** com esta geometria (a área de trabalho encolheria e a fórmula de `y` deslocaria duas vezes: revisar antes da E5) |
| Mundo 1D dependente do monitor torna a simulação não determinística entre máquinas | Média | Alto | DP-22.1 |
| Pixel art em escalas não inteiras (125%, 150%, 175%) gera pixels desiguais | Alta | Baixo | `Nearest` a partir de cache pré-escalado (SDD-09); DP-22.7 sobre a política (arredondar para escala inteira ou aceitar) |
| Ocultação automática da barra faz a barra surgir **por cima** da faixa (ambas topmost) | Média | Baixo | Margem de 2 px (SDD-05 RF-05.7), reafirmação de topmost limitada (SDD-03 RF-03.6); validar em MTC-22.8 |
| Várias janelas layered (cena, selo, banner, caixas) aumentam o custo do DWM | Média | Médio | Hotspots pequenos e ocultos quando inativos; pulso limitado a 15 Hz; medir `strip-present` com banner e caixa (RNF-22.1) |
| Selo em janela própria some em ordem de z diferente da cena | Baixa | Baixo | Janela dona = cena; testes T-22.6 |
| 8 fps contínuos em modo ausente contrariam a prioridade de baixo consumo (SDD-07 RNF-07.1 previa 0 fps em ociosidade) | Média | Médio | Meta RNF-22.4; se reprovar, abrir ADR com a medição (SDD-16 "Revisão de metas"); alternativa: 1 fps com cena parada |
| Silkscreen não carregar ou ficar borrada em tamanhos de 9–12 px escalados | Média | Baixo | TTF fornecida pelo usuário em `assets/fonts` (ADR-025); texto por `SKTextBlob` pré-criado por escala; golden de texto só por bounding box |
| Largura do banner depende da fonte e do idioma | Baixa | Baixo | Medir com o `TextCache` e dimensionar a janela em tempo de execução |

### Decisões pendentes (lacunas reais; o Tech Lead decide ou delega)

| ID local | Questão | Recomendação | Bloqueia |
|---|---|---|---|
| DP-22.1 | ~~Determinismo do layout~~ **Resolvida por ADR-026:** layout no estado, mudado só por `LayoutChanged` entre ticks, no save e no `StateHash` (RF-22.2) | n/a | n/a |
| DP-22.2 | A linha de borda de estado fica na **barra de tarefas** no design (`y` 96–98 de 144) e a cena só tem 96 DIP | Desenhar nos 2 últimos DIP da cena (como nesta ficha), sob os sprites, aceitando cobrir 2 DIP do pé dos sprites; alternativa: janela da cena com 98 DIP | RF-22.6 |
| DP-22.3 | Peek (SDD-04 RF-04.5): manter na cena? Combina com a opacidade 0,5 do modo ausente? | Manter desligado por padrão nesta faixa, pois ela já é pequena e translúcida em ausente | SDD-04, SDD-22 |
| DP-22.4 | O design não tem a alça (HandleWindow, ADR-011). Como o usuário alterna Passivo/Interativo? | Atalho (Ctrl+Alt+G) + widget (SDD-23) + bandeja; remover a HandleWindow | SDD-03 RF-03.3, SDD-04 |
| DP-22.5 | Fim de RF-22.1: a fórmula de `y` é a mesma com a barra embaixo ou em outra borda; o enunciado descreve como casos distintos | Manter a fórmula única; só a margem de 2 px com ocultação automática muda | — |
| DP-22.6 | Pico e chefe simultâneos (ambos pedem o mesmo topo da faixa) | SDD-26 define prioridade; esta ficha prevê 1 banner por vez | SDD-26 |
| DP-22.7 | Política de escala dos sprites (pixels nativos dos PNG do design e escalas não inteiras) | Registrar os `pixels` reais no `MANIFEST.json` e decidir depois da exportação | RF-22.9 |
| DP-22.8 | ~~Projéteis e efeitos de impacto~~ **Resolvida por ADR-027:** acerto instantâneo, só número de dano | n/a | n/a |
| DP-22.9 | O ponto de vazamento (`leakX`) não vem do design (que só mostra a marcha em loop) | `leakX = 94` (borda direita do núcleo). Ajustar em SDD-11 se o playtest pedir | SDD-11 |
| DP-22.10 | Origem do número flutuante relativa ao inimigo (o design mostra posições fixas, `top = 32/34`) | **Resolvida por ADR-032:** `top` fixo em 32 (RF-22.5) | RF-22.5 |
| DP-22.11 | Formato de "ausente há" para menos de 1 h e para ≥ 12 h | `Ymin` (< 1 h) e `Xh Ymin`; limite de 12 h pelo modo gerenciado (canvas) | RF-22.7 |
| DP-22.12 | Selo em janela própria (opacidade 100%) vs. desenhado na cena (ficaria a 50%) | Janela própria, como definido | RF-22.7 |
| DP-22.13 | Números flutuantes durante o modo ausente (8 fps): o design os mantém | **Aceita (ADR-030):** suprimir em ausente (custo, e ninguém olha) | RF-22.7 |
| DP-22.14 | ~~Máximo de caixas simultâneas~~ **Resolvida por SDD-26 RF-26.11:** 1 caixa por vez | n/a | n/a |
| DP-22.15 | O anel pulsante do banner do chefe usa âmbar em fundo rosa (herdado do design) | Manter e confirmar com o design | RF-22.8 |

### Fichas a revisar por causa desta ficha (não editadas aqui)

- **SDD-05:** RF-05.4, RF-05.5 e RF-05.9 (substituídos); RF-05.8 (AppBar × nova geometria).
- **SDD-12:** RF-12.2 (mapa) e a tabela de sprites de RF-12.5/12.4.
- **SDD-11:** RF-11.3, RF-11.5, RF-11.9, RF-11.10, RF-11.12 (mundo 1D, slots, determinismo, projéteis).
- **SDD-08:** RF-08.2 a RF-08.5 (camadas, ordem, entidades, efeitos).
- **SDD-09:** RF-09.1 e RF-09.2 (importação e ids de sprites).
- **SDD-04:** RF-04.1, RF-04.4, RF-04.6 (e a alça).
- **SDD-03:** RF-03.3 e ADR-011 (HandleWindow).
- **SDD-07:** RF-07.2 e RF-07.4 (ausente como modo gerenciado).
- **SDD-10:** RF-10.7 (fps por presença).
- **SDD-15:** atalhos, arraste de torre e HUD.
- **SDD-16:** novos cenários `strip-present`, `strip-away`, `strip-idle`.
