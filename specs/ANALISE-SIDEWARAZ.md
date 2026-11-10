# Análise do SideWarAZ (Fase 1)

> Repositório de referência: `/home/wellissonreis/repos/sideWarAZ` (somente leitura; nada foi alterado).
> Commit analisado: `bb16778` (2026-11-08, "versão mobile em paisagem").
> Data da análise: 2026-11-10.

Convenção de marcação:

- **[FATO]**: observado diretamente no código, com referência `arquivo:linha`.
- **[INFERÊNCIA]**: conclusão tirada do código, mas não verificada em execução.
- **[PROPOSTA]**: recomendação para o SideWar Desktop. As decisões finais ficam nas fichas SDD e em `DECISOES.md`.

Nenhum teste nem build do SideWarAZ foi executado nesta análise. Os resultados citados vêm dos próprios documentos do repositório (`docs/validacao.md`) e **não foram reproduzidos**.

---

## 1. Visão geral

| Item | Valor |
|---|---|
| Nome do produto | "Azure Defense" (README.md:1) |
| Stack | TypeScript 5.7, Vite 6, Canvas2D, Playwright para testes de navegador (package.json) |
| Tamanho | ~12,8 mil linhas em `src/*.ts`; `main.ts` tem 2157 linhas, `campaign.ts` 1417, `game.ts` 704 (com linhas muito longas, ~105 KB) |
| Assets | 24 PNG RGBA em `public/assets/azure/atlas/` (~2 MB no total), além de fontes WOFF2 (Pixelify Sans e IBM Plex, licença OFL) |
| Execução | Navegador; não há integração com o Windows (README.md, "Limitações atuais") |

[FATO] O motor (`src/game.ts`) não depende do DOM. A UI, o loop, a persistência e os tutoriais ficam em `src/main.ts`.

---

## 2. Mecânicas, entidades e regras de negócio

### 2.1 Entidades [FATO]

- **Tower** (`game.ts:66`): `slot` (identidade de 0 a 15), `type` (bolt|frost|cannon), `x,y` livres, `level` (1–5), `range`, `damage`, `cooldown`, `aimAngle`, `targeting` (first|last|strong|weak), `hits` (contador do super hit) e campos opcionais de modos educativos (`source`, `os`, `patch`, `overload`, `idle`, `moving`, `offline`).
- **Enemy** (`game.ts:67`): `id`, `pathDistance`, `lane`, `hp/maxHp`, `speed`, `kind` (grunt|runner|tank|boss|swarm|armored|payload), `armor`, `chilled` e `lastHit`.
- **Projectile** (`game.ts:68`): é teleguiado. Guarda `targetId`, `damage`, `speed`, `origin` (réplicas de escala) e `owner`.
- **Effect** (`game.ts:69`): efeito visual com `ttl`. Fica **dentro do estado da simulação** e é serializado.
- **GameState** (`game.ts:81`): `phase` (build|wave|victory|defeat), `wave`, `gold`, `xp`, `level`, `baseHp/maxBaseHp`, listas de entidades, `speed` (1|2), `paused` e `scaling`.

### 2.2 Mapa [FATO]

- Mundo fixo de 1600×760 (`battle-map.ts:2-3`). O caminho tem 14 pontos-guia, passa por Catmull-Rom e é reamostrado a cada 4 unidades (`battle-map.ts:17-60`). O comprimento resultante, medido nesta análise com Node, é **3563 unidades**.
- A perspectiva é achatada: `GROUND_Y_SCALE = 0.62`. A distância vertical vale 1/0,62 (`battle-map.ts:6`, `groundDistance`).
- `pointAtDistance(d, lane)` devolve a posição e o heading a partir da distância percorrida. Os inimigos andam por `pathDistance`.
- O posicionamento das torres é livre e validado por `validatePlacement` (`tower-drag.ts:24-34`), que exige folga da rua (`ROAD_HALF_WIDTH + TOWER_RADIUS + 6`), separação mínima entre torres (44), distância do núcleo (84), da entrada (60) e de uma área reservada à réplica. O limite é de 16 torres.

### 2.3 Torres [FATO] (`game.ts:41,113-140`)

| Torre | Custo | Alcance | Dano | Intervalo | Vel. projétil | Papel |
|---|---|---|---|---|---|---|
| Bolt ("Raio") | 45 | 250 | 18 | 0,72 s | 700 | Longo alcance, alvo único; **super hit ×2 a cada 3º acerto** (`game.ts:122`) |
| Frost ("Gelo") | 65 | 205 | 8 | 0,90 s | 600 | Lentidão (×0,72 por acerto, piso de 45% da velocidade base) e marca "chilled" (+25% de dano recebido de outras torres) |
| Cannon ("Canhão") | 90 | 168 | 38 | 1,35 s | 480 | Splash com raio 60 e fator 0,6; metade do dano contra runner |

- Melhoria (`game.ts:383-386,702`): custa `35 + nível × 28`, com nível máximo 5. Cada nível multiplica o dano por 1,34 e o alcance por 1,06, e o intervalo por `max(0,48; 1 − (nível−1) × 0,06)`.
- Venda (`game.ts:486-494`): reembolsa 50% do investido (construção mais melhorias).
- Limites por tipo no modo "roles": Cannon 4, Frost 3 (`game.ts:136-140`).
- Armadura plana: armored tem 18 (`game.ts:120`). O dano é `max(1, round(dano − armadura))` (`game.ts:663-667`).

### 2.4 Inimigos [FATO] (`game.ts:116-119`)

| Tipo | HP | Vel. (u/s) | Ouro | XP | Dano ao núcleo |
|---|---|---|---|---|---|
| grunt | 42 | 82 | 9 | 5 | 1 |
| runner | 27 | 150 | 10 | 6 | 1 |
| tank | 120 | 56 | 20 | 12 | 2 |
| boss | 620 | 48 | 110 | 65 | 10 |
| swarm | 22 | 86 | 5 | 3 | 1 |
| armored | 120 (armadura 18) | 60 | 18 | 10 | 2 |
| payload | 60 | 70 | 14 | 8 | 2 (exige uma "VM com patch"; mecânica educativa de IaaS) |

No máximo 44 inimigos ficam em campo ao mesmo tempo (`game.ts:10`). Quando o limite é atingido, o spawn é adiado.

### 2.5 Ondas [FATO]

- As ondas são manuais: `startWave()` só funciona na fase `build` (`game.ts:296`).
- **Modo clássico** (10 ondas, 140 de ouro inicial):
  - população `6 + 2w + ⌊w²/10⌋`, com chefe na última onda (`game.ts:700`);
  - intervalo de spawn `max(0,48; 1,22 − 0,055w)` (`game.ts:191`);
  - composição: tank em ondas `w%4==0`, runner em `w%3==0` (`game.ts:687`);
  - HP `× (1 + 0,16(w−1))` e velocidade `× (1 + 0,025(w−1))`.
- **Modo campanha longa** (`game.ts:15-40`):
  - de 60 a 100 ondas por fase (`STAGE_WAVE_COUNTS`);
  - perfil por progresso `p = (w−1)/(total−1)`: quantidade `round((4 + 26·p^0,85)·countScale) + bonus`; mistura progressiva de runner, tank e swarm; HP `1 + hpGrowth·p^1,2`; velocidade `1 + speedGrowth·p`; intervalo `max(0,28; 0,85 − 0,5p)·factor`;
  - **3 chefes por fase**, em cerca de 12%, 50% e 100% das ondas (`bossWavesFor`), com força 1; 1,4 e 2 (`BOSS_STRENGTH`).
- A onda termina quando a fila, os inimigos em campo e os projéteis acabam, seguido de um descanso de 1,4 s (`game.ts:583`).
- Bônus de fim de onda: `18 + 3w` no clássico ou `12 + 5√w` no modo longo, e nenhum na última onda (`game.ts:697`).
- O "pico de demanda" (spike) dá um aviso de 3 s e traz +4 inimigos extras distribuídos em 12 s (`game.ts:43-44,603-604`). Ele faz parte do módulo de escalabilidade.

### 2.6 Combate [FATO] (`game.ts:575-584`, ordem de um passo)

1. TTL dos efeitos.
2. Custo operacional (modos educativos).
3. Estado de escalabilidade.
4. Spawn pela agenda.
5. Spawn extra do pico.
6. Patches (IaaS).
7. Disparo das torres: cooldown, escolha do alvo dentro do alcance por modo de mira, disparo.
8. Movimento dos projéteis: o acerto acontece quando `dist ≤ deslocamento + 8`; aplica dano, lentidão, splash e super hit.
9. Movimento dos inimigos e vazamento no núcleo.
10. Derrota (`baseHp ≤ 0`).
11. Fim de onda.

- `update(dt)` limita `dt` a 0,25 s, multiplica pela velocidade (1× ou 2×) e subdivide em passos de 1/60 s (`game.ts:534`).
- O alvo é escolhido por varredura linear de todos os inimigos para cada torre. O projétil procura o alvo com `find` por id a cada passo (`game.ts:668`), o que custa O(projéteis × inimigos).

### 2.7 Escalabilidade (módulo educativo Azure) [FATO] (`game.ts:42-45,499-524,601-617`)

- **Vertical**: 18 de ouro, dano ×2 na torre selecionada por 10 s, uma vez por onda.
- **Horizontal**: 35 de ouro; uma réplica Bolt em ponto fixo, com 2 s de provisionamento e 12 s ativa, uma vez por onda.
- **Elasticidade**: interruptor que provisiona uma réplica automaticamente quando há pico com ≥4 inimigos em campo (35 de ouro) e a retira quando a demanda cai.
- Há também os modos "deployment" (local/pública/híbrida), "IaaS" (VM e patches), empréstimos, custo operacional e telemetria de custos. **Todos vivem dentro da mesma classe `Game`** e são ligados por flags em `GameOptions` (`game.ts:111`).

---

## 3. Progressão, experiência e salvamento

### 3.1 Experiência e nível [FATO] (`game.ts:696`)

- XP por eliminação. O nível sobe quando `xp ≥ nível × 45` (o XP excedente é mantido).
- Ao subir de nível: +20 de ouro (fora do modo "roles"), integridade máxima +5 e cura de 5 (`levelHeal`).

### 3.2 Campanha [FATO] (`campaign.ts:157-272`)

- 10 fases, uma por tema AZ-900: nuvem, implantação, IaaS, PaaS, SaaS, responsabilidade compartilhada, consumo, vertical, horizontal e elasticidade (README.md).
- Cada fase tem: `gold = 150 + 8i`, `hpScale = 1 + 0,05i`, `enemyCountScale = 1,25 + 0,07i`, `hpGrowth = 26 + 1,5i`, `speedGrowth = 0,4 + 0,03i` e ondas 60/60/60/80/80/80/80/100/100/100 (`campaign.ts:223-246`).
- Antes de cada chefe aparece uma pergunta sobre o tema da fase. A resposta certa enfraquece o chefe (HP ×0,6, velocidade ×0,75, `BOSS_BONUS`) e o erro o mantém com força total (`campaign.ts:249-255`).
- Estrelas (`starsForStage`, `campaign.ts:258-271`): derrota dá 0. A vitória dá 1, ou 2 com no máximo um erro nas perguntas de chefe, ou 3 com todas certas, transferência inédita correta e HP ≥ 90%.
- Checkpoint anti-farm: abortar ou perder restaura XP e recursos do início da tentativa. A vitória consolida (README.md, "Repetição e perfil").
- Banco de cenários de decisão: 15 entradas com `id:` em `world-one-content.ts` (contagem por grep; a quantidade exata de cenários não foi auditada).

### 3.3 Salvamento [FATO]

- Sessão em `sessionStorage`, chave `azure-defense-tab-session-v1`, com limite de 200 000 caracteres (`session-save.ts:1-2`). **O progresso se perde ao fechar a aba** (README.md, "Progresso e sessão").
- Preferências (dicas vistas, som, tutoriais) ficam em `localStorage` (`main.ts:1074,1821,1875`).
- `Game.serialize()` faz `JSON.parse(JSON.stringify(...))` do estado completo, incluindo efeitos e telemetria (`game.ts:535`).
- `restore()` valida e limita cada campo (`finite(...)`) e migra coordenadas de saves legados (`game.ts:537-573`).
- **O autosave roda a cada 30 frames** (~0,5 s a 60 fps), além de `beforeunload` (`main.ts:2104-2108,2113`).

---

## 4. Recursos gráficos e animações [FATO]

- Os sprites são 24 PNG separados, de 101×173 a 448×298 px (`public/assets/azure/atlas/*.png`; `manifest.json` registra o recorte de um atlas de origem). Cada arquivo é carregado com `new Image()` e redimensionado **a cada desenho** (`render.ts:61-65,148-152`).
- A renderização é pixel art, com `imageSmoothingEnabled = false` (`render.ts:57,74`).
- As torres têm duas variantes por tipo (`-a` costas e `-b` frente), escolhidas pelo ângulo de mira, e são espelhadas horizontalmente (`render.ts:133-139`).
- Os inimigos usam o sprite por tipo, um balanço senoidal (bob) e um drone que flutua (`render.ts:308-340`). O tipo é indicado por um anel colorido no chão (`KIND_MARK`).
- Efeitos: impacto, gelo e explosão por sprite com escala e alpha pelo TTL; anel expansivo ao construir ou melhorar; "×2" no super hit; flash vermelho no vazamento (`render.ts:364-396`).
- A profundidade é simulada com `depthScale(y) = 0,84 + 0,26·y/H` e ordenação por y (painter) (`render.ts:28,84-99`).
- O cenário é desenhado **uma vez** em canvas offscreen com meia resolução e ampliado. A cidade infinita é gerada em chunks determinísticos de 512 px com cache LRU de 96 (`scene.ts`).
- Existe suporte a `prefers-reduced-motion`, que congela bob e piscas (`render.ts:58-60`).
- O áudio é sintetizado com Web Audio e fica desligado por padrão (`audio.ts`).
- Há câmera com zoom e pan por transformação (`camera.ts`), que não se aplica a uma faixa de overlay.

---

## 5. Padrões arquiteturais utilizados [FATO]

- O motor é puro (`Game`), com comandos que devolvem `boolean` e propriedades de leitura (previews, telemetria).
- O estado é um objeto mutável único, serializado inteiro.
- Partes da UI são máquinas de estado independentes do DOM: `TowerDrag` (`tower-drag.ts:57-90`) e `MapCamera`.
- O conteúdo didático está separado (`world-one-content.ts`, `missions.ts`, `learning.ts`).
- O orquestrador `main.ts` é monolítico, com variáveis globais de módulo, ~86 consultas ao DOM e `paint()` chamado a todo frame (`main.ts:914,2106`).
- Testes: há testes unitários com `tsx --test` (~25 arquivos `*.test.ts`) e ~35 roteiros Playwright que **dependem de um servidor Vite já em execução** e de um gancho `window.__AZURE_QA__` (docs/validacao.md).

---

## 6. Componentes conceitualmente reutilizáveis [PROPOSTA]

| Conceito do SideWarAZ | Como aproveitar no desktop |
|---|---|
| Motor puro e sem DOM | Projeto `SideWar.Core` (net10.0) sem WPF nem Win32, testável no Linux |
| Passo fixo com `dt` limitado a 0,25 s | Passo fixo de 30 Hz, acumulador limitado e sem "catch-up" ao retomar (SDD-10) |
| Caminho por Catmull-Rom com reamostragem constante e `pointAtDistance` | Mesmo algoritmo, com o mapa em formato de faixa (SDD-12) |
| `validatePlacement` como função pura | Mesma ideia, com motivos tipados (SDD-11) |
| `TowerDrag` independente da UI | Máquina de arraste em `SideWar.Runtime`, testável (SDD-15) |
| Painter ordenado por y e cenário estático em cache | Camada estática pré-renderizada e ordenação sem alocação (SDD-08) |
| Restore com validação e limites | Desserialização tipada, validação explícita e versão de schema (SDD-14) |
| Checkpoint anti-farm | Mantido na progressão (SDD-13) |
| Regras de combate do modo "roles" (armadura, chilled, splash, super hit) | Regras canônicas do combate (SDD-11) |
| Perfil de onda por progresso e ondas de chefe | Mantidos como conteúdo parametrizado (SDD-12) |
| Reduced motion | Configuração de acessibilidade e modo de baixo consumo (SDD-15/16) |
| Áudio sintetizado sem arquivos | Opcional, pós-MVP (SDD-15) |
| Banco de perguntas AZ-900 e escalabilidade | Módulo opcional, pendente da decisão D-01 (SDD-19) |

---

## 7. Limitações e problemas que **não** devemos reproduzir

| # | Problema observado | Evidência | Diretriz para o desktop |
|---|---|---|---|
| L1 | Classe `Game` "deus": mistura combate, implantação, IaaS, empréstimos, telemetria e custos com dezenas de flags opcionais | `game.ts:111,162-178` | Núcleo de combate mínimo; módulos opcionais como sistemas separados e plugáveis (SDD-01/11/19) |
| L2 | Orquestrador monolítico (`main.ts` com 2157 linhas e estado global) | `main.ts` | Composition root fino, serviços com responsabilidade única e comandos tipados (SDD-01) |
| L3 | Loop `requestAnimationFrame` permanente, que renderiza e atualiza o DOM a todo frame, mesmo pausado ou no mapa | `main.ts:2076-2110` | Renderização sob demanda, fps por política e zero frames quando oculto ou parado (SDD-08/10/16) |
| L4 | Autosave a cada ~0,5 s com serialização completa | `main.ts:2104-2108`, `game.ts:535` | Salvamento por evento, com debounce e no máximo 1 a cada 60 s durante a onda (SDD-14) |
| L5 | Alocações por frame e por passo: closures `Drawable`, `.filter`, `.find`, `Set`, cópias de arrays | `render.ts:85-99`, `game.ts:576,668` | Pools de structs, buffers reutilizados e meta de 0 B alocados por frame em regime (SDD-11/16) |
| L6 | Busca O(n·m) do alvo do projétil por `find` | `game.ts:668` | Índice e geração da entidade (handle) com acesso O(1) (SDD-11) |
| L7 | Sprites grandes redimensionados a cada desenho, sem atlas único | `render.ts:61-65,148-152` | Atlas único e cache pré-escalado por escala de renderização (SDD-09) |
| L8 | Constantes duplicadas e espelhadas entre arquivos (`REPLICA_GUARD`, `TOWER_WIDTH`) | `tower-drag.ts:8`, `render.ts:18` | Fonte única de verdade no conteúdo (JSON) e uma constante por conceito (SDD-12) |
| L9 | Efeitos visuais no estado da simulação e serializados | `game.ts:69,535` | Efeitos na camada de apresentação, alimentados por eventos (SDD-08/11) |
| L10 | Progresso volátil (`sessionStorage`) e armazenamento misturado | `session-save.ts`, `main.ts` | Arquivos versionados em `%LOCALAPPDATA%`, com escrita atômica e backup (SDD-14) |
| L11 | Documentação divergente do código (README fala em 2 a 6 ondas por fase e 4 ondas por nível; o código usa 60/80/100) | README.md vs `game.ts:16` | Specs são a fonte da verdade; cada etapa atualiza o índice e o status (README de specs) |
| L12 | Testes de navegador acoplados ao DOM, a um servidor externo e a ganchos globais de QA | `tests/*.mjs` | Lógica testável sem UI; testes Windows isolados; modo benchmark por CLI (SDD-17) |
| L13 | UI com muitos modais e tutoriais que bloqueiam o jogo | `main.ts` (tutoriais, dialogs) | O overlay nunca abre janela ativável por conta própria (SDD-15, foco) |
| L14 | Mundo 1600×760 com câmera, pensado para tela cheia | `battle-map.ts`, `camera.ts` | Mapa em faixa, projetado para ficar acoplado a uma borda (SDD-05/12) |

---

## 8. Fatos relevantes do ambiente desta análise

- [FATO] O WSL2 tem kernel 6.18 e o **.NET SDK 10.0.402 já está instalado no Linux** (`dotnet --version`). O Node é v24.22.1.
- [FATO] O diretório do novo projeto (`/home/wellissonreis/repos/taskBarWarAZ`) estava vazio e não é um repositório Git.
- [INFERÊNCIA] Projetos `net10.0-windows` com WPF podem ser **compilados** no Linux com `EnableWindowsTargeting=true`, mas **não podem ser executados**. Isso precisa ser confirmado na etapa E0 (SDD-02).
