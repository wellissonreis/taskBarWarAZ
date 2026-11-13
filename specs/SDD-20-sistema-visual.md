# SDD-20: Sistema visual "Azure Defense": tokens, tipografia, ícones e animações

**Status:** Rascunho para revisão · **Etapa:** E4 · **Depende de:** SDD-08, SDD-09 · **Bloqueia:** SDD-22, 23, 24

## 1. Identificador e título

SDD-20: Tokens de cor, tipografia, espaçamento, raio, sombra dura, alvos de clique, catálogo de ícones vetoriais, catálogo de animações e regras de contraste do design oficial (ADR-018).

## 2. Objetivo

Ser a **única fonte** dos valores visuais do jogo. Toda ficha de interface (SDD-22 faixa de cena, SDD-23 widget, SDD-24 painel e cards) cita tokens daqui pelo nome e não repete hex nem px soltos. O design aprovado é o artefato "Azure Defense — Idle de barra" (arquivos `Main.dc.html`, `Strip.dc.html`, `Presence.dc.html`, `Offline.dc.html`). Todos os valores abaixo foram transcritos desses arquivos.

## 3. Contexto técnico

- ADR-018: o design aprovado é o oficial; estes tokens (SDD-20) o descrevem.
- ADR-019: o jogo é um idle; ondas automáticas, presença/ausência/offline. Os estados de borda e os selos de presença nascem daí.
- ADR-021: painel, cards, banners e caixas são janelas layered Skia `WS_EX_NOACTIVATE`; WPF só para Configurações e Sobre. Portanto **todos os tokens são consumidos por Skia**, nunca por WPF (exceto as duas janelas WPF, que podem reutilizar os hex).
- ADR-025: fontes **Silkscreen** e **JetBrains Mono** (OFL) substituem Pixelify Sans e IBM Plex. Isto **substitui SDD-08 RF-08.6** (lista de fontes) e a nota de fontes de SDD-09 (importação das fontes de referência). Os `.ttf` são fornecidos pelo usuário (D-03); o repositório não baixa fontes (ADR-014).
- SDD-08 RF-08.8 continua valendo: zero alocação por frame, sem `SKImageFilter` por frame, sem blur, retângulos sem antialias.
- SDD-16 continua valendo: prioridade absoluta de baixo consumo; todas as metas deste documento são **não comprovadas**.
- O design é desenhado em CSS px a 96 DPI. **1 CSS px do design = 1 DIP.** A conversão para pixel físico é `round(DIP × dpi/96)` (SDD-05).
- O formato de números e textos pt-BR é definido em SDD-27 e **só é referenciado aqui**.

## 4. Escopo e exclusões

**Escopo:** tokens de cor (semânticos), tipografia e regra de renderização de texto, espaçamento, raio, sombra dura, alvo mínimo de clique, ícones (path data e conversão para `SKPath`), animações (keyframes, duração, fps, `reducedMotion`, Saver), estados de borda, contraste.

**Exclusões:**

- layout e conteúdo da faixa, do widget, do painel e dos cards (SDD-22/23/24);
- geometria da barra de tarefas (SDD-21);
- formatação de números, unidades e textos (SDD-27);
- carga, escala e atlas dos sprites PNG (SDD-09);
- comportamento de jogo (SDD-25/26).

**Substitui (o design vence):**

| Ficha | Item | Motivo |
|---|---|---|
| SDD-08 | RF-08.6 (fontes Pixelify Sans e IBM Plex) | ADR-025: Silkscreen + JetBrains Mono |
| SDD-08 | RF-08.4, cores de barra de vida (`#5cf0a8`, `#f0ca79`, `#ff6a7e`, fundo `#3a1a26`) | o design usa barra de núcleo `#5cf0b0` sobre `#16304d` e barra de chefe `#ff5a7a` sobre `#3a0d1d`; os demais tipos de barra seguem o token `hp-*` abaixo |
| SDD-08 | RF-08.4, "Base: barra NÚCLEO na HUD (não no campo)" | no design a barra do núcleo fica na cena, acima do sprite (60×5 DIP); a decisão final de posição é de SDD-22 |
| SDD-08 | RF-08.2, camada HUD "painel esquerdo" | o painel esquerdo da HUD de SDD-15 é substituído pelo widget de SDD-23 |
| SDD-09 | nota "fontes WOFF2 da referência → TTF equivalentes (Pixelify/IBM Plex)" | ADR-025 |

## 5. Requisitos funcionais

### RF-20.1: Tokens de cor (nome semântico → hex)

Todos os hex são sRGB opacos, salvo indicação de alfa. Skia: `SKColor` pré-criado em campos `static readonly` (zero alocação por frame). O nome é o identificador usado no código (`Palette.<Nome>`, em `SideWar.Rendering`).

**Fundos e superfícies**

| Token | Hex | Uso no design |
|---|---|---|
| `bg-abismo` | `#050e1b` | fundo do quadro da tela "Bem-vindo de volta" (apenas apresentação; **não** é desenhado no jogo, a janela é transparente ao redor) |
| `bg-trilho-fundo` | `#071323` | fundo do slot do ícone de torre (52×52), tinta escura sobre âmbar/menta, borda da barra de vida, texto sobre botões âmbar e menta |
| `bg-painel` | `#0b1a2e` | painel, cards, widget (alfa 230 no widget), botão de ação secundário (transparente) |
| `bg-item` | `#0d1f37` | linhas de torre, de fase, de missão, tiles, opções de resposta |
| `bg-card` | `#0f2440` | cards de destaque (auto-escala, fase atual) |
| `bg-ausente` | `#0d1c30` | selo "MODO GERENCIADO" na faixa |
| `bg-barra-so` | `#1b2230` | **apenas** a barra de tarefas simulada no design; no jogo vem do Windows. Não é token de jogo |
| `bg-trilho` | `#16304d` | trilho de barras de progresso (5 e 8 DIP), botão bloqueado, borda do selo de HP |

**Bordas**

| Token | Hex | Uso |
|---|---|---|
| `borda-painel` | `#1f3350` | borda 2 DIP do painel, 1 DIP do widget, de itens e de tiles, filete do cabeçalho |
| `borda-forte` | `#2c4468` | borda de cards de destaque, de opções de resposta, do selo "MODO GERENCIADO", do botão "COLETAR +X" |

**Texto**

| Token | Hex | Uso |
|---|---|---|
| `txt-principal` | `#e8f0ff` | texto padrão do painel, nomes, números |
| `txt-secundario` | `#b8c8e0` | estatísticas, descrições, "Você esteve fora por" |
| `txt-terciario` | `#9fb2cc` | descrição do card de auto-escala |
| `txt-rotulo` | `#8aa0c0` | rótulos de 10 DIP, botão fechar, nota de rodapé |
| `txt-widget-rotulo` | `#a9b6c8` | rótulo "onda", selo "AUSENTE" (cor e borda), marca de app aberto |
| `txt-botao-neutro` | `#c9d6ea` | texto do botão "COLETAR +X" |
| `txt-branco` | `#ffffff` | selos "LV n" e "+" sobre a cena (com sombra `1px 1px 0 #000`) |

**Âmbar (acento primário, crédito, ação)**

| Token | Hex | Uso |
|---|---|---|
| `ambar` | `#f5b335` | título do painel, créditos, botão primário, borda de presença, "LV n", estrelas, barra de missão, barra de ativo do nav, badge |
| `ambar-hover` | `#ffc139` | `ambar` × 1,08 por canal (equivale a `filter: brightness(1.08)` do design), pré-calculado |
| `ambar-anel` | `rgba(245,179,53,.7)` | início do anel `adPulse` |

**Menta (positivo, presente)**

| Token | Hex | Uso |
|---|---|---|
| `menta` | `#5cf0b0` | taxa `+312/s`, selo "PRESENTE", barra de HP do núcleo, toggle ON, tile ativo, resposta correta (borda) |
| `menta-fundo` | `#0f2a2a` | card de bônus de presença, resposta correta (fundo) |
| `menta-borda` | `#2f7a62` | borda do card de bônus de presença |
| `menta-faixa` | `#12403a` | faixas inativas do bônus (0–15 e 60+) |
| `menta-texto` | `#b8d8cc` | texto no card de bônus e nas faixas inativas |

**Azul (informação, progresso, gelo)**

| Token | Hex | Uso |
|---|---|---|
| `azul` | `#3aa0ff` | barra de progresso de fase e de tempo acumulado, borda do card "fase atual" |
| `azul-claro` | `#9fd3ff` | rótulo "FASE ATUAL", número flutuante "-96" |
| `gelo` | `#7fe7ff` | número flutuante "LENTO" |

**Perigo (chefe, alerta)**

| Token | Hex | Uso |
|---|---|---|
| `perigo` | `#ff5a7a` | borda do estado chefe, botão "CHEFE · RESPONDER", borda do card de pergunta, barra de HP do chefe, resposta errada (borda) |
| `perigo-claro` | `#ff9bb0` | cabeçalho e timer do card de pergunta, ícone de alerta, timer no botão de chefe |
| `perigo-texto` | `#ffd0da` | texto do aviso de perdas |
| `perigo-fundo` | `#2a1420` | fundo do aviso de perdas |
| `perigo-borda` | `#7a2f48` | borda do aviso de perdas |
| `perigo-trilho` | `#3a0d1d` | trilho da barra de vida do chefe |
| `perigo-tinta` | `#1a0610` | texto e chip sobre `perigo` |

**Prestígio (Migrar)**

| Token | Hex | Uso |
|---|---|---|
| `prestigio` | `#8f74ff` | botão MIGRAR |
| `prestigio-hover` | `#9a7dff` | `prestigio` × 1,08, pré-calculado |
| `prestigio-claro` | `#c9b8ff` | título do card, "+8" |
| `prestigio-fundo` | `#1a1636` | fundo do card de migração |
| `prestigio-borda` | `#5d4ba0` | borda do card de migração |
| `prestigio-texto` | `#cfc6ee` | corpo do card de migração |
| `prestigio-tinta` | `#0b0720` | texto sobre `prestigio` |

**Estados de presença e borda de cena**

| Token | Hex | Uso |
|---|---|---|
| `borda-presente` | `#f5b335` (= `ambar`) | borda superior de 2 DIP da faixa em estado presente e pico |
| `borda-ausente` | `#3a4558` | borda da faixa em estado ausente |
| `borda-chefe` | `#ff5a7a` (= `perigo`) | borda da faixa em estado chefe |
| `selo-presente` | `#5cf0b0` (= `menta`) | cor e borda do selo "PRESENTE ×2,0" |
| `selo-ausente` | `#a9b6c8` (= `txt-widget-rotulo`) | cor e borda do selo "AUSENTE ×0,25" |
| `ausente-marca` | `#8aa0c0` (= `txt-rotulo`) | quadrado 8×8 e tempo "ausente há …" do selo "MODO GERENCIADO" |

**Interação (translúcidos, sobre qualquer fundo)**

| Token | Valor | Uso |
|---|---|---|
| `hover-nav` | `rgba(255,255,255,.10)` | hover de botão do nav do widget |
| `ativo-nav` | `rgba(255,255,255,.12)` | aba ativa do nav |
| `sombra-painel` | `rgba(0,0,0,.35)` | sombra dura do painel |
| `sombra-card` | `rgba(0,0,0,.45)` | sombra dura dos cards de pergunta e de ausência |
| `sombra-texto` | `#000000` | sombra de texto `1px 1px 0` sobre a cena |

**Cores de sprites e de cena (referência; os pixels vêm de SDD-09):** o design usa, para os ícones da barra simulada, `#c9d3e0`, `#3aa0ff`, `#5cb85c`, `#e8a23a`, `#9b7bff`, `#e45b5b`, `#3ac7c7`, `#7f8ea3`. Eles representam **aplicativos do usuário**, não são tokens do jogo. A barra do Windows (`#1b2230`, texto `#c9d3e0`, `system-ui`) também não é do jogo.

**Mantidos de SDD-08 sem equivalente no design (não substituídos):** anéis de tipo de inimigo (`#ff7ad9`, `#c9d4de`, `#7ff8e6`) e cores de barra de vida de inimigos comuns. SDD-22 decide se mantém.

**Regras:**

- Nenhum hex fora desta tabela pode aparecer em código de render. Um teste (T-20.1) varre `SideWar.Rendering` e `SideWar.Ui` procurando literais `0xFF......` fora de `Palette`.
- Alfa do fundo do widget: 230 (ver SDD-23). Alfa do fundo de painel e cards: 255.

### RF-20.2: Tipografia

| Papel | Família | Pesos carregados | Observação |
|---|---|---|---|
| Títulos, números, botões, selos, rótulos de nav | **Silkscreen** | 400 (Regular) e 700 (Bold) | `h1` do design usa `font-weight: 400`; Bold só se o design pedir (não pedido hoje) |
| Corpo, estatísticas, notas, opções de resposta | **JetBrains Mono** | 400 e 600 | o design pede os dois pesos na carga; **nenhum texto do design usa 600 explicitamente** |

Escala usada no design (DIP), com o uso:

| Fonte | Tamanho | Usos |
|---|---|---|
| Silkscreen | 9 | custo no botão MELHORAR, selo "LV n" das torres na cena |
| Silkscreen | 10 | "ON" do toggle, texto de botão (MELHORAR, COLETAR), "+" de slot livre, selo de presença, números flutuantes |
| Silkscreen | 11 | título do card de auto-escala, número da fase, estrelas, cabeçalho do card de pergunta (+ timer), selo "MODO GERENCIADO", "COLETAR +X" |
| Silkscreen | 12 | nome de torre, "BÔNUS DE PRESENÇA", `37/60`, botão MIGRAR, botões de pico e chefe |
| Silkscreen | 13 | título do card de migração, botão "COLETAR ×2" |
| Silkscreen | 14 | título do painel (com `letter-spacing` 1), créditos `48,2K` |
| Silkscreen | 15 | nome da fase atual |
| Silkscreen | 16 | multiplicador `×2,0` do card de presença |
| Silkscreen | 18 | números dos tiles de Migrar |
| Silkscreen | 20 | números dos tiles do relatório offline |
| Silkscreen | 24 | título "BEM-VINDO DE VOLTA" |
| JetBrains Mono | 9 | número do badge do nav |
| JetBrains Mono | 10 | rótulos, taxa `+312/s`, "onda", sublinha de tiles, nota de rodapé, sublinha do botão "COLETAR ×2" |
| JetBrains Mono | 11 | descrições, estatísticas das regiões, corpo dos avisos, opções de resposta |
| JetBrains Mono | 12 | título de missão, título de fase, "Você esteve fora por" |
| JetBrains Mono | 13 | texto da pergunta |

Letter-spacing (DIP): `1` em títulos do painel, rótulos em caixa alta de 10–11 DIP, selos de faixa, botões MIGRAR, "COLETAR ×2" e "COLETAR +X", botões de pico e chefe e cabeçalho do card de pergunta; `0` no resto (inclusive MELHORAR e o "COLETAR" de missão, que não têm `letter-spacing` no design) e na sublinha do botão "COLETAR ×2" (que **zera** o `letter-spacing` do botão pai). Line-height (múltiplo do tamanho): `1,1` para créditos e `37/60`; `1,45` para descrições de 11 DIP e pergunta de 13 DIP; `1,5` para o corpo do card de migração; demais `normal` (métrica da fonte).

**Carga:** `SKTypeface.FromStream` de `assets/fonts/Silkscreen-Regular.ttf`, `Silkscreen-Bold.ttf`, `JetBrainsMono-Regular.ttf`, `JetBrainsMono-SemiBold.ttf`, embutidos como recurso (SDD-09). **Os arquivos são fornecidos pelo usuário (D-03/ADR-025); ausência bloqueia a etapa E4, não é baixada pelo app.** Licença OFL anexa em `assets/fonts/LICENSE-*.txt`.

### RF-20.3: Regra de renderização de texto no Skia

1. **Silkscreen: sem antialias.** `SKFont.Edging = SKFontEdging.Alias`, `SKFont.Subpixel = false`, `SKFont.Hinting = SKFontHinting.None`, `SKPaint.IsAntialias = false`. Objetivo: pixels nítidos, como no design.
2. **Tamanho físico inteiro:** `sizePx = round(sizeDip × dpi/96)`. Nunca fracionário. Para 100% (96 DPI) o tamanho coincide com o DIP do design. Em escalas fracionárias (125%, 175%) o tamanho arredondado é usado **também** para a métrica de layout, para não deslocar o texto.
3. **Posição de texto em pixel inteiro:** x e y arredondados antes de `DrawText`.
4. **JetBrains Mono:** `SKFontEdging.Antialias` (grayscale, sem `SubpixelAntialias`, porque a superfície tem alfa por pixel e LCD gera franjas coloridas). `Subpixel = false`, `Hinting = Slight`.
5. `letter-spacing`: Skia não o aplica; o `TextCache` posiciona glifo a glifo com `SKFont.GetGlyphWidths` + espaçamento inteiro em px físicos, calculado **uma vez** por (string estática, escala) e guardado em `SKTextBlob` (SDD-08 RF-08.6). Textos dinâmicos (números) usam o caminho `Span<char>` de SDD-08 e o mesmo laço, sem alocar.
6. **Sombra de texto sobre a cena** (`1px 1px 0 #000`): o texto é desenhado duas vezes, primeiro deslocado `(1,1)` px físicos em `sombra-texto`, depois na cor. Sem blur.
7. **Cobertura de glifos:** o design usa `×`, `·`, `→`, `★`, `☆`, `ã é ê ç ó`. Um teste (T-20.4) verifica o `cmap` de cada fonte contra todas as strings de `Strings.cs` (SDD-15 RF-15.11 e SDD-27). Glifo ausente → *fallback* para JetBrains Mono; se também ausente, ver Decisão pendente DP-20.2.
8. Proibido `string.Format` e interpolação no render (SDD-08 RF-08.6 permanece).

### RF-20.4: Espaçamento, raio, sombra dura e alvos de clique

**Escala de espaçamento (DIP), exatamente as usadas:** `2, 3, 4, 6, 8, 10, 12, 14, 16, 22, 24`. Mapeamento semântico:

| Token | DIP | Uso |
|---|---|---|
| `esp-2` | 2 | gap entre botões do nav; gap vertical em botões de duas linhas |
| `esp-4` | 4 | gap pequeno em colunas de texto |
| `esp-6` | 6 | gap em grades de 3 faixas; gap entre blocos de texto de 2 linhas |
| `esp-8` | 8 | gap em grades 2×2; padding horizontal do cabeçalho do painel (lado do botão fechar) |
| `esp-10` | 10 | gap entre cards no corpo do painel |
| `esp-12` | 12 | padding de linha e tile; gap horizontal em linhas |
| `esp-14` | 14 | padding de card de destaque; padding vertical do corpo do painel |
| `esp-16` | 16 | padding horizontal do corpo do painel e do cabeçalho; padding do card de pergunta |
| `esp-22` | 22 | padding do card "Bem-vindo de volta" |

**Raio:** `0` em painéis, cards, tiles, botões de ação, barras e selos (visual "pixel"). `4` em botões do nav e ícones de apps (e no fundo do widget, SDD-23). `2` em barras de indicador de ativo do nav (16×3) e células da planilha de exemplo (não de jogo). `7` (círculo de 14 DIP) no badge. Nenhum outro raio.

**Sombra dura:** `6px 6px 0 <sombra>` do design = **um retângulo preenchido deslocado `(+6,+6)` DIP**, desenhado antes do corpo, sem blur e sem `SKImageFilter`. Tamanho físico `round(6 × dpi/96)`. Cor: `sombra-painel` (painel) e `sombra-card` (cards). A janela layered precisa de **margem de 6 DIP à direita e abaixo** para conter a sombra (a caixa visível do painel não muda).

**Bordas:** `border: 2px solid` do painel e dos cards = retângulo preenchido na cor da borda e, dentro, o corpo recuado em 2 DIP (sem AA). Bordas de 1 DIP idem.

**Alvo mínimo de clique:** **44×44 DIP** em todo botão e toda opção (valor do design: botão fechar 44×44, nav 44×44, MELHORAR `min-width 84` × 44, COLETAR `min-width 72` × 44, linha de fase `min-height 44`, opção de resposta `min-height 44`, MIGRAR 48, "COLETAR ×2" 52, "COLETAR +X" 44, botões de pico/chefe 36). **Exceção do design:** os botões de pico e de chefe na faixa têm 36 DIP de altura; a área de clique é **estendida** para 44 DIP na vertical (hit-test, sem mudar o desenho). A caixa de suprimentos (52×44) cumpre. Elementos puramente informativos (selos, badges) não são alvo de clique.

### RF-20.5: Catálogo de ícones vetoriais

Todos em `viewBox="0 0 16 16"`, `fill="none"`, traço `currentColor`, `stroke-linecap` e `stroke-linejoin` padrão SVG (**butt** e **miter**, limite de miter 4).

| Ícone | `d` (path data exato) | Tamanho (DIP) | Largura de traço (unidades do viewBox) | Onde |
|---|---|---|---|---|
| `arsenal` | `M5 14h6M6 14V7h4v7M4 7h8l-1-3H5zM8 1v3` | 18 | 1,5 | nav |
| `fases` | `M4 15V2M4 2h8l-2 3 2 3H4` | 18 | 1,5 | nav |
| `missoes` | `M2 4l2 2 3-3M2 11l2 2 3-3M9 5h5M9 12h5` | 18 | 1,5 | nav |
| `migrar` | `M4.5 12H4a3 3 0 0 1-.3-6A4.5 4.5 0 0 1 12.4 6 3 3 0 0 1 12 12h-.5M8 14V8M5.5 10.5 8 8l2.5 2.5` | 18 | 1,5 | nav |
| `fechar` | `M3 3l10 10M13 3 3 13` | 16 | 1,8 | cabeçalho do painel |
| `seta-pico` | `M8 13V3M3 8l5-5 5 5` | 16 | 2 | botão de pico |
| `alerta` | `M8 2 1.5 14h13zM8 6.5v3.5M8 12v.5` | 20 | 1,6 | aviso de perdas (cor `perigo-claro`, fixa) |

Cor: `currentColor` do design = `txt-principal` no nav (`#e8f0ff`), `txt-rotulo` no `fechar`, `bg-trilho-fundo` sobre `ambar` na seta de pico (herda a cor do texto do botão), `perigo-claro` no alerta.

**Conversão para `SKPath` em tempo de carga (uma vez):**

1. `SKPath.ParseSvgPathData(d)` na carga do `IconCatalog`; erro → exceção de inicialização (falha cedo; T-20.2).
2. Escala do ícone: `k = sizeDip/16 × dpi/96`; `strokeWidthPx = strokeUnits × k`.
3. **Rasterizar para cache:** por (ícone, escala), desenhar o path com `SKPaint { Style = Stroke, StrokeWidth, StrokeCap = Butt, StrokeJoin = Miter, StrokeMiter = 4, IsAntialias = true }` em um `SKBitmap` **Alpha8** de `round(size × scale)` px (uma vez; refeito em `WM_DPICHANGED`, junto ao cache de SDD-09). Em render, `DrawBitmap` com `SKPaint.Color` = cor atual (máscara Alpha8 tingida). Nenhum `SKPath` é desenhado por frame.
4. Antialias é **ligado só na rasterização do cache** (os ícones têm diagonais e curvas). Isto estende SDD-08 RF-08.8 item 4 (que lista AA só para anéis, elipses e texto): o custo é de carga, não de frame.
5. Posição: centro do ícone no centro do botão, em pixel inteiro (arredondar `x`, `y`).

### RF-20.6: Catálogo de animações

Origem: `Strip.dc.html` (`@keyframes` e classes). Tempo contínuo = relógio de apresentação (SDD-08 RF-08.5; para quando `PauseReasons != None`). Nenhuma animação aloca; todas são funções puras do tempo.

| Nome | Duração / curva | Valores | Alvo | fps alvo (Normal) | `reducedMotion` | Saver |
|---|---|---|---|---|---|---|
| `adMarch` | 26 s, linear, infinita | `translateX 0 → −1180 DIP` (≈ 45,4 DIP/s); cada inimigo tem `animation-delay = −i × 26/n` s (n = quantidade) | inimigos na cena | 30 (ADR-004) | movimento só nos ticks do modelo, sem render intermediário (DP-20.3) | 15 fps |
| `adRise` | 1,4 s, ease-out, infinita no design | `translateY 0 → −16 DIP`, `opacity 1 → 0` | números flutuantes ("-96", "-240", "LENTO") | 30 | número parado em `opacity 1` por 1,4 s e some (sem subida) | número parado e some, **sem animação** |
| `adBlink` | 1,1 s, `steps(2)`, infinita | `opacity 1 → .45 → 1` em 0 %, 50 %, 100 % | caixa de suprimentos | 4 mudanças a cada 1,1 s (ver nota 1) | opacidade fixa `1` | opacidade fixa `1` |
| `adPulse` | 1,2 s, ease-in-out, infinita | anel `0 0 0 0 rgba(245,179,53,.7)` → `0 0 0 6px rgba(245,179,53,0)` em 50 % → volta | botões de pico e de chefe | 15 (limite de SDD-22 RF-22.10, para baixo consumo) | anel fixo de 2 DIP em `rgba(245,179,53,.7)` (proposta, DP-20.4) | anel fixo, igual a `reducedMotion` |
| `adBob` | 0,5 s, `steps(2)`, infinita | `translateY 0 → −2 DIP → 0` | sprite de cada inimigo | 4 posições a cada 0,5 s (ver nota 2) | `0` (parado) | `0` (parado) |

**Nota 1 (`adBlink`, `steps(2)` por intervalo de keyframe):** equivale a quatro estados de 275 ms: opacidade `1`, `0,725`, `0,45`, `0,725`. **Cálculo do autor desta ficha a partir da semântica CSS (`jump-end`); validar contra o artefato em MTC-20.2.**

**Nota 2 (`adBob`):** quatro estados de 125 ms: `0`, `−1`, `−2`, `−1` DIP. Mesma ressalva. Em escalas > 100 %, o deslocamento em px físicos é `round(−1 × scale)` etc.

**Nota 3 (anel `adPulse` sem blur):** o anel é um traço (stroke) retangular ao redor do botão com largura `spread(t)` e alfa `a(t)`, sem `SKMaskFilter`. `spread(t)` e `a(t)` interpolam linearmente (aproximação de ease-in-out com a curva `t² (3 − 2t)`) entre `(0 DIP, .7)` e `(6 DIP, 0)` na primeira metade e voltam na segunda. Custo: 1 retângulo por frame, só enquanto o botão existe.

**Nota 4 (cor do anel em chefe):** o CSS do design aplica **o mesmo** `adPulse` âmbar ao botão de chefe (que é `perigo`). Transcrito literal; ver DP-20.5.

**Regras gerais:**

- Frames só são pedidos enquanto existe animação **visível** (SDD-10 `RenderPolicy`: `animationsEnabled = !reducedMotion && !Saver`). Sem animação ativa (por exemplo, ausente com cena esmaecida e `reducedMotion`) o contador de Presents é 0 (SDD-08 CA-08.4).
- Animação ambiente = `adBlink`, `adPulse`, `adBob`. `adBlink` e `adPulse` só existem com o alvo visível (caixa, botões de pico e chefe), e **esses alvos não existem no estado ausente** (design: só aparecem em presente, pico e chefe). `adMarch`, `adRise` **e `adBob`** continuam no estado ausente (cena a 50% de opacidade, 8 fps: o passo de 125 ms do bob coincide com o frame, sem frame extra); o design (`Strip.dc.html`) não desliga o bob em ausente. Alinhado a SDD-22 RF-22.10.
- Ao ocultar o overlay (SDD-07) todas param (nenhum timer).

### RF-20.7: Mapeamento de estados de borda e de presença

| Estado do jogo | Borda superior da faixa (2 DIP) | Selo do widget | Cena | Elementos extras |
|---|---|---|---|---|
| Presente | `borda-presente` `#f5b335` | `PRESENTE ×2,0` em `selo-presente` | opacidade 1 | caixa de suprimentos clicável |
| Pico de demanda | `#f5b335` | `PRESENTE …` | opacidade 1; fila com 13 inimigos no design | botão `ambar` "PICO DE DEMANDA · ESCALAR ×3" + timer em chip `bg-trilho-fundo`/`ambar` |
| Chefe | `borda-chefe` `#ff5a7a` | `PRESENTE …` | opacidade 1 | sprite do chefe com barra `perigo`/`perigo-trilho`; botão `perigo` "CHEFE · RESPONDER PERGUNTA AZURE" + timer em chip `perigo-tinta`/`perigo-claro` |
| Ausente | `borda-ausente` `#3a4558` | `AUSENTE ×0,25` em `selo-ausente`; taxa `+39/s` | **opacidade 0,5** | selo "MODO GERENCIADO · AUTO-ESCALA ATIVA" |

Valores de multiplicador e de taxa (`×2,0`, `×0,25`, `+312/s`, `+39/s`) são **exemplos do design**; as regras de cálculo são de SDD-25/26 e o formato pt-BR (vírgula decimal, ADR-029) é de SDD-27. O design usa ponto decimal em alguns desses exemplos (`×2.0`); vale a vírgula (ADR-029).

### RF-20.8: Contraste

- **Mínimo 4,5:1** para texto e ícones informativos; **3:1** para bordas e elementos gráficos de estado.
- O `Palette` expõe um teste (T-20.3) que recalcula a razão WCAG 2.x (luminância relativa sRGB) dos pares abaixo e falha se algum cair abaixo do mínimo.
- Pares verificados (cálculo do autor desta ficha, a reconferir no teste):

| Texto / fundo | Razão |
|---|---|
| `txt-principal` `#e8f0ff` / `bg-painel` `#0b1a2e` | 15,26 |
| `ambar` / `bg-painel` | 9,47 |
| `menta` / `bg-painel` | 12,11 |
| `txt-secundario` `#b8c8e0` / `bg-item` `#0d1f37` | 9,76 |
| `txt-secundario` / `bg-card` `#0f2440` | 9,19 |
| `txt-terciario` `#9fb2cc` / `bg-card` | 7,21 |
| `txt-rotulo` `#8aa0c0` / `bg-item` | 6,21 |
| `txt-rotulo` / `bg-painel` | 6,55 |
| `txt-rotulo` / `bg-card` | 5,84 |
| `txt-rotulo` / `bg-trilho` `#16304d` | 5,03 |
| `txt-widget-rotulo` `#a9b6c8` / `bg-painel` | 8,50 |
| `azul-claro` `#9fd3ff` / `bg-card` | 9,83 |
| `perigo-claro` `#ff9bb0` / `bg-painel` | 8,80 |
| `perigo-texto` `#ffd0da` / `perigo-fundo` `#2a1420` | 12,54 |
| `perigo-claro` / `perigo-fundo` | 8,67 |
| `bg-trilho-fundo` `#071323` / `ambar` | 10,10 |
| `bg-trilho-fundo` / `menta` | 12,91 |
| `menta-texto` `#b8d8cc` / `menta-fundo` `#0f2a2a` | 9,92 |
| `menta-texto` / `menta-faixa` `#12403a` | 7,54 |
| `menta` / `menta-fundo` | 10,50 |
| `perigo-tinta` `#1a0610` / `perigo` `#ff5a7a` | 6,49 |
| `perigo-claro` / `perigo-tinta` | 9,81 |
| `ambar` / `bg-trilho-fundo` (chip do timer de pico) | 10,10 |
| `prestigio-tinta` `#0b0720` / `prestigio` `#8f74ff` | 5,73 |
| `prestigio-claro` `#c9b8ff` / `prestigio-fundo` `#1a1636` | 9,72 |
| `prestigio-texto` `#cfc6ee` / `prestigio-fundo` | 10,70 |
| `txt-botao-neutro` `#c9d6ea` / `bg-painel` | 11,89 |
| `txt-principal` / `bg-item` | 14,46 |
| `txt-principal` / `bg-card` | 13,62 |
| `txt-principal` / `menta-fundo` | 13,24 |
| `txt-secundario` / `bg-ausente` `#0d1c30` | 10,10 |
| `txt-rotulo` / `bg-ausente` | 6,42 |
| `txt-widget-rotulo` / `bg-ausente` | 8,33 |
| `perigo` `#ff5a7a` / `bg-painel` (borda e texto de estado, ≥ 3:1) | 5,82 |
| `azul` `#3aa0ff` / `bg-trilho` (barra sobre trilho, ≥ 3:1) | 4,89 |
| `ambar` / `bg-trilho` (barra de missão, ≥ 3:1) | 7,28 |

- **Widget com alfa 230:** o fundo efetivo varia com o que está embaixo (sobre branco `#233042`, sobre preto `#0a1729`). Pior caso (sobre branco): `ambar` 7,24; `menta` 9,25; `txt-widget-rotulo` 6,49; `txt-principal` 11,66. Todos ≥ 4,5.
- **Estados desabilitados (opacidade do design):** linha de torre bloqueada (opacidade 0,45) e linhas de fase bloqueadas (0,55) ficam **abaixo de 4,5:1** (por exemplo, rótulo do botão bloqueado 2,10; "BLOQ." 3,54; título da fase bloqueada 5,25). Componentes inativos são isentos de contraste mínimo na WCAG 2.x, e o design os quer apagados. Registrado como DP-20.6 para decisão do usuário.
- **Par reprovado:** `bg-trilho-fundo` `#071323` / `menta-faixa` `#12403a` = 1,62. **Não é usado** no design (a faixa ativa do bônus é `#5cf0b0` com texto `#071323`). T-20.3 inclui este par como "proibido" para evitar uso futuro.

### RF-20.9: Formatação de números e textos

- Todo número, unidade, multiplicador e texto visível vem de SDD-27 (pt-BR) e de `Strings.cs` (SDD-15 RF-15.11). Esta ficha **não define formatação**.
- Conflito do design resolvido por ADR-029: o design mistura vírgula (`1,2K`, `0,7s`, `Cad 2,1s`) e ponto (`×2.0`, `×0.25`, `+4.1K`, `48.2K`); no jogo vale **sempre a vírgula** (`×2,0`, `×0,25`, `+4,1K`, `48,2K`). DP-20.7 resolvida.

## 6. Requisitos não funcionais

- **RNF-20.1:** Carregar fontes, `Palette`, `IconCatalog` e rasterizar os 7 ícones leva ≤ 50 ms no início e ≤ 20 ms em `WM_DPICHANGED` (meta **não comprovada**).
- **RNF-20.2:** Memória do `IconCatalog` ≤ 100 KB em 4K a 200 % (7 bitmaps Alpha8 de ≤ 40×40 px) (meta **não comprovada**).
- **RNF-20.3:** Zero alocação gerenciada por frame no uso de tokens, fontes e ícones (`SKColor`, `SKPaint`, `SKFont`, `SKBitmap` pré-criados; SDD-08 RF-08.8).
- **RNF-20.4:** Com `reducedMotion` ligado, **nenhuma** das cinco animações pede frame (contador de Presents 0 em 30 s, exceto eventos de dados; meta **não comprovada**).
- **RNF-20.5:** Nenhum acesso à rede para fontes (ADR-014).

## 7. Estratégia de implementação

1. `Palette` (Rendering): `static readonly SKColor`, mais `Contrast.Ratio` (puro, Runtime) e `Hover(color)` (× 1,08 por canal, com teto 255) calculado na inicialização.
2. `Typography` (Rendering): `SKTypeface`, `SKFont` e `SKPaint` pré-criados por (papel, tamanho DIP, escala), reconstruídos em `WM_DPICHANGED`; `TextCache` aplica as regras do RF-20.3.
3. `IconCatalog` (Rendering): tabela de `d`, `ParseSvgPathData` e `SKBitmap` Alpha8 por escala.
4. `Motion` (Runtime, puro): funções `March(t, i, n)`, `Rise(t)`, `Blink(t)`, `PulseSpread(t)`, `Bob(t)` com `reducedMotion` e `Saver` por parâmetro; testáveis em Linux.
5. `HardShadow.Draw(canvas, rect, color)` (retângulo deslocado, sem blur).
6. Testes de imagem (golden, sem texto; texto só por bounding box, SDD-08 seção 11).

## 8. Dependências

SDD-08 (pipeline Skia e regras de custo), SDD-09 (cache de sprites, fonte dos `.ttf`). Consumidores: SDD-22, 23, 24. Referências sem dependência: SDD-21 (geometria), SDD-27 (formato pt-BR), SDD-25/26 (comportamento).

## 9. Critérios de aceitação

- **CA-20.1:** Todo token da seção RF-20.1 existe em `Palette` com o hex exato; nenhum literal de cor fora de `Palette` (T-20.1).
- **CA-20.2:** Os 7 ícones passam por `ParseSvgPathData` sem erro e renderizam idênticos (≤ 0,5 % de pixels com ΔE > 8) a um PNG de referência feito do SVG original (T-20.2, MTC-20.1).
- **CA-20.3:** Todo par da tabela RF-20.8 tem razão ≥ 4,5 (texto) ou ≥ 3 (borda e gráfico); o par proibido é rejeitado (T-20.3).
- **CA-20.4:** Silkscreen é desenhada com `Edging = Alias` e `Subpixel = false`, e o tamanho físico é inteiro para 100, 125, 150, 175 e 200 % (T-20.5).
- **CA-20.5:** Todas as strings de `Strings.cs` têm glifos nas fontes ou fallback documentado (T-20.4).
- **CA-20.6:** Com `reducedMotion`, as cinco animações não pedem frame (T-20.6). No Saver, `adBlink`, `adPulse` e `adBob` ficam estáticos e `adMarch` roda a ≤ 15 fps.
- **CA-20.7:** Sombra dura é um retângulo deslocado (+6,+6) DIP sem uso de `SKImageFilter` e `SKMaskFilter` (inspeção do código e T-20.7).

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-20.1 | Varredura de literais de cor fora de `Palette`; `Palette` contém todos os hex da tabela | Linux |
| T-20.2 | `IconCatalog`: parse dos 7 paths e golden de cada ícone em 100/150/200 % | Linux + Windows |
| T-20.3 | `Contrast.Ratio` dos pares de RF-20.8 (+ par proibido) | Linux |
| T-20.4 | Cobertura de glifos: `cmap` × strings de `Strings.cs` | Linux |
| T-20.5 | Tamanho físico inteiro de Silkscreen e flags de `SKFont` em 100–200 % | Linux |
| T-20.6 | `Motion`: `reducedMotion` e `Saver` por tabela; contagem de pedidos de frame | Linux |
| T-20.7 | `HardShadow`: retângulo esperado por escala; ausência de filtros | Linux |
| T-20.8 | Sequências de `adBlink` e `adBob` conforme notas 1 e 2 (valores por instante) | Linux |
| MTC-20.1 | Comparar ícones e tipografia no Windows lado a lado com o artefato de design (100/150/200 %) | Manual |
| MTC-20.2 | Comparar o ritmo de `adBlink`, `adBob`, `adPulse` e `adRise` com o artefato de design | Manual |
| MTC-20.3 | Legibilidade de Silkscreen 9–11 DIP em 100 % no monitor de referência (D-05) | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Silkscreen sem AA em tamanhos fora da grade da fonte (9, 11, 13, 15 DIP) ficar com pixels desiguais | Média | Médio | MTC-20.3; se reprovar, ADR de "ajuste de tamanhos à grade" (DP-20.1) |
| Glifos ausentes (`★ ☆ → ×`) na Silkscreen | Média | Médio | T-20.4; fallback JetBrains Mono; estrelas como ícone vetorial (DP-20.2) |
| Renderização de texto diferente entre Linux e Windows quebrar golden | Alta | Baixo | Golden só sem texto (SDD-08 seção 11) |
| Escalas fracionárias (125 %, 175 %) deixarem traços de ícone com 1,5 px borrados | Média | Baixo | Rasterizar na escala exata e alinhar o centro ao pixel; MTC-20.1 |
| `steps()` interpretado de outro modo que o assumido (notas 1 e 2) | Média | Baixo | MTC-20.2; ajustar `Motion` |
| Arquivos `.ttf` não entregues pelo usuário (D-03) | Média | Alto | Bloquear E4 e reportar; sem fallback de rede (ADR-014) |
| Muitos tokens duplicarem hex (manutenção) | Baixa | Baixo | T-20.1 |

### Decisões pendentes (lacunas reais do design ou das ADRs)

| ID | Questão | Proposta desta ficha | Bloqueia |
|---|---|---|---|
| DP-20.1 | O design usa Silkscreen em 9, 11, 13 e 15 DIP; a fonte é desenhada sobre uma grade de pixel e tamanhos fora dela podem ficar irregulares sem AA | Manter os tamanhos do design e validar em MTC-20.3 | acabamento de E4 |
| DP-20.2 | Estrelas `★☆` e setas `→` podem não existir na Silkscreen | Fallback de fonte; se ruim, desenhar estrelas como `SKPath` (path data a criar; **não existe no design**) | SDD-24 (lista de fases) |
| DP-20.3 | Comportamento da `adMarch` em `reducedMotion`/Saver: no idle a marcha é o próprio jogo | Posição vem do modelo; `reducedMotion` atualiza só por tick do modelo (sem render intermediário); Saver 15 fps. Decidir com SDD-10/22 | SDD-22 |
| DP-20.4 | Aparência estática de `adPulse` sob `reducedMotion` | Anel fixo de 2 DIP em `ambar-anel` | SDD-22 |
| DP-20.5 | O design pulsa o botão de chefe (rosa) com anel **âmbar** | Transcrito literal; sugestão: anel na cor do botão | SDD-22 |
| DP-20.6 | Estados desabilitados com opacidade 0,45 e 0,55 ficam abaixo de 4,5:1 | Manter (inativos isentos); alternativa: subir para ≥ 0,7 | SDD-24 |
| DP-20.7 | ~~Ponto decimal em `×2.0`, `×0.25`, `+4.1K`, `48.2K` vs vírgula em `1,2K`, `0,7s`~~ **Resolvida por ADR-029:** vírgula em todo lugar | n/a | n/a |

Itens do design **ausentes** (não inventados aqui): estados hover e pressionado dos botões do painel (o design só tem `brightness(1.08)` em botões de ação e `hover-nav` no nav), estado desabilitado detalhado de botões, foco por teclado (o jogo não usa teclado).
