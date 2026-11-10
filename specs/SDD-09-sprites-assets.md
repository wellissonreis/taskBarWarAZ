# SDD-09: Sprites, atlas e pipeline de assets

**Status:** Rascunho para revisão · **Etapa:** E4 (o placeholder entra na E1) · **Depende de:** SDD-01, SDD-08, SDD-20, SDD-22 · **Bloqueia:** SDD-15 (visual final), SDD-22, SDD-23, SDD-24

> **Revisada (rev2, ADR-018, ADR-025, ADR-027).** A importação dos 24 PNG da referência foi substituída pela **exportação dos 13 sprites do design** (SDD-22 RF-22.9); a lista de sprites e os tamanhos passam a ser os de SDD-22 (em DIP); as fontes são Silkscreen e JetBrains Mono. O pipeline de atlas e o cache pré-escalado foram mantidos.

## 1. Identificador e título

SDD-09: Exportação dos assets do design, geração de atlas, manifesto, carregamento e cache pré-escalado por escala de renderização.

## 2. Objetivo

Reduzir a memória e a CPU de desenho:

- um único atlas compacto embutido no executável;
- sprites pré-escalados para a escala exata da tela, com blit 1:1 por frame;
- descarte dos dados de origem após o bake.

## 3. Contexto técnico

- A referência usava 24 PNG separados (~2 MB), com 101–448 px por lado, redimensionados a cada desenho (ANALISE L7). **Histórico:** esses PNG deixam de ser a fonte (RF-09.1).
- Os sprites oficiais são os **13 blobs do design** (SDD-22 RF-22.9), exibidos com 22–84 DIP de largura e `renderScale = dpi/96` (1,0 a 3,0 em 100% a 300%; SDD-22 RF-22.1, sem S/M/L). A resolução nativa em pixels dos PNG ainda não foi conferida (DP-22.7): a origem pode ser maior ou **igual** ao tamanho exibido.
- O estilo é pixel art: amostragem *nearest* no blit 1:1. **A redução de escala no bake** usa filtragem de alta qualidade (`SKFilterMode.Linear` + `SKMipmapMode.Linear`), porque reduzir com *nearest* destrói o sprite.
- D-03 (atualizado): os assets são **exportados do artefato de design** e os TTF de Silkscreen e JetBrains Mono são fornecidos pelo usuário. Os direitos são do próprio autor do design.

## 4. Escopo e exclusões

**Escopo:** `tools/SideWar.AssetPipeline`, `assets/source/design/`, formato do manifesto, `SpriteAtlas`, `ScaledSpriteCache`, IDs de sprite, tamanhos em DIP, fontes e placeholders.

**Exclusões:** arte nova e animações por frames (não existem no design; ficam fora do MVP) e os ícones vetoriais (SDD-20 RF-20.5).

## 5. Requisitos funcionais

### RF-09.1: Importação (executada uma vez, manualmente, após D-03)

> **Substituído por SDD-22 RF-22.9 (ADR-018).** Os PNG vêm da **exportação dos 13 blobs do artefato de design** para `assets/source/design/` (com `MANIFEST.json` e Scale Set como placeholder, D-13); os TTF de Silkscreen e JetBrains Mono são fornecidos pelo usuário (D-03, ADR-025). O script `import-reference-assets.sh` **não é executado**. O texto abaixo é histórico.

**Vale agora:** o executor lê cada blob do asset store do artefato (somente leitura), grava em `assets/source/design/<nome canônico>`, gera `assets/source/design/MANIFEST.json` (`id`, `file`, `displayDip`, `pixels`, `sha256`, `role`, e `placeholder: true` no Scale Set) e registra origem, data e licença em `assets/source/ATTRIBUTION.md`. Nada é baixado da rede (ADR-014) e a origem não é alterada. Os TTF ficam em `assets/fonts/` (RF-09.7).

- (Histórico) O script `scripts/wsl/import-reference-assets.sh` **copia** (sem modificar a origem) os 24 PNG de `/home/wellissonreis/repos/sideWarAZ/public/assets/azure/atlas/` para `assets/source/sprites/`. Copia também as fontes `pixelify-sans` e `ibm-plex-*` e os arquivos `OFL-*.txt` de `public/fonts/` para `assets/fonts/`.
  - **Atenção:** a referência tem as fontes apenas em WOFF2, e o Skia não carrega WOFF2 de forma confiável. Os arquivos `.ttf` equivalentes (Pixelify Sans, IBM Plex Sans e IBM Plex Mono, todos OFL) precisam ser **fornecidos pelo usuário** em `assets/fonts/`, baixados por ele da fonte oficial. O executor não baixa nada (ADR-014 e a restrição de não instalar dependências). A origem dos TTF faz parte de D-03. Enquanto eles não existirem, vale o fallback de RF-09.7.
- O arquivo `assets/source/ATTRIBUTION.md` registra origem, data e licença.

### RF-09.2: Definição dos sprites (`assets/source/sprites.json`, editado à mão)

> **Substituído por SDD-22 RF-22.9 (ADR-018, ADR-027).** Os ids e os tamanhos (em DIP, `worldWidth`/`worldHeight`) são os do design; saem os projéteis, os efeitos de impacto, as variantes costas/frente e o espelhamento. O JSON abaixo é a lista vigente; a lista antiga (24 PNG da referência) foi removida deste bloco e permanece no histórico do Git.

```json
{
  "version": 1,
  "sprites": [
    { "id": "tower.bolt",     "file": "design/tower-bolt.png",    "worldWidth": 44, "pivot": [0.5, 1.0], "trim": false },
    { "id": "tower.frost",    "file": "design/tower-frost.png",   "worldWidth": 39, "pivot": [0.5, 1.0], "trim": false },
    { "id": "tower.cannon",   "file": "design/tower-cannon.png",  "worldWidth": 45, "pivot": [0.5, 1.0], "trim": false },
    { "id": "tower.scaleset", "file": "design/tower-scaleset.png","worldWidth": 44, "pivot": [0.5, 1.0], "trim": false, "placeholder": true },
    { "id": "slot.empty",     "file": "design/slot-empty.png",    "worldWidth": 42, "pivot": [0.5, 1.0], "trim": false },
    { "id": "core",           "file": "design/core.png",          "worldWidth": 84, "pivot": [0.5, 1.0], "trim": false },
    { "id": "portal",         "file": "design/portal.png",        "worldWidth": 68, "pivot": [0.5, 1.0], "trim": false },
    { "id": "enemy.grunt",    "file": "design/enemy-scout.png",   "worldWidth": 23, "pivot": [0.5, 1.0], "trim": false },
    { "id": "enemy.runner",   "file": "design/enemy-drone.png",   "worldWidth": 38, "pivot": [0.5, 1.0], "trim": false },
    { "id": "enemy.swarm",    "file": "design/enemy-crawler.png", "worldWidth": 36, "pivot": [0.5, 1.0], "trim": false },
    { "id": "enemy.armored",  "file": "design/enemy-shield.png",  "worldWidth": 32, "pivot": [0.5, 1.0], "trim": false },
    { "id": "enemy.tank",     "file": "design/enemy-brute.png",   "worldWidth": 43, "pivot": [0.5, 1.0], "trim": false },
    { "id": "enemy.boss",     "file": "design/enemy-boss.png",    "worldWidth": 68, "pivot": [0.5, 1.0], "trim": false },
    { "id": "supply.crate",   "file": "design/supply-crate.png",  "worldWidth": 45, "pivot": [0.5, 1.0], "trim": false }
  ]
}
```

- Exatamente um entre `worldWidth` e `worldHeight` é obrigatório. O outro é derivado da proporção do PNG e **deve coincidir (±1 DIP) com a altura da tabela de SDD-22 RF-22.9** (44, 44, 44, 26, 56, 52, 26, 22, 22, 30, 34, 52, 24); o `ContentValidator`/teste T-09.4 confere.
- Todos os sprites ficam alinhados pela base (`pivot` Y = 1,0, pé em y = 96, SDD-22 RF-22.2). `trim: false`: o retângulo exibido é o do PNG inteiro (os tamanhos de SDD-22 RF-22.5 são os da caixa do elemento), então **não se corta a borda transparente** destes sprites (ver RF-09.3 passo 2).
- O mapeamento tipo → sprite é o de SDD-22 RF-22.9 (`grunt` = scout, `runner` = drone, `swarm` = crawler, `armored` = shield, `tank` = brute, `boss` = chefe). **Os ids acima são propostos** a partir desse mapeamento e dos nomes canônicos.
- **Os tamanhos vêm do design** (SDD-22 RF-22.5/22.9); mudar um tamanho exige alterar SDD-22. `tower.scaleset` é placeholder (D-13) até haver sprite próprio.
- Não há ids para projéteis, efeitos de impacto nem variantes espelhadas (ADR-027; SDD-22 RF-22.5).

### RF-09.3: AssetPipeline (`tools/SideWar.AssetPipeline`, console net10.0 com SkiaSharp)

Comando: `dotnet run --project tools/SideWar.AssetPipeline -- --in assets/source --out src/SideWar.Rendering/Assets` (lê `assets/source/sprites.json` e os PNG de `assets/source/design/`).

1. Ler o `sprites.json` e cada PNG.
2. **Cortar a borda transparente** (bounding box com alfa ≥ 8) **somente** nos sprites com `trim` ≠ `false` (nenhum, nesta lista; RF-09.2) e **zerar os pixels com alfa < 8** (contrato SDD-04, RF-04.4, nesta etapa para a cena sem fundo).
3. **Reduzir cada sprite** para uma "resolução mestre" de `ceil(tamanhoDip × masterScale)` px no eixo definido, com `masterScale = 3,25` **provisório**. Esse valor era o máximo de `renderScale` da faixa antiga (SDD-05 RF-05.4, que SDD-22 substitui); agora `renderScale = dpi/96` (SDD-22 RF-22.2). **Reavaliar após a exportação** (DP-22.7): como os sprites são pequenos (22–84 DIP) e a resolução nativa dos PNG pode já ser baixa, a mestre é `min(nativo, ceil(tamanhoDip × masterScale))`. A redução usa `SKSamplingOptions(SKCubicResampler.Mitchell)`. Se a origem for menor, não ampliar.
4. Empacotar num atlas (algoritmo *shelf* por altura decrescente, com padding de 2 px e largura máxima de 1024 px) e gerar **um** `atlas.png` (RGBA não premultiplicado no arquivo) mais o `atlas.json`:

```json
{ "version": 1, "masterScale": 3.25, "width": 1024, "height": 512,
  "sprites": { "tower.bolt.front": { "x":0,"y":0,"w":130,"h":124,"pivotX":0.5,"pivotY":0.96,"worldW":52,"worldH":49.6 }, "...": {} } }
```

5. Gravar o hash SHA-256 das entradas em `atlas.json.inputsHash`. Se nada mudou, o pipeline não regrava (build reproduzível).
6. Falhar se o atlas passar de 1024×1024 px.

O `masterScale` gravado no `atlas.json` acima (3,25) é o provisório. `atlas.png` e `atlas.json` são versionados no Git e embutidos (`EmbeddedResource`) em `SideWar.Rendering`. O pipeline **não** roda no build normal, só manualmente, quando os assets mudam.

### RF-09.4: Carregamento (`SpriteAtlas`, Rendering)

- No início: decodificar `atlas.png` de um `Stream` do recurso embutido para `SKBitmap` premultiplicado (`SKImageInfo(…, Premul)`), mais o manifesto (JSON source-gen).
- Manter o `SKBitmap` mestre **somente** até o primeiro bake (RF-09.5). Depois, descartá-lo e guardar só os bytes comprimidos do PNG (em `ReadOnlyMemory<byte>`, ~200–400 KB) para refazer o bake se o DPI mudar.
- Os IDs de sprite em tempo de execução são `enum SpriteId : ushort` gerados a partir do manifesto por um source generator simples **ou** uma tabela estática escrita no `Rendering`, validada por teste contra o manifesto (opção recomendada no MVP: tabela estática + teste).

### RF-09.5: Cache pré-escalado (`ScaledSpriteCache`)

- `Bake(float renderScale)` (com `renderScale = dpi/96`, SDD-22 RF-22.2):
  - para cada sprite, tamanho destino `round(worldW·renderScale) × round(worldH·renderScale)` (`worldW`/`worldH` em DIP);
  - redimensionar a partir do mestre com `SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)`;
  - empacotar num atlas de runtime (`SKImage` raster) e gravar os retângulos por `SpriteId`;
  - ~~gerar também a versão espelhada horizontalmente das torres e dos inimigos~~ — **Substituído por SDD-22 RF-22.5 (ADR-018):** os inimigos marcham para a esquerda sem espelhamento e as torres não têm costas/frente; não há versão espelhada.
- O bake é disparado no primeiro frame, em mudança de `renderScale` (SDD-05) e após `ReleaseCaches` (SDD-07).
- `TryGet(SpriteId id, out SKRectI src, out SKPointI pivotPx)` (sem o parâmetro `mirrored`).
- Memória esperada do atlas de runtime: ≤ 1 MB a 150% (meta; SDD-22 RNF-22.8). A política para escalas não inteiras (125%, 150%, 175%) é a decisão pendente DP-22.7 de SDD-22: por ora, redução com filtragem de qualidade no bake e blit 1:1 com `Nearest`.

### RF-09.6: Placeholders

- Se o recurso do atlas estiver ausente (build sem exportação, D-03 pendente), o `PlaceholderAtlas` gera em código formas geométricas coloridas com os mesmos tamanhos em DIP e **cobre todos os ids de SDD-22 RF-22.9** (mais o Scale Set, D-13). Cores de referência: bolt `#53d9f2`, frost `#9deaff`, cannon `#ffb36d`, inimigo `#c04a56`, núcleo `#6a7690`.
- O jogo deve ser 100% jogável com os placeholders.

### RF-09.7: Fontes

> **Substituído por SDD-20 RF-20.2 (ADR-025).** Silkscreen e JetBrains Mono (OFL) substituem Pixelify Sans e IBM Plex.

- Arquivos `.ttf` em `assets/fonts/` embutidos em `SideWar.Rendering`: `Silkscreen-Regular.ttf`, `Silkscreen-Bold.ttf`, `JetBrainsMono-Regular.ttf` e `JetBrainsMono-SemiBold.ttf`, com as licenças OFL (`LICENSE-*.txt`). Carregados uma vez como `SKTypeface`. Os TTF são **fornecidos pelo usuário** (D-03); o app e o executor não baixam nada (ADR-014).
- Se faltarem (desenvolvimento e CI Linux), usar `SKTypeface.Default` e registrar um aviso no log; a ausência **bloqueia a etapa E4** (SDD-20 RF-20.2).

## 6. Requisitos não funcionais

- **RNF-09.1:** `atlas.png` embutido com ≤ 600 KB; atlas mestre decodificado ≤ 2 MB, liberado após o bake.
- **RNF-09.2:** `Bake` em ≤ 60 ms a 150% (meta), na T-Game, durante o "Show" ou após o DPI change.
- **RNF-09.3:** Desenho de sprite = 1 `DrawImage` 1:1 sem matriz de escala local.

## 7. Estratégia de implementação

1. Placeholders + tabela `SpriteId` (E1/E4).
2. AssetPipeline com testes: corte (opcional por sprite), limiar de alfa, empacotamento e determinismo (dois runs → bytes idênticos).
3. Exportação dos sprites do design (SDD-22 RF-22.9, depois de D-03), geração e commit do atlas.
4. `SpriteAtlas` + `ScaledSpriteCache` com testes de memória e de retângulos.

## 8. Dependências

SDD-01 (projetos), SDD-08 (consumo), SDD-05 (monitor e DPI), SDD-20 (fontes), SDD-22 (lista de sprites e exportação) e D-03 (assets do design e TTF).

## 9. Critérios de aceitação

- **CA-09.1:** O AssetPipeline gera bytes idênticos em duas execuções consecutivas, no Linux e no Windows (hash igual).
- **CA-09.2:** Nenhum pixel do atlas com 0 < alfa < 8 (teste).
- **CA-09.3:** `ScaledSpriteCache.Bake` para as escalas 1,0; 1,25; 1,5; 1,75; 2,0 e 3,0 (100% a 300% de DPI) gera retângulos com o tamanho esperado (±1 px) para todos os `SpriteId` (teste).
- **CA-09.4:** O jogo roda com `PlaceholderAtlas` quando o recurso está ausente (teste de Rendering + smoke).
- **CA-09.5:** A memória do atlas de runtime é registrada na tela de diagnóstico e fica ≤ 1 MB a 150% (medida; registro em evidência).
- **CA-09.6:** Os 13 PNG exportados existem em `assets/source/design/` com `MANIFEST.json` consistente (SDD-22 CA-22.10), e cada `worldWidth`/altura derivada de RF-09.2 confere com a tabela de SDD-22 RF-22.9.

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-09.1 | Determinismo do pipeline | Linux + Windows |
| T-09.2 | Corte e limiar de alfa em PNG sintético | Linux |
| T-09.3 | Empacotamento sem sobreposição (propriedade: retângulos disjuntos) | Linux |
| T-09.4 | Tabela `SpriteId` ↔ manifesto ↔ tabela de SDD-22 RF-22.9 (ids e tamanhos) | Linux |
| T-09.5 | Bake em várias escalas (CA-09.3) | Linux |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Sprites pequenos (o menor, 23×26 DIP) ficarem ilegíveis ou desiguais em escalas não inteiras (125%, 150%, 175%) | Média | Baixo | Tamanhos fixos do design; DP-22.7 (política de escala depois da exportação); `Nearest` a partir de cache pré-escalado |
| Redução de pixel art por Mitchell gerar borrão | Média | Baixo | Avaliação visual na E4; alternativa: Lanczos ou *box filter* por fator inteiro |
| Exportação do artefato de design indisponível ou TTF não fornecido (D-03) | Baixa | Médio | Placeholders jogáveis (RF-09.6) e `SKTypeface.Default`; a E4 fica bloqueada sem os TTF (SDD-20) |
