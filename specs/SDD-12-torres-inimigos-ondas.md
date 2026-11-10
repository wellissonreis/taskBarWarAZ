# SDD-12: Conteúdo: mapa em faixa, torres, inimigos, ondas e chefes

**Status:** Rascunho para revisão · **Etapa:** E3 · **Depende de:** SDD-01, SDD-22, SDD-27 · **Bloqueia:** SDD-11, 13, 25, 26, 27

> **Revisada (rev2, ADR-019, ADR-020, ADR-024, ADR-026).** O mapa `strip-a` e a `PathTable` foram substituídos pelo **layout da faixa** (SDD-11 RF-11.3, SDD-22 RF-22.2); torres, inimigos, ondas e chefes passam a ter os **valores e fórmulas de SDD-27**. Mantidos: o formato de conteúdo em JSON, o `ContentHash`, o `ContentValidator` e o bot de sanidade (adaptado ao idle). O modo Clássico foi removido. O título da ficha é mantido por compatibilidade de referências.

## 1. Identificador e título

SDD-12: Modelo de conteúdo em JSON, validador e algoritmos de geração de ondas (valores de balanceamento em SDD-27).

## 2. Objetivo

Concentrar todos os números do jogo em arquivos de conteúdo versionados, como fonte única de verdade (ANALISE L8), validados no carregamento. Os valores iniciais do idle são os de SDD-27 (todos ajustáveis no playtest); esta ficha define o **formato** dos arquivos e o validador.

## 3. Contexto técnico

- **Histórico (faixa 1920×200):** o caminho da referência media 3563 unidades e o da faixa antiga ~2000 (k ≈ 0,56), com tempos preservados e distâncias escaladas por k. **Deixa de valer:** o mundo agora é 1D em DIP (ADR-020), com comprimento que depende do monitor e da barra (SDD-22 RF-22.2), e velocidades em **DIP/s** por tipo (SDD-27 RF-27.6; DP-12.1 sobre o texto de SDD-22 que ainda cita `traversalSeconds`).
- Não há perspectiva: a distância é `|dx|` (SDD-11 RF-11.5), sem `GROUND_Y_SCALE` nem `depthScale`.

## 4. Escopo e exclusões

**Escopo:** `Content/*.json` embutidos em `SideWar.Core`, schemas, `ContentLoader`, `ContentValidator`, o `IdleWaveBuilder` (algoritmo de onda; SDD-27 RF-27.7), regra de chefe a cada 20 ondas e o bot de sanidade. Os **valores** de torres, inimigos, ondas, fases, regiões, missões e economia estão em SDD-27.

**Exclusões:** fases da campanha, estrelas, missões e prestígio (SDD-27; SDD-13 substituída), eventos e banco de perguntas (SDD-26), presença (SDD-25) e o inimigo `payload` (módulo IaaS; fora do escopo).

## 5. Requisitos funcionais

### RF-12.1: Arquivos (`src/SideWar.Core/Content/`, EmbeddedResource)

`towers.json`, `enemies.json`, `waves.json`, `stages.json`, `regions.json`, `missions.json`, `economy.json` e `rules.json` (constantes gerais de combate), conforme SDD-27 RF-27.14, mais `presence.json` (SDD-25 RF-25.3 e RF-25.8) e `questions.json` (SDD-26 RF-26.9). **`map.strip-a.json` foi removido** (substituído pelo layout da faixa, SDD-11 RF-11.3). O **ContentHash** é o SHA-256 dos bytes concatenados, em ordem alfabética de nome.

### RF-12.2: Mapa `map.strip-a.json`

> **Substituído por SDD-22 RF-22.2 e SDD-11 RF-11.3 (ADR-020, ADR-026).** Não há arquivo de mapa: o mundo é 1D e o layout (comprimento, posições dos slots, núcleo, portal) é estado da simulação, alterado só pelo comando `LayoutChanged`. O validador de mapa (`PathLength ∈ [1900, 2100]`) deixa de existir; a validação do layout é a do comando `LayoutChanged` (SDD-11 RF-11.10). O texto abaixo é histórico.

```json
{
  "id": "strip-a", "worldWidth": 1920, "worldHeight": 200, "roadHalfWidth": 16,
  "guide": [[1905,60],[1700,60],[1560,140],[1300,140],[1160,60],[900,60],[760,140],[500,140],[360,60],[200,60],[80,112]],
  "base":  { "x": 80,  "y": 112 },
  "spawn": { "x": 1905, "y": 60 },
  "decor": { "seed": 7, "lampEvery": 104 }
}
```

- Os inimigos entram pela direita e o núcleo fica à esquerda, junto da HUD (como na referência).
- O validador exige `PathLength ∈ [1900, 2100]`, que os pontos estejam dentro do mundo e que a distância entre "fileiras" da estrada seja ≥ 2·(roadHalf+TowerRadius+4).

### RF-12.3: Regras gerais `rules.json`

> **Substituído em parte por SDD-27 RF-27.4, RF-27.5 e RF-27.14 (ADR-019, ADR-027).** Saem `maxTowers` (limite = número de slots), `towerRadius`, `towerSeparation`, `coreClearance`, `entryClearance`, `hitRadius` (sem projéteis), `upgrade.*` e `sellRefund` (sem venda), `laneSpacing` e `baseHp` (núcleo `100 + 5(nível−1)` em `economy.json`); mudam `splashRadius` (36 DIP) e `frostSlowFactor` (0,65). Permanecem `maxEnemiesOnField`, `splashFactor`, `chilledBonus`, `cannonVsRunner`, `frostSlowFloor`, `boltSuperHit*` e `waveRestSeconds` (1,4 s). O JSON vigente é o primeiro bloco; o segundo é histórico.

```json
{ "maxEnemiesOnField": 44, "slotRangeDip": 48,
  "splashRadius": 36, "splashFactor": 0.6,
  "chilledBonus": 1.25, "cannonVsRunner": 0.5, "frostSlowFactor": 0.65, "frostSlowFloor": 0.45,
  "boltSuperHitEvery": 3, "boltSuperHitMultiplier": 2, "waveRestSeconds": 1.4 }
```

- `slotRangeDip` (48 DIP por slot de alcance, SDD-27 RF-27.4) fica em JSON para respeitar RNF-12.1; o nome do campo é proposto (DP-12.2).

**Histórico:**

```json
{ "maxEnemiesOnField": 44, "maxTowers": 16, "towerRadius": 14, "towerSeparation": 36,
  "coreClearance": 56, "entryClearance": 40, "hitRadius": 6, "splashRadius": 34, "splashFactor": 0.6,
  "chilledBonus": 1.25, "cannonVsRunner": 0.5, "frostSlowFactor": 0.72, "frostSlowFloor": 0.45,
  "boltSuperHitEvery": 3, "boltSuperHitMultiplier": 2,
  "upgrade": { "maxLevel": 5, "costBase": 35, "costPerLevel": 28, "damageMult": 1.34, "rangeMult": 1.06,
               "intervalStep": 0.06, "intervalFloor": 0.48 },
  "sellRefund": 0.5, "waveRestSeconds": 1.4, "laneSpacing": 6, "baseHp": 100 }
```

> Histórico: o raio de splash antigo era 34 (= 60·0,56). Vale 36 DIP (SDD-27 RF-27.4); a SDD-11 lê este valor do conteúdo.

### RF-12.4: Torres `towers.json`

> **Substituído por SDD-27 RF-27.4 (ADR-019, ADR-020).** Quatro torres (`bolt`, `frost`, `cannon`, `scaleset`) com alcance em **slots** (3, 2, 2, 3), cadências 0,7 / 0,9 / 2,1 / 0,7 s, dano₁ 18 / 6 / 60 / 14×(1+0,15×Raios) e custo-base 45 / 65 / 90 / 400; sem limite por tipo; Scale Set desbloqueia na fase 09. Os campos `projectileSpeed` e `limit` saem (acerto instantâneo, ADR-027; sem limite, DP-27.3). A tabela abaixo é histórico; o formato do JSON vigente está depois dela.

| id | nome (pt-BR) | custo | alcance | dano | intervalo (s) | vel. projétil | limite | papel |
|---|---|---|---|---|---|---|---|---|
| `bolt` | Raio | 45 | 140 | 18 | 0,72 | 392 | — | longo alcance, alvo único, super hit a cada 3º acerto |
| `frost` | Gelo | 65 | 115 | 8 | 0,90 | 336 | 3 | lentidão e "chilled" (+25% de dano das demais) |
| `cannon` | Canhão | 90 | 94 | 38 | 1,35 | 269 | 4 | splash; fraco contra Rápido |

Cada torre no JSON (histórico) tinha `{ id, name, cost, range, damage, interval, projectileSpeed, limit|null, strong:[kinds], weak:[kinds], description }`.

**Formato vigente:** `{ id, name, azureTheme, cost /* base */, rangeSlots, damage /* dano₁ */, interval /* cadência, s */, unlockStage|null, strong:[kinds], weak:[kinds], description }`, com os valores de SDD-27 RF-27.4. O Scale Set acrescenta `damageBonusPerBolt: 0.15`. Os textos de `description` vêm de `TOWER_ROLE_INFO` da referência, revisados. (SDD-27 RF-27.4 ainda lista a velocidade de projétil; ver DP-11.4 de SDD-11.)

### RF-12.5: Inimigos `enemies.json`

> **Substituído por SDD-27 RF-27.6 (ADR-019, ADR-020).** Valores-base de HP, ouro, XP, armadura e dano ao núcleo **mantidos**; a coluna `traversalSeconds` é substituída por **velocidade em DIP/s** (grunt 45, runner 80, swarm 48, armored 34, tank 30, boss 25); HP, ouro e XP crescem por onda (`1,09^(w−1)`, `1,07^(w−1)`, `1,04^(w−1)`); a velocidade **não** cresce. Um sprite por tipo (SDD-22 RF-22.9): `grunt` = scout, `runner` = drone, `swarm` = crawler, `armored` = shield, `tank` = brute, `boss` = chefe (sem as variantes `.a`/`.b`). A tabela abaixo é histórico.

`traversalSeconds = 3563 / velocidadeReferência`. Na execução (histórico), `speed = PathLength / traversalSeconds`. **Vigente:** `speed` é lida direto do JSON em DIP/s.

| id | nome | HP | traversalSeconds | ouro | XP | dano ao núcleo | armadura | sprite(s) |
|---|---|---|---|---|---|---|---|---|
| `grunt` | Fila comum | 42 | 43,45 | 9 | 5 | 1 | 0 | grunt.a / grunt.b (`id % 2`) |
| `runner` | Rápido | 27 | 23,75 | 10 | 6 | 1 | 0 | runner |
| `tank` | Pesado | 120 | 63,63 | 20 | 12 | 2 | 0 | tank.a / tank.b (`id % 3 == 0` → b) |
| `swarm` | Fila densa | 22 | 41,43 | 5 | 3 | 1 | 0 | swarm |
| `armored` | Blindado | 120 | 59,38 | 18 | 10 | 2 | 18 | armored |
| `boss` | Chefe | 620 | 74,23 | 110 | 65 | 10 | 0 | boss |

### RF-12.6: Ondas `waves.json`

> **Substituído por SDD-27 RF-27.7 (ADR-019).** Saem os blocos `classic` e `long` com `bonus` e `hpExponent`. O `waves.json` vigente (abaixo) guarda quantidade `N(w) = min(30, 6 + floor(w/3))`, o perfil de mistura de inimigos (herdado de `long.mix`), o intervalo entre spawns, o fator de chefe regular e o chefe **a cada 20 ondas**; HP, ouro e XP ficam em `enemies.json`/`economy.json`. O JSON antigo é histórico.

```json
{
  "count": { "base": 6, "perWaves": 3, "cap": 30 },
  "interval": { "start": 0.85, "slope": -0.5, "floor": 0.28 },
  "mix": [ { "kind": "runner",  "start": 0.08, "slope": 0.9, "cap": 0.30 },
           { "kind": "tank",    "start": 0.18, "slope": 0.6, "cap": 0.20 },
           { "kind": "swarm",   "start": 0.30, "slope": 0.8, "cap": 0.30 },
           { "kind": "armored", "start": 0.45, "slope": 0.5, "cap": 0.15 } ],
  "bossEvery": 20, "bossRegularFactor": 0.6
}
```

**Histórico:**

```json
{
  "classic": { "waves": 10, "startGold": 140,
               "population": { "base": 6, "perWave": 2, "quadDivisor": 10 },
               "interval": { "start": 1.22, "perWave": -0.055, "floor": 0.48, "finalWave": 2.1 },
               "hpPerWave": 0.16, "speedPerWave": 0.025, "bossOnFinal": true,
               "bonus": { "base": 18, "perWave": 3 } },
  "long":    { "count": { "base": 4, "range": 26, "exponent": 0.85 },
               "interval": { "start": 0.85, "slope": -0.5, "floor": 0.28 },
               "hpExponent": 1.2,
               "mix": [ { "kind": "runner",  "start": 0.08, "slope": 0.9, "cap": 0.30 },
                        { "kind": "tank",    "start": 0.18, "slope": 0.6, "cap": 0.20 },
                        { "kind": "swarm",   "start": 0.30, "slope": 0.8, "cap": 0.30 },
                        { "kind": "armored", "start": 0.45, "slope": 0.5, "cap": 0.15 } ],
               "bossWaveFractions": [0.12, 0.5, 1.0], "bossStrength": [1.0, 1.4, 2.0], "bossRegularFactor": 0.6,
               "bonus": { "base": 12, "sqrtFactor": 5 } }
}
```

- A entrada `armored` em `long.mix` (e agora em `mix`) **não existe na referência**. Foi acrescentada para dar função ao Canhão (ADR-017, revisável no playtest).

### RF-12.7: Algoritmo do modo Clássico (`ClassicWaveBuilder.Build(w) → Schedule`)

> **Substituído por SDD-27 RF-27.7 (ADR-019).** O modo Clássico foi **removido** (gênero idle: ondas automáticas, níveis ilimitados). O `ClassicWaveBuilder` não é implementado. O texto abaixo é histórico.

```
N = round(6 + 2w + floor(w²/10)) ; se w == 10 e bossOnFinal: N += 1
gap = (w == 10) ? 2.1 : max(0.48, 1.22 − 0.055w)
para i em 0..N−1 (remaining = N − i):
   kind = (w == 10 && remaining == 1) ? boss
        : (w % 4 == 0 && remaining % 5 == 0) ? tank
        : (w % 3 == 0 && remaining % 3 == 0) ? runner : grunt
   t = i · gap
hpMult = 1 + 0.16(w−1) ; speedMult = 1 + 0.025(w−1)
```

### RF-12.8: Algoritmo do modo Longo (`LongWaveBuilder.Build(w, total, stage) → Schedule`)

> **Substituído em parte por SDD-27 RF-27.7 e RF-27.6 (ADR-019).** Vira `IdleWaveBuilder.Build(w, total, stageIndex)`: o perfil de **mistura** (grupos, `start`/`slope`/`cap`, ordenação por `(pos, ordemDoGrupo)`, `p = (w−1)/(total−1)`) e a inserção do chefe no índice `floor(len/2)` são mantidos; mudam a **quantidade** (`min(30, 6 + floor(w/3))`; chefe: `regular = max(3, round(N × 0,6))`), os **chefes** (a cada 20 ondas, sem `bossWavesFor`), o intervalo (`max(0,28; 0,85 − 0,5p)` s, sem `spawnIntervalFactor`) e os multiplicadores (`hpMult` etc. saem: HP vem de `1,09^(w−1)`, SDD-27 RF-27.6; sem `bossStrength`). Quem decide se o chefe entra é SDD-26 (Presente) ou SDD-25 (Ausente: farm da onda `w − 1`). O pseudocódigo abaixo é histórico.

Porta fiel de `waveProfile` da referência (`game.ts:27-40`), com `armored` acrescentado ao mix:

```
p = clamp((w−1)/(max(2,total)−1), 0, 1)
bossWaves = sorted(unique([max(2, round(total·0.12)), floor(total/2), total]))   # bossWavesFor
boss = w ∈ bossWaves ; ordinal = index em bossWaves
base = max(1, round((4 + 26·p^0.85) · stage.countScale) + stage.countBonus)
regular = boss ? max(3, round(base·0.6)) : base
para cada mix m: n_m = round(regular · min(cap, max(0, (p − start)·slope)))
n_grunt = regular − Σ n_m   (se < 0, reduzir os mixes na ordem inversa até zerar o excesso)
itens: para cada grupo (ordem grunt, runner, tank, swarm, armored), j em 0..n−1: pos = (j+0.5)/n
ordenar por (pos, ordemDoGrupo) → lista de kinds; se boss: inserir "boss" no índice floor(len/2)
interval = max(0.28, 0.85 − 0.5p) · stage.spawnIntervalFactor ; t_i = i · interval
hpMult = 1 + stage.hpGrowth · p^1.2 ; speedMult = 1 + stage.speedGrowth · p
bossStrength = [1, 1.4, 2][ordinal]
```

- Histórico: os resultados de `pow` eram quantizados (SDD-11, RF-11.1) e calculados uma vez por onda, em `StartWave`. **Vigente:** as potências vêm de `PowTable` (multiplicação iterativa) e a agenda é montada **uma vez no início de cada onda**, que agora começa sozinha (SDD-27 RF-27.7).
- O `Schedule` é um array de structs `(float t, EnemyKind kind)` alugado de um pool por partida (capacidade máxima: `max(N) + 1`, calculada no início da fase), sem alocação por onda.

### RF-12.9: `ContentLoader` e `ContentValidator`

- Carregamento síncrono no início (< 20 ms, meta), com JSON source-gen.
- O validador verifica:
  - tipos e faixas (custos > 0, alcance em slots ≥ 1, intervalos de 0,1 a 5, velocidades em DIP/s > 0);
  - IDs únicos e referências cruzadas (sprites ↔ SDD-09 e SDD-22 RF-22.9, kinds em `mix` e fases, regiões, missões);
  - que a soma dos grupos do perfil de mistura não exceda a quantidade (SDD-27 RF-27.14), que a última onda de cada fase seja de chefe (60, 80 ou 100 são múltiplos de 20) e os fatores de `economy.json` (1,5; 1,2; 1,18; 1,12; 1,09; 1,07; 1,04);
  - ~~o mapa (RF-12.2)~~ — removido (RF-12.2).
- Uma falha é **fatal**: o log recebe a lista completa de erros e a T-UI mostra um `MessageBox` "Conteúdo inválido (veja o log)". Isso não pode acontecer num build de release validado pelo teste T-12.1.

### RF-12.10: Bot de sanidade (`SanityBot`, apenas em testes e benchmarks)

- Estratégia determinística (adaptada ao idle, sem fase Build nem `StartWave`):
  - layout fixo de teste: o fallback virtual de 8 slots com largura 1440 DIP (SDD-21 RF-21.7), aplicado por `LayoutChanged`;
  - a cada tick em que houver créditos, construir em slots livres na ordem `bolt, bolt, frost, bolt, cannon, bolt, …` (o Scale Set só após desbloquear);
  - depois, melhorar a torre de menor nível (empate: menor Id);
  - as ondas começam sozinhas (SDD-27 RF-27.7).
- Uso:
  - cenário de benchmark `wave-heavy` (SDD-16) e a partida scriptada de 200 ondas de SDD-27 CA-27.11;
  - teste de sanidade "o bot avança N ondas sem cair" (**não bloqueante** até E5; a `ondaMax` atingida e as quedas são registradas; o valor de N não é definido aqui, DP-12.3).

## 6. Requisitos não funcionais

- **RNF-12.1:** Nenhum número de balanceamento no código C# fora do conteúdo. Exceções: `LoopConstants` (SDD-10) e as constantes de layout (SDD-05/15).
- **RNF-12.2:** Mudar um valor de balanceamento exige apenas editar o JSON e o teste correspondente.

## 7. Estratégia de implementação

1. Escrever os JSON com os valores de SDD-27 e os modelos `record` com o contexto source-gen.
2. Implementar `ContentLoader`, `ContentValidator`, `StripLayout` (SDD-11) e `IdleWaveBuilder`.
3. Testes de tabela: quantidade e composição das ondas 1, 20, 60, 72 e 100 (SDD-27 CA-27.4), chefes só em 20, 40, 60, 80 e 100, com valores esperados calculados manualmente e fixados.

## 8. Dependências

SDD-01. SDD-09 e SDD-22 (IDs de sprite, validação cruzada). SDD-27 fornece os valores; os consumidores são SDD-11, SDD-25, SDD-26 e SDD-27.

## 9. Critérios de aceitação

- **CA-12.1:** O conteúdo embutido passa no validador (T-12.1). (O critério de `PathLength` foi removido com o mapa.)
- **CA-12.2:** As tabelas de onda (T-12.2/T-12.3) batem com os valores fixados.
- **CA-12.3:** O resultado do bot no idle é registrado (`ondaMax`, quedas e créditos ao fim de uma partida scriptada) na evidência E3. Um mau resultado abre item de balanceamento para a E5 (não reprova a E3).

## 10. Cenários de teste

| ID | Cenário |
|---|---|
| T-12.1 | Validador sobre o conteúdo real e sobre JSON com cada erro possível |
| T-12.2 | `IdleWaveBuilder`: N, intervalo e composição das ondas 1, 20, 60, 72 e 100 |
| T-12.3 | Chefes a cada 20 ondas nas fases de 60, 80 e 100 ondas; `regular = max(3, round(N × 0,6))` |
| T-12.4 | ContentHash estável entre execuções e SOs |
| T-12.5 | Bot de sanidade (registro) |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| ~~Faixa com poucas áreas de construção~~ | — | — | **Obsoleto:** as torres ocupam slots da barra (ADR-020) |
| ~~Escala k mudar a dificuldade~~ | — | — | **Obsoleto:** sem mapa 2D; ver o risco de densidade em SDD-27 §11 |
| ~~`hpGrowth` de 26 excessivo~~ | — | — | **Obsoleto:** D-08 substituído pelas fases idle de SDD-27 (`1,09^(w−1)`); calibração é D-10 |
| Valores de SDD-27 mal calibrados (D-10) | Alta | Médio | Todos em JSON; ajuste após o playtest; validador de faixas |

### Decisões pendentes (lacunas reais; o Tech Lead decide ou delega)

| ID local | Questão | Padrão proposto |
|---|---|---|
| DP-12.1 | SDD-22 RF-22.2 diz que `traversalSeconds` é preservado e `speed = PathLength / traversalSeconds`, mas SDD-27 RF-27.6 fixa a velocidade em DIP/s e substitui `traversalSeconds` | Seguir SDD-27 (DIP/s); revisar o texto de SDD-22 |
| DP-12.2 | Onde fica a constante de 48 DIP por slot de alcance (SDD-27 RF-27.4), pela regra de nenhum número de balanceamento fora do JSON | `rules.json` (`slotRangeDip`) |
| DP-12.3 | Valor mínimo de ondas que o bot de sanidade deve atingir sem cair | Só registrar (não bloqueante) |
