# SDD-08: Renderização (Skia → DIB → janela layered)

**Status:** Rascunho para revisão · **Etapa:** E1 (superfície + quadro de teste) e E4 (cena completa) · **Depende de:** SDD-03, 04, 05, 20, 21, 22 · **Bloqueia:** SDD-09, 15, 16, 22, 23, 24

> **Revisada (rev2, ADR-018 a ADR-030).** O pipeline (`LayeredSurface`, regras de custo, invalidação) foi mantido e generalizado para **várias janelas** (uma superfície por janela). A cena passa a ser a faixa de 96 DIP de SDD-22 (sem estrada, sem HUD lateral, sem projéteis nem efeitos de impacto, ADR-027). Os trechos substituídos têm nota "Substituído por" logo abaixo do título; o texto original foi mantido como histórico.

## 1. Identificador e título

SDD-08: Pipeline de renderização, camadas, composição de frame, apresentação e regras de custo.

## 2. Objetivo

Desenhar a cena (faixa de 96 DIP, SDD-22), o widget HUD (SDD-23) e as janelas layered do painel, dos cards, dos banners, das caixas e do selo de ausente (ADR-021) com Skia raster direto na memória de um DIB por janela e apresentar via `UpdateLayeredWindowIndirect`. O custo de CPU deve ser previsível, sem alocação em regime e com zero frames quando nada muda (painel, cards e widget renderizam **por evento**).

## 3. Contexto técnico

- ADR-001 (layered + Skia raster), ADR-009 (HUD em Skia), ADR-013 (efeitos fora da simulação), ADR-018 (design oficial, SDD-20), ADR-021 (painel, cards, banners, caixas e selo são janelas layered Skia `WS_EX_NOACTIVATE`; WPF só em Configurações e Sobre), ADR-027 (acerto instantâneo: só números de dano flutuantes) e ADR-029 (formato numérico pt-BR).
- Ordem de grandeza: a faixa é de 96 DIP × largura da área de trabalho. Em 1920×1080 a 100% ≈ 1920×96 px ≈ 0,18 Mpx (0,74 MB BGRA); em 4K a 150% ≈ 3840×144 px ≈ 0,55 Mpx (2,2 MB). A tabela completa está em SDD-22 seção 6. (Os valores antigos da faixa M de 176 a 264 px de altura deixam de valer.)
- Skia raster: `SKSurface.Create(SKImageInfo(w, h, Bgra8888, Premul), pixelsPtr, rowBytes)` desenha direto nos bits do DIB, sem cópia intermediária.

## 4. Escopo e exclusões

**Escopo:** `LayeredSurface` (Platform; **uma instância por janela**), `FrameComposer`, `StaticLayer`, `SceneRenderer` (substitui `FieldRenderer`; SDD-22), números de dano flutuantes (substituem o `EffectSystem`; SDD-22 RF-22.5), `TextCache`, invalidação ("dirty"), retângulo sujo e regras de alocação. O conteúdo do widget HUD é de SDD-23; o do painel e dos cards, de SDD-24.

**Exclusões:** carregamento e escala de sprites (SDD-09), tokens de cor e tipografia (SDD-20), layout e conteúdo do widget e do painel (SDD-23, SDD-24), política de fps (SDD-10, SDD-22) e metas numéricas (SDD-16).

## 5. Requisitos funcionais

### RF-08.1: `LayeredSurface` (Platform.Windows, implementa `IOverlaySurface`)

- `Resize(PixelSize)`:
  - libera os anteriores;
  - `CreateCompatibleDC(null)`;
  - `CreateDIBSection` (32 bpp, `BI_RGB`, altura **negativa** para top-down);
  - `SelectObject`;
  - cria a `SKSurface` sobre `ppvBits` com `rowBytes = width*4`.
- `BeginFrame()` devolve o `SKCanvas`. O conteúdo anterior **é mantido** (para atualização parcial, RF-08.7).
- `EndFrame(PixelRect? dirty)`:
  - `canvas.Flush()`, `GdiFlush()`;
  - `UpdateLayeredWindowIndirect` com `UPDATELAYEREDWINDOWINFO { cbSize, hdcDst=null, pptDst=posição, psize, hdcSrc=memDC, pptSrc=(0,0), crKey=0, pblend={AC_SRC_OVER,0,constAlpha,AC_SRC_ALPHA}, dwFlags=ULW_ALPHA, prcDirty=dirty? }`;
  - se a chamada falhar com `prcDirty`, repetir sem `prcDirty` e registrar no log uma vez.
- `SetOpacity(byte)`: o mesmo Update, sem redesenho e sem `prcDirty`.
- `Release()` (chamado por `ReleaseCaches`, SDD-07): libera `SKSurface`, DIB e DC. O próximo `BeginFrame` recria.
- **Uma superfície por janela.** A mesma classe serve à cena, ao widget HUD, ao selo de ausente, aos banners, às caixas, ao painel e aos cards, cada um com a sua instância (DIB, DC e `SKSurface` próprios, tamanho da própria janela) e o seu `SourceConstantAlpha`. Superfícies de janelas ocultas são liberadas com `Release()` (RF-08.10). A HandleWindow deixou de existir (ADR-028; o widget HUD a substitui).

### RF-08.2: Camadas

> **Substituído por SDD-22 RF-22.4 (ADR-018, ADR-027).** A cena tem camada estática (borda, núcleo, torres, slots, portal) e dinâmica (inimigos, números, chefe); não há estrada, HUD no campo, projéteis nem anéis de alcance. O HUD vive no widget (SDD-23) e o painel/cards em janelas próprias (SDD-24). A tabela abaixo é histórico.

| Camada | Conteúdo | Quando redesenha | Armazenamento |
|---|---|---|---|
| **Static** | Fundo da faixa (cor + alfa), estrada (bordas, faixa central), decoração fixa, portal, base sem o estado de HP | Mudança de layout, escala, tema ou `backgroundAlpha` | `SKImage` raster pré-renderizado do tamanho do `FieldRect` |
| **Dynamic** | Torres, inimigos, projéteis, efeitos, barras de vida, anéis de alcance, arraste fantasma | Todo frame com mudança | Desenhado direto na superfície |
| **HUD** | Painel esquerdo e mini-painel da torre selecionada | `HudModel.Version` mudou | `SKImage` cacheado do `HudRect`, redesenhado só ao mudar |
| **Overlay** | Selos ("PAUSADO", "AUSENTE"), aviso de chefe, diagnóstico | Quando ativo | Direto |

### RF-08.3: Ordem de composição (`FrameComposer.Compose`)

> **Substituído por SDD-22 RF-22.4 (ADR-018, ADR-027).** Ordem nova da cena, de baixo para cima: linha de borda, núcleo e barra, slots/torres, inimigos, números de dano, portal, chefe e barra do chefe. Os passos abaixo (anéis, projéteis, efeitos, arraste, HUD e mini-painel) são histórico.

1. `canvas.Clear(SKColors.Transparent)` **somente** no retângulo sujo, ou na superfície toda se o frame for completo.
2. `DrawImage(staticLayer, FieldRect.Origin)`.
3. Anéis de alcance (torre selecionada, ou o arraste) usando o mesmo `SKPaint` de traço tracejado pré-criado.
4. Entidades ordenadas por y (painter): torres, inimigos, réplica (se houver módulo). A ordenação usa um **buffer de índices reutilizável** (`int[]` de capacidade 128) com ordenação por inserção, que é estável e O(n) quando já quase ordenado.
5. Projéteis.
6. Efeitos (`EffectSystem`).
7. Barras de vida (por cima de tudo, legíveis).
8. Arraste fantasma.
9. `DrawImage(hudImage, HudRect.Origin)`.
10. Mini-painel da torre selecionada (parte da HUD, posicionado no campo).
11. Camada Overlay.

A transformação do campo é aplicada uma vez: `canvas.SetMatrix(SKMatrix.CreateScaleTranslation(renderScale, renderScale, FieldRect.X, FieldRect.Y))` e, depois, `ResetMatrix` para a HUD.

### RF-08.4: Desenho de entidades (`FieldRenderer`)

> **Substituído por SDD-22 RF-22.5 (ADR-018).** Um sprite por torre e por inimigo, alinhado à base (y = 96), sem sombra, anel, rotação nem espelhamento; barra de vida só no chefe (e a do núcleo); bob global `0, −1, −2, −1` DIP. O texto abaixo é histórico.

- **Torre:** sprite do cache escalado (SDD-09) com a variante `a` se `sin(aim) < −0,15` (costas) ou `b` (frente), espelhado horizontalmente se `cos(aim) < 0`, ancorado pelos pés (`pivot` do manifesto). Selo de nível "LV n" em `TextCache`. Contorno de seleção.
- **Inimigo:**
  - sombra elíptica (alfa 0,42);
  - anel de tipo (cores: swarm `#ff7ad9`, armored `#c9d4de` com traço de 4 px, runner `#7ff8e6`);
  - anel tracejado se `chilled`;
  - sprite espelhado conforme a direção (`cos(heading) > 0,1` → espelha);
  - **bob** `sin(t·9 + id·1,7)·1,5` unidades (drone/runner: `sin(t·4 + id·1,7)·3` e elevação de 10 unidades); bob é desligado com `reducedMotion`.
- **Barra de vida:** largura `clamp(0,8·larguraSprite, 22, 44)` unidades, altura 4. Cores: > 50% `#5cf0a8`, > 25% `#f0ca79`, caso contrário `#ff6a7e`. Fundo `#3a1a26` e moldura `rgba(5,10,20,.92)`.
- **Projétil:** sprite rotacionado pelo ângulo até o alvo. Sem alvo, não desenha.
- **Base:** sprite da base e uma barra "NÚCLEO" na HUD (não no campo, para economizar espaço). No campo fica apenas uma luz piscante (`floor(t·1,6) % 2`), vermelha quando HP < 40%.
- **Portal:** sprite estático na camada Static e brilho opcional animado (desligado em `reducedMotion`).

### RF-08.5: Efeitos visuais (`EffectSystem`, Rendering)

> **Substituído por SDD-22 RF-22.5 e SDD-20 RF-20.6 (ADR-027).** Acerto instantâneo: sem projéteis voando e sem efeitos de impacto; só **números de dano flutuantes** (pool de 24, sobem 16 DIP em 1,4 s). Pico, caixa e chefe são janelas (SDD-22 RF-22.8). O texto abaixo é histórico.

- Entrada: eventos da simulação (`SimEventBuffer`, SDD-11), lidos após cada tick.
- Pool fixo de **128** efeitos (struct). Se o pool estiver cheio, o efeito mais antigo é substituído.

| Evento | Efeito | TTL | Desenho |
|---|---|---|---|
| `Hit(bolt)` | `fx-impact` | 0,35 s | escala `0,6 + 0,5·(1−vida)`, alfa `min(1, vida·1,8)` |
| `Hit(frost)` | `fx-frost` | 0,35 s | idem |
| `Hit(cannon)` / `Splash` | `fx-explosion` | 0,35 s | idem, base 1,6× |
| `SuperHit` | texto "×2" âmbar e 4 faíscas | 0,55 s | sobe 14 unidades (parado em `reducedMotion`) |
| `TowerBuilt` / `TowerUpgraded` | anel expansivo (ciano/âmbar) e partículas; "▲" no upgrade | 0,55 / 0,65 s | — |
| `Leak` | flash vermelho na base | 0,6 s | retângulo alfa ≤ 0,7 |
| `Kill` | nenhum efeito extra no MVP | — | — |

- Os efeitos avançam com o **relógio de apresentação**, que para quando `PauseReasons != None`. Efeitos não são salvos.

### RF-08.6: Texto (`TextCache`)

> **Substituído por SDD-20 RF-20.2 e RF-20.3 (ADR-025) na parte de fontes.** Silkscreen (títulos, números, botões, selos) e JetBrains Mono (corpo) substituem Pixelify Sans e IBM Plex. Silkscreen é desenhada sem antialias, com tamanho físico inteiro e posição em pixel inteiro (SDD-20 RF-20.3).

- Fontes embutidas como recurso: **Silkscreen** (Regular e Bold) e **JetBrains Mono** (Regular e SemiBold), carregadas uma vez com `SKTypeface.FromStream`. Arquivos `.ttf` em `assets/fonts` com licença OFL, fornecidos pelo usuário (D-03; SDD-09 RF-09.7).
- Rótulos estáticos ("ONDA", nomes, "+" do slot) viram `SKTextBlob` pré-criados por escala; o rótulo "LV n" (níveis ilimitados, SDD-27) vem do cache LRU abaixo.
- Números dinâmicos (créditos, onda, nível, dano): formatar em pt-BR com vírgula decimal (`CreditFormatter`, SDD-27 RF-27.2; ADR-029) em `Span<char>` e desenhar com `canvas.DrawText(ReadOnlySpan<char>, …)` (sem alocar string) ou com um cache LRU de 64 blobs por valor.
- Proibido `string.Format` ou interpolação no caminho de render.

### RF-08.7: Invalidação e retângulo sujo

- `RenderInvalidation` mantém `bool fullFrame` e `PixelRect dirtyUnion`.
- **Frame completo** em: mudança de layout (inclui `TaskbarLayoutChanged`, SDD-21), primeiro frame após Show, mudança de estado da linha de borda (presença, chefe) e quando há mais de 24 entidades em movimento. Nesse último caso, o custo de calcular a união supera o ganho.
- **Cena (SDD-22 RNF-22.3):** como as entidades ocupam a altura inteira da faixa, a granularidade é **horizontal**: lista de bandas verticais (X em px, alinhadas a 4, expandidas 2 px, fundidas se distam < 16 px); `Present` usa a união e passa a frame completo se ela cobrir > 60% da largura. Continua atrás de `RenderOptions.PartialUpdates`.
- **Frame parcial** nos demais casos:
  - união dos retângulos (atual ∪ anterior) de cada entidade, efeito e barra que mudou;
  - (o widget HUD tem superfície e janela próprias, SDD-23; não há mais `HudRect` na cena);
  - expandida em 2 px e alinhada a múltiplos de 4.
  - Cada `IRenderable` dinâmico guarda o `lastBounds` em pixels.
- **Ignorar o frame** (sem `UpdateLayeredWindow`) se `!fullFrame && dirtyUnion.IsEmpty`. Cada janela tem a **sua** `RenderInvalidation` (RF-08.10).
- No MVP técnico (E1), a atualização parcial pode ficar **desligada** por configuração interna (`RenderOptions.PartialUpdates=false`). Ela será ligada na E7 somente se o benchmark mostrar ganho ≥ 20% de CPU no cenário `strip-present` (SDD-22 RNF-22.3; SDD-16).

### RF-08.8: Regras de custo (obrigatórias no código de render)

1. Zero alocação gerenciada por frame em regime (`SKPaint`, `SKFont`, `SKPath` e `SKRect` pré-criados ou `stackalloc`; sem LINQ, `foreach` sobre `List<T>` só com struct enumerator, sem closures).
2. `SKSamplingOptions(SKFilterMode.Nearest)` para sprites (pixel art) e `SKSamplingOptions(SKFilterMode.Linear)` só para texto ou elementos vetoriais.
3. Sem `SKImageFilter` (blur, sombras) por frame. Sombras são elipses simples.
4. Antialias apenas em elipses (anel pulsante) e no texto JetBrains Mono. **Silkscreen e retângulos sem AA** (SDD-20 RF-20.3).
5. Desenhar sprites sempre do cache pré-escalado com `DrawImage(atlas, srcRect, dstRect)`, em que `dstRect` tem o mesmo tamanho de `srcRect` (blit 1:1, SDD-09).
6. Culling: não desenhar o que estiver fora da superfície da janela (a faixa, no caso da cena).

### RF-08.9: Quadro de teste (MVP técnico E1)

O `TestSceneRenderer` desenha, numa superfície do **tamanho exato da faixa (96 DIP × largura da área de trabalho, SDD-22 §7)**, a linha de borda e marcas nas posições do design (núcleo, 8 slots, portal), mais retângulos com sprite placeholder em movimento horizontal (24 "inimigos"), 12 "torres" fixas, 24 números flutuantes simulados, 1 banner e 1 caixa, e o texto de contadores. Serve para medir o custo antes do jogo existir (SDD-16, cenário `e1-strip-synthetic`).

### RF-08.10: Várias janelas, uma superfície por janela (ADR-021, ADR-028)

- Cada janela layered do jogo (cena, widget HUD, selo de ausente, banner, caixa, painel, card) possui **uma** `LayeredSurface`, uma `RenderInvalidation` e o seu próprio `SourceConstantAlpha`. Não há superfície compartilhada nem composição entre janelas: a ordem visual é o z-order das janelas (dona = cena, SDD-22 RF-22.8).
- **Render por evento** (painel, cards, widget): redesenham **somente** quando o modelo visual muda (`Version`, hover, estado discreto) e nunca por relógio de fps; sem mudança, 0 `UpdateLayeredWindow` e 0 wakes (SDD-23 RF-23.9). A cena segue a `RenderPolicy` (SDD-10 RF-10.7; SDD-22 RF-22.10); banners e caixas, os deadlines discretos de SDD-22 RF-22.10.
- **Ciclo de vida:** a superfície é criada no primeiro `BeginFrame` depois do `Show`. Ao ocultar a janela (ou ocultar o overlay, SDD-07), chama-se `Release()` das janelas que não precisam manter o conteúdo (painel e cards); cena, widget e hotspots reaproveitam a superfície se o tamanho não mudou. Ocultas = 0 frames.
- As regras de custo (RF-08.8) valem para **todas** as janelas. Cada janela possui os seus `SKPaint`/`SKFont` (ou os compartilha **somente leitura** do `Typography`/`Palette` de SDD-20); como todas as janelas rodam na GameThread (ADR-002), não há concorrência entre elas.
- A janela da cena é a única com atualização parcial por bandas (RF-08.7). As demais usam frame completo (janelas pequenas).

## 6. Requisitos não funcionais

- **RNF-08.1:** Tempo de composição p95 ≤ 2 ms no cenário `strip-present` (24 inimigos, 12 torres, 24 números flutuantes, 1 banner, 1 caixa) em 1920×1080 a 150% (meta, a medir; SDD-22 RNF-22.1; SDD-16). Substitui o valor antigo de 4 ms do cenário `wave-heavy` (sem projéteis nem efeitos, ADR-027).
- **RNF-08.2:** `UpdateLayeredWindowIndirect` p95 ≤ 1,5 ms da cena no mesmo cenário (meta; SDD-22 RNF-22.2).
- **RNF-08.3:** Alocação gerenciada ≤ 0 B/frame em regime (medida com `GC.GetAllocatedBytesForCurrentThread` em 1000 frames; tolerância de 64 B/frame no MVP E1, 0 a partir da E7).
- **RNF-08.4:** Memória da superfície da cena ≤ 3 MB (DIB) e da camada estática ≤ 3 MB em 4K a 200% (SDD-22 RNF-22.8); a soma das superfícies de todas as janelas visíveis é registrada na tela de diagnóstico.

## 7. Estratégia de implementação

1. **E1:** `LayeredSurface`, `FrameComposer` com o quadro de teste e medição de tempos (`Stopwatch.GetTimestamp`) na tela de diagnóstico.
2. **E4:** `StaticLayer`, `SceneRenderer` e números flutuantes (SDD-22), `TextCache` (SDD-20) e a generalização para várias janelas (RF-08.10), com os renderizadores do widget (SDD-23) e do painel/cards (SDD-24).
3. **E7:** atualização parcial (RF-08.7) sob benchmark.
4. Testes de imagem (Linux): renderizar cenas fixas num `SKSurface` em memória e comparar com PNG de referência com tolerância (SDD-17).

## 8. Dependências

SDD-03 (janela), SDD-04 (contrato de alfa e opacidade), SDD-05 (monitor e escala), SDD-09 (sprites), SDD-11 (eventos e estado), SDD-20 (tokens e fontes), SDD-21 (layout da barra), SDD-22 (cena) e SDD-23 (widget HUD).

## 9. Critérios de aceitação

- **CA-08.1 (E1):** O quadro de teste renderiza a 30 fps no overlay com transparência correta (sem halo preto em bordas, ou seja, alfa pré-multiplicado correto, verificado por captura sobre um fundo branco e um preto) (MTC-08.1).
- **CA-08.2 (E1):** Os tempos de composição e de Present aparecem na tela de diagnóstico e são registrados em CSV pelo `measure-perf.ps1` (SDD-16).
- **CA-08.3 (E4):** Os testes de imagem das cenas de SDD-22 CA-22.7 (`strip-present`, `strip-away`, `strip-peak`, `strip-boss`, `strip-fallback`, `strip-empty-slots`) e do widget (SDD-23) passam em Linux e Windows (diferença ≤ 0,5% de pixels com ΔE > 8). Substitui `field-empty`, `field-wave-mid`, `hud-build` e `tower-selected`.
- **CA-08.4 (E4):** Com o jogo pausado ou sem inimigos em campo, com `reducedMotion`, o contador de Presents fica em 0 durante 30 s sem input (SDD-22 RNF-22.6). O painel, os cards e o widget ficam em 0 Presents sem mudança de modelo (RF-08.10).
- **CA-08.5 (E7):** RNF-08.3 a 0 B/frame comprovado no teste `RenderAllocationTests`.

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-08.1 | Golden images (CA-08.3) | Linux + Windows |
| T-08.2 | Alocação por frame (1000 frames `strip-present`) | Linux + Windows |
| T-08.3 | `RenderInvalidation`: união, alinhamento, limiar de 24 entidades, bandas verticais da cena (fusão < 16 px, limiar 60%) | Linux |
| T-08.5 | Várias janelas: uma superfície por janela, `Release` ao ocultar, 0 Presents do widget/painel/cards sem mudança de modelo | Linux (contagem) / Windows |
| T-08.4 | `LayeredSurface` Resize 100× sem vazamento de GDI (`GetGuiResources(GR_GDIOBJECTS)` estável) | WindowsInteractive |
| MTC-08.1 | Bordas sobre fundos claro e escuro e nitidez em 100/150/200% | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Custo do `UpdateLayeredWindow` (cópia para o DWM) maior que o previsto em 4K | Média | Alto | Medir na E1; atualização parcial por bandas (`prcDirty`); faixa limitada a 96 DIP; contingência ADR-001 |
| Várias janelas layered (cena, widget, selo, banner, caixas, painel, cards) aumentarem o custo do DWM | Média | Médio | Janelas pequenas e ocultas quando inativas; render por evento; medir `strip-present` com banner e caixa (SDD-22 §11) |
| Diferenças de rasterização de texto entre Linux e Windows quebrarem as golden images | Alta | Baixo | Golden de texto só por bounding box; golden de pixel apenas sem texto |
| Alfa não pré-multiplicado gerar halos | Média | Médio | Pipeline de assets gera PNG premultiplicado em memória (`SKAlphaType.Premul`); CA-08.1 |
| Atualização parcial com bugs (rastros) | Média | Médio | Atrás de opção; testes T-08.3; frame completo periódico a cada 2 s como "cura" |
