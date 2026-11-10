# SDD-11: Simulação de combate (núcleo determinístico)

**Status:** Rascunho para revisão · **Etapa:** E3 · **Depende de:** SDD-01, SDD-10, SDD-12 (valores), SDD-22, SDD-27 · **Bloqueia:** SDD-13, 14, 15, 25, 26

> **Revisada (rev2, ADR-019, ADR-020, ADR-026, ADR-027).** O combate passa a ser um **mundo 1D** (distância `|dx|`, alcance em slots, torres presas a slots da barra), com **acerto instantâneo** (sem `ProjectileStore` nem projéteis) e com o **layout da faixa como estado da simulação**, alterado só pelo comando `LayoutChanged` aplicado entre ticks e incluído no save e no `StateHash`. Números e regras de torre, inimigo e onda vêm de SDD-27. Permanecem: handles, stores, determinismo, eventos, `StateHash` e `Rng`.

## 1. Identificador e título

SDD-11: Estado da simulação, armazenamento de entidades, layout 1D da faixa, ordem do tick, mira, acerto instantâneo, dano, efeitos de status, comandos e determinismo.

## 2. Objetivo

Implementar em `SideWar.Core` um combate fiel às regras canônicas da referência (modo "roles": armadura, chilled, splash e super hit), com:

- O(1) por consulta de entidade;
- zero alocação por tick em regime;
- determinismo verificável por hash;
- testes completos no Linux.

## 3. Contexto técnico

- As regras vêm da referência (ANALISE §2.3–2.6), adaptadas ao gênero idle (ADR-019). Os valores ficam em SDD-12 (formato) e SDD-27 (números e fórmulas), e esta ficha define o **comportamento**. A geometria 1D e a cena são de SDD-22; os slots, de SDD-21.
- Problemas a evitar: L1 (classe deus), L5 (alocações), L6 (busca O(n·m)) e L9 (efeitos no estado).
- Passo fixo `dt = 1/30 s` (SDD-10).

## 4. Escopo e exclusões

**Escopo:** `Simulation`, `EnemyStore`, `TowerStore`, `Handle`, sistemas, `StripLayout` e o comando `LayoutChanged` (substituem `PathTable` e `PlacementValidator`), comandos e resultados, `SimEventBuffer`, `StateHash`, `Rng`. (`ProjectileStore` foi removido: ADR-027.)

**Exclusões:** economia, XP, fases e progressão (SDD-27; SDD-13 substituída), conteúdo (SDD-12), presença e offline (SDD-25), eventos ao vivo (pico, chefe, caixas; SDD-26, que são núcleo, ADR-024), além de mover torre, vender torre e construir via "auto-placement" (fora do MVP; DP-27.3).

## 5. Requisitos funcionais

### RF-11.1: Tipos numéricos e determinismo

> **Substituído em parte por SDD-27 RF-27.1 e §3 (DP-27.4).** HP e dano passam a `double` e créditos, XP e Reservas a `long` com adição saturante; potências vêm de `PowTable` por multiplicação iterativa. O restante (proibições no tick, funções permitidas, quantização de tabelas) continua.

- **Posição (X em DIP), velocidade (DIP/s), alcance e tempo dentro do tick:** `float` (IEEE 754, sem `MathF` transcendental no tick).
- **HP e dano:** `double` (SDD-27); **créditos, XP e Reservas:** `long` saturante; contadores e `Id`: `int`.
- **Proibido no tick:** `Math.Sin/Cos/Atan2/Pow/Exp/Log`, `DateTime`, `Random`, `Dictionary` com ordem de iteração relevante e `HashSet` iterado.
- **Permitido:** `+ − × ÷`, `MathF.Sqrt` (correctly rounded), `MathF.Min/Max/Abs`, `MathF.Floor/Round` com `MidpointRounding.ToEven` explícito.
- Funções transcendentais **só no carregamento de conteúdo**; as potências de SDD-27 usam `PowTable` (multiplicação iterativa, sem `Math.Pow`) e a agenda de cada onda é montada uma vez no início dela. Os valores de **tabela** passam por `Det.Quantize(x) = MathF.Round(x * 1024f) / 1024f`.
- A simulação não guarda ângulos nem heading (não há rotação de sprites nem projéteis, SDD-22 RF-22.5).

### RF-11.2: `Handle` e armazenamento

```csharp
public readonly record struct Handle(int Value) { // bits 0..7 = índice (até 256), bits 8..31 = geração
    public int Index => Value & 0xFF; public int Generation => Value >>> 8; public static readonly Handle None = new(0); }
```

- Cada store é um array de structs com capacidade fixa, **slots estáveis**, *free list* e `generation[]` incrementada a cada remoção. As gerações **começam em 1** (o valor 0 é reservado para `Handle.None`). Na saturação de 2²⁴, voltam para 1. Uma consulta por handle é O(1) e valida a geração.
- Capacidades: `EnemyStore` 64 e `TowerStore` 16 (os slots são no máximo 12, SDD-21 RF-21.7). `ProjectileStore` foi **removido** (ADR-027).
- O limite de inimigos em campo é **44** (`MaxEnemiesOnField`, SDD-12). A capacidade 64 é margem.
- Iteração: `for i in 0..capacity-1 if alive[i]` (capacidade pequena, sem lista auxiliar).
- (Histórico: o descarte de disparo com `ProjectileStore` cheio e o contador `ProjectileDrops` deixam de existir com o acerto instantâneo.)

```csharp
public struct Enemy   { public int Id; public EnemyKind Kind; public float X, Speed, SpawnSpeed;   // X = centro do sprite em DIP; marcha para a esquerda
                        public double Hp, MaxHp; public short Armor; public EnemyFlags Flags /* Chilled, Boss */; public TowerType LastHit; }
public struct Tower   { public int Id; public TowerType Type; public int SlotIndex; public float Cooldown; public int Level;
                        public Targeting Mode; public byte Hits; }   // X e alcance vêm do StripLayout (SlotIndex) e do tipo
// Projectile removido (ADR-027): o acerto é instantâneo.
```

- `Id` é um número monotônico global (`Simulation.NextId`), exposto para a UI e o save.
- `Handle` é interno e **não** é persistido. No restore, os handles são reconstruídos a partir dos `Id`.
- A torre guarda `SlotIndex` (0-based), **não** coordenadas (SDD-21 RF-21.7); uma torre com `SlotIndex ≥ contagem de slots` do layout atual fica `SEM SLOT` (sem disparar) até o slot reaparecer.

### RF-11.3: `PathTable` (construída no carregamento do mapa)

> **Substituído por SDD-22 RF-22.2 e pelo layout abaixo (ADR-020, ADR-026).** O caminho é uma reta horizontal do portal ao núcleo; a distância é `|x_inimigo − x_torre|`. O layout da faixa faz parte do estado da simulação, muda só pelo comando `LayoutChanged` aplicado entre ticks e entra no save e no `StateHash`. O texto da `PathTable` abaixo é histórico.

**`StripLayout` (vigente, Core):**

- Campos (em DIP, `float`): `SpawnX` (centro X do inimigo ao nascer = `portal.Left + portal.Width/2`, `portal.Left = TrayLeftX + 16`), `LeakX` (borda direita do núcleo, `94`; DP-22.9), `CoreX`, `PortalX` e `SlotX[]` (`CenterXDip` de cada slot do `SlotMap`, SDD-21 RF-21.7; no máximo 12). `PathLength = SpawnX − LeakX`.
- **Comando `LayoutChanged(layout)`**: substitui o layout inteiro. Aplicado pelo `GameHost` **entre ticks** (SDD-10 RF-10.6), nunca durante um tick; os valores entram quantizados (`Det.Quantize`) e o layout é gravado no save e incluído no `StateHash` (RF-11.12). O Runtime o envia quando `TaskbarLayoutChanged` (SDD-21), DPI ou monitor mudam (SDD-22 RF-22.1).
> **Ajustado por ADR-031.** (DP-11.1)
- **Efeitos de um `LayoutChanged`:** o alcance de cada torre passa a ser medido a partir do novo `SlotX[SlotIndex]`; torres com `SlotIndex` fora do novo `SlotMap` ficam `SEM SLOT`; a velocidade dos inimigos é em DIP/s (SDD-27 RF-27.6) e **não** muda. O tratamento de inimigos já em campo cujo `X` fica fora do novo intervalo `[LeakX, SpawnX]` é resolvido por ADR-031: inimigos com `X` além do novo `SpawnX` são **limitados a ele** (sem recalcular os demais; nenhuma morte nem vazamento imediato).
- Não há `PathDist`, faixa (`lane`), normal, grade de distância à estrada nem `DistanceToRoad`.

**Histórico (`PathTable`):**

- Entrada: pontos-guia do mapa (SDD-12).
- Catmull-Rom com 60 amostras por segmento, reamostragem a cada **4 unidades** (como na referência) e `Det.Quantize` em cada coordenada.
- Saída: arrays `float[] X, Y, DirX, DirY` (direção normalizada por `Sqrt`) e o `Length` total.
- `PointAt(dist, lane, out x, out y, out dirX, out dirY)`:
  - índice `floor(dist/4)` e interpolação linear;
  - deslocamento de faixa pela normal `(−dirY, dirX)·clamp(lane, −(roadHalf−5), roadHalf−5)`.
- `DistanceToRoad(x, y)`: distância mínima aos segmentos. Usada **só** em validação de posicionamento, com cache por grade de 8 unidades criada no carregamento (`float[]` 240×25).

### RF-11.4: Ordem do tick (`Simulation.Tick()`, apenas na fase Wave)

> **Substituído por SDD-27 RF-27.7, RF-27.8 e RF-27.9 e por SDD-26 (ADR-019, ADR-024, ADR-027).** Ondas automáticas (sem `StartWave` nem `Build`), acerto instantâneo (sem `ProjectileSystem`), queda com recuo (sem `Defeat`), pico/chefe como núcleo (sem `ISimModule`). A ordem vigente está abaixo; a original é histórico.

**Ordem vigente:**

1. `WaveSystem.Spawn`: `waveClock += dt`. Enquanto `schedule[next].t ≤ waveClock` e `aliveEnemies < 44`, spawnar em `X = SpawnX` (RF-11.8). A fila de extras do pico (`PeakQueue`, SDD-26 RF-26.2) também é consumida aqui.
2. Fatores de evento (SDD-26, núcleo): o **fator de pico** ×3 (SDD-26 RF-26.3) é lido no passo 3.3; o `EventScheduler` e o `BossEncounter` avançam com `PresentSeconds` (só em Presente). Não há `ISimModule`.
3. `TowerSystem`: para cada torre com slot válido, em ordem crescente de `Id`:
   1. `Cooldown = max(0, Cooldown − dt)`;
   2. `Target = Targeting.Pick(tower)` (RF-11.5, distância 1D);
   3. se `Target ≠ None && Cooldown == 0`: executa o **acerto instantâneo** (RF-11.6) no mesmo passo, emite `Hit` (com X do alvo e o dano, para o número flutuante) e define `Cooldown = cadência` do tipo (RF-11.7).
4. *(removido: `ProjectileSystem`; ADR-027)*
5. `DeathSystem`: para cada inimigo com `Hp ≤ 0`, ganhos de créditos e XP de SDD-27 RF-27.1/27.6 (com multiplicadores), emitir `Kill` e remover.
6. `MovementSystem`: para cada inimigo vivo:
   1. `X −= Speed·dt`;
   2. se `X ≤ LeakX`: dano ao núcleo (SDD-27 RF-27.6/27.8), emitir `Leak` e remover.
7. Nível e XP (SDD-27 RF-27.9: `enquanto xp ≥ nível × 45`).
8. Se `BaseHp ≤ 0`: **queda** (SDD-27 RF-27.8): onda abandonada, inimigos em campo descartados, núcleo restaurado, recuo de 5 ondas, `falls += 1`, evento `Fall`; sem `Defeat` e sem perda de créditos.
9. `WaveSystem.CheckEnd`: se `schedule` (e a `PeakQueue`) está esgotado e `aliveEnemies == 0`, então `rest += dt`, senão `rest = 0`. Se `rest ≥ 1,4`, começa a próxima onda automaticamente (SDD-27 RF-27.7; chefe conforme SDD-26/SDD-25) ou conclui a fase (SDD-27 RF-27.9).

**Histórico:**

1. `WaveSystem.Spawn`: `waveClock += dt`. Enquanto `schedule[next].t ≤ waveClock` e `aliveEnemies < 44`, spawnar.
2. Ganchos de módulo (interface `ISimModule`, SDD-19): `OnWaveStarted`, `PreTowers(dt)` (chamado aqui), `DamageFactor(towerId)` (aplicado no passo 3.3), `ModifySpawn(ref Enemy)` (RF-11.8), `HasPendingWork` (consultado no passo 9, `CheckEnd`), `OnWaveFinished` e `ReservedPoints` (fonte do ponto/raio da linha 5 do RF-11.9). Sem módulo ativo a lista de módulos é vazia e nada muda.
3. `TowerSystem`: para cada torre, em ordem crescente de `Id`: cooldown, `Targeting.Pick` e, com alvo e `Cooldown == 0`, criar o projétil e definir `Cooldown = intervaloEfetivo`.
4. `ProjectileSystem`: mover o projétil e executar o impacto quando `d ≤ travel + HitRadius`.
5. `DeathSystem`: `Economy.OnKill(kind)` (SDD-13), `Kill` e remover.
6. `MovementSystem`: `PathDist += Speed·dt`; se `PathDist ≥ Length`, `Economy.OnLeak(kind)`, `Leak` e remover.
7. `Economy.CheckLevelUp()` (SDD-13).
8. Se `BaseHp ≤ 0`: `phase = Defeat`.
9. `WaveSystem.CheckEnd`: descanso de 1,4 s e `FinishWave()` (SDD-13: bônus, Victory ou Build).

### RF-11.5: Mira (`Targeting.Pick`)

> **Substituído por SDD-27 RF-27.4 e SDD-22 RF-22.2 (ADR-020).** Distância 1D `|xInimigo − xTorre|` e alcance em slots (`slots × 48` DIP); o inimigo "mais avançado" é o de **menor `X`** (mais perto do núcleo). Na UI só `First` (DP-27.6); os outros modos continuam no Core. O texto abaixo foi adaptado.

- Candidatos: inimigos vivos (`Hp > 0`) com `X > LeakX` e `|X − xTorre| ≤ alcanceDIP`, em que `xTorre = SlotX[SlotIndex]` e `alcanceDIP = alcanceSlots × 48` (JSON, SDD-12 RF-12.4). Sem `Sqrt` nem achatamento de perspectiva.
- Modos (`Targeting`):
  - `First` (padrão): menor `X` (o mais próximo do núcleo dentro do alcance);
  - `Last`: maior `X`;
  - `Strong`: maior `Hp` e, no empate, menor `X`;
  - `Weak`: menor `Hp` e, no empate, menor `X`.
- Empate total: menor `Id`.
- Custo O(inimigos) por torre: no máximo 12×44 = 528 verificações por tick. Grade espacial não é necessária no MVP.

### RF-11.6: Impacto e dano

> **Substituído em parte por SDD-27 RF-27.4 e RF-27.5 (ADR-027).** O impacto é **instantâneo**, na hora do disparo (sem projétil em voo e sem efeito de impacto; só o número de dano flutuante). Números novos: `dano(L) = dano₁ × 1,12^(L−1)` (tabela), lentidão do Gelo fator **0,65** (piso 45% da `SpawnSpeed`), área do Canhão de raio **36 DIP** com fator 0,6, `Scale Set` com `14 × (1 + 0,15 × nº de Raios)` reavaliado a cada disparo, e o fator de pico ×3 (SDD-26 RF-26.3). Não há venda de torre. O pseudocódigo abaixo mostra a estrutura (com os números antigos como histórico).

> **Ajustado por ADR-031.** Ordem fixa do dano (tudo em `double`): dano(nível) → super hit ×2 → Canhão contra runner ×0,5 → chilled ×1,25 → pico ×3 → menos armadura → `max(1, floor(resultado))`.

```
superHit = tower.Type == Bolt && (++tower.Hits >= 3) → tower.Hits = 0, factor = 2  (senão factor = 1)
dano(alvo, torre, fator):                                     (ADR-031: ordem fixa, tudo em double)
    d = danoDaTorre(L, tipo)                                  (1) dano(nível)
    d *= fator                                                (2) super hit ×2 (Raio), ou 0,6 de área (Canhão)
    if torre.Type == Cannon && alvo.Kind == Runner: d *= 0.5  (3) Canhão contra runner ×0,5
    if alvo.Chilled && torre.Type != Frost:         d *= 1.25 (4) chilled ×1,25
    d *= fatorPico                                            (5) pico ×3 (SDD-26), senão 1
    d -= alvo.Armor                                           (6) menos armadura
    return max(1, floor(d))                                   (7) piso de 1 após floor
aplicar ao alvo: Hp −= dano(alvo, torre, factor); LastHit = torre.Type; emitir Hit(tipo, alvo.X, dano); se superHit emitir SuperHit
Frost: alvo.Chilled = true; alvo.Speed = min(alvo.Speed, max(alvo.Speed*0.65, alvo.SpawnSpeed*0.45))
Cannon (área): para cada outro inimigo vivo com |X − alvo.X| ≤ SplashRadius (36 DIP, de `rules.json`): Hp −= dano(outro, torre, 0.6); LastHit = Cannon; emitir Splash(X) uma vez
```

- O contador de super hit pertence à torre e conta **apenas acertos reais** (impactos), não disparos. Só o Raio tem super hit.
- Com o acerto instantâneo, um alvo já morto no mesmo tick não é candidato (`Hp > 0`, RF-11.5); o excesso de dano em uma morte é descartado.

### RF-11.7: Atributos efetivos da torre

> **Substituído por SDD-27 RF-27.5 (ADR-019).** Níveis de 1 a 999 (sem o máximo de 5); `dano(L) = dano₁ × 1,12^(L−1)` **recalculado da base** pela tabela (sem arredondamento cumulativo); custo de melhoria `ceil((base × 1,2) × 1,18^(L−1))`; cadência e alcance **não** mudam com o nível. O texto abaixo é histórico.

- `intervaloEfetivo = BaseInterval(tipo) · max(0,48; 1 − (Level−1)·0,06)`.
- Na melhoria:
  - `Damage = RoundToEven(Damage · 1,34)`;
  - `Range = RoundToEven(Range · 1,06)`;
  - `Level += 1` (máximo 5);
  - `Invested += custo`.
- **Não** recalcular a partir da base: o arredondamento é cumulativo, como na referência.

### RF-11.8: Spawn de inimigo

> **Substituído por SDD-27 RF-27.6 e RF-27.7 (ADR-019, ADR-020).** `HP(w) = hpBase × 1,09^(w−1) × (1 + 0,05·i) × multRegiãoHP`; velocidade fixa em DIP/s por tipo (não cresce com a onda); sem faixa (`lane`) nem `bossStrength`. O inimigo nasce em `X = SpawnX` e marcha para a esquerda. O texto abaixo é histórico.

- A partir do item da agenda `{t, kind}`:
  - `Hp = MaxHp = max(1, RoundToEven(baseHp(kind) · hpMult(onda) · hpScale · bossStrength))`;
  - `SpawnSpeed = Speed = PathLength / traversalSeconds(kind) · speedMult(onda)`.
- Faixa: `Lane = ((spawnIndex % 3) − 1) · 6`, em que `spawnIndex` é o índice do inimigo na onda.
- `Armor = armor(kind)`. `Flags.Boss` se `kind == Boss`.
- Emitir `EnemySpawned` (e `BossSpawned` para o chefe).

### RF-11.9: Posicionamento (`PlacementValidator.Validate(x, y, towers, ignoreId?) → PlacementReason`)

> **Substituído por SDD-21 RF-21.7, SDD-22 e SDD-27 RF-27.5 (ADR-020).** Não há posição livre `(x, y)`: a torre ocupa um **slot** (botão da barra ou slot virtual); `BuildTower(type, slot)` só exige slot livre, torre desbloqueada e créditos. Saem os motivos `OutOfField`, `Road`, `Core`, `Entry`, `Reserved` e `Overlap` e a função `CoversRoad`. O texto abaixo é histórico.

| Ordem | Motivo | Regra (unidades de mundo) |
|---|---|---|
| 1 | `OutOfField` | `x < 18 ∨ x > 1902 ∨ y < 16 ∨ y > 184` |
| 2 | `Road` | `DistanceToRoad(x,y) < RoadHalfWidth + TowerRadius + 4` (= 16+14+4 = **34**) |
| 3 | `Core` | `dist(base) < 56` |
| 4 | `Entry` | `dist(spawn) < 40` |
| 5 | `Reserved` | ponto reservado de módulo (SDD-19), se ativo: `dist < 36` |
| 6 | `Overlap` | `dist(outra torre) < 36` |
| — | `None` | válido |

Função auxiliar `CoversRoad(x, y, range) = DistanceToRoad ≤ range + RoadHalfWidth`, usada apenas para o aviso "fora do alcance da pista" (a construção continua permitida).

### RF-11.10: Comandos (`Simulation.Apply`)

> **Substituído em parte por SDD-27 RF-27.5 e SDD-26 RF-26.15 (ADR-019, ADR-026).** `BuildTower(type, slot)` e `UpgradeTower(id)` com os custos de SDD-27 (construção `ceil(base × 1,5^n)`, sem limite por tipo); **removidos** `SellTower` e `StartWave` (DP-27.3, ADR-019); acrescentados `LayoutChanged` e os comandos de eventos de SDD-26. A tabela abaixo foi atualizada, com as linhas antigas marcadas.

Pré-condição comum: `RunState == InStage` e `phase == Wave`, salvo indicação contrária. O `GameHost` rejeita com `Paused` quando `PauseReasons ≠ None` (exceto `SetTargeting` e `LayoutChanged`).

| Comando | Validações (em ordem) | Efeito | Eventos |
|---|---|---|---|
| `BuildTower(type, slot)` | slot existe e está livre; torre desbloqueada; `credits ≥ ceil(base × 1,5^n)` (`n` = torres existentes) | debita; cria a torre `Level=1`, `SlotIndex=slot`, `Mode=First` | `TowerBuilt` |
| `UpgradeTower(id)` | existe; `Level < 999`; `credits ≥ ceil((base × 1,2) × 1,18^(L−1))` | SDD-27 RF-27.5 | `TowerUpgraded` |
| ~~`SellTower(id)`~~ | **removido** (DP-27.3) | — | — |
| `SetTargeting(id, mode)` | existe (vale também com o jogo pausado) | muda o modo | — |
| ~~`StartWave()`~~ | **removido** (ondas automáticas, SDD-27 RF-27.7) | — | — |
| `LayoutChanged(layout)` | layout válido (`SpawnX > LeakX`, slots ≤ 12) | substitui o `StripLayout` entre ticks (RF-11.3) | `LayoutChanged` |
| comandos de SDD-26 RF-26.15 | estado Presente e evento ativo | pico, chefe, caixa | eventos de SDD-26 |

Resultado: `CommandResult(Status, Reason, Missing)`, em que `Missing` são os créditos que faltam.

### RF-11.11: Eventos (`SimEventBuffer`)

- Buffer circular de 1024 `SimEvent` (struct: `Type`, `X`, `Y`, `A`, `B`).
- Os consumidores (Rendering, HUD e, futuramente, áudio) leem por um cursor próprio após cada wake. Se o leitor ficar mais de 1024 eventos atrás, perde os mais antigos (só efeitos visuais, sem impacto na lógica).
- Tipos: `EnemySpawned, BossSpawned, Hit, SuperHit, Splash, Kill, Leak, TowerBuilt, TowerUpgraded, WaveStarted, WaveCleared, LevelUp, Fall` (queda do núcleo), `StageCleared` e `LayoutChanged`, mais os eventos de SDD-26 RF-26.15. (Saem `TowerSold`, `Victory` e `Defeat`.) `Hit` carrega o X do alvo e o dano, que alimentam o número flutuante (SDD-22 RF-22.5).

### RF-11.12: `StateHash`

- FNV-1a 64 sobre: `tick`, `phase`, `wave`, `credits`, `carry`, `reservas`, `xp`, `level`, `baseHp`, `maxBaseHp`, **o layout da faixa (`StripLayout`: `SpawnX`, `LeakX`, `CoreX`, `PortalX`, contagem e `SlotX[]`; ADR-026)** e, em ordem de `Id`, os campos de cada torre (inclui `SlotIndex`) e inimigo (floats pelos **bits**, `BitConverter.SingleToInt32Bits`; `double` por `DoubleToInt64Bits`). Inclui também o estado do `WaveSystem` (agenda restante, `PeakQueue`, `waveClock`, `rest`), o estado de SDD-26 (eventos, ledger) e o `Rng`. (Não há projéteis.)
- Usado em testes, replay, verificação de save e restore (SDD-14) e RNF-07.4.

### RF-11.13: `Rng`

- PCG32 (estado de 64 bits mais incremento), com seed gerada na primeira partida, persistida e nunca reiniciada (SDD-27 RF-27.9).
- Agora é **usado** pelas regras de SDD-26 (sorteio de pico, caixa, posição da caixa e pergunta, na ordem de SDD-26 RF-26.13); as missões diárias usam uma seed derivada e **não** consomem este `Rng` (SDD-27 RF-27.11). Consumir o `Rng` altera o hash.

## 6. Requisitos não funcionais

- **RNF-11.1:** `Tick()` no cenário `wave-heavy` (44 inimigos e 12 torres de nível alto, sem projéteis) com média ≤ 60 µs e p99 ≤ 150 µs em x64 (meta; BenchmarkDotNet em Windows, com indicação no Linux).
- **RNF-11.2:** 0 B alocados por tick em regime (`[MemoryDiagnoser]`).
- **RNF-11.3:** Memória de `Simulation` ≤ 64 KB, sem contar o conteúdo.
- **RNF-11.4:** Cobertura de linhas ≥ 90% em `SideWar.Core` (coverlet do SDK, `dotnet test --collect "XPlat Code Coverage"`).

## 7. Estratégia de implementação

1. `Handle`, stores e testes de free list e geração.
2. `StripLayout` e o comando `LayoutChanged` com testes (conversão de slots, `PathLength`, efeito em torres e inimigos).
3. Sistemas na ordem do tick (RF-11.4 vigente), um por arquivo, cada um com testes unitários de cenário mínimo.
4. Comandos e resultados (RF-11.10 vigente).
5. `StateHash`, replay determinístico e benchmarks.
6. Testes de paridade com a referência: cenários controlados (1 torre Bolt e 1 grunt, por exemplo). Os valores esperados são calculados à mão a partir das fórmulas desta ficha, **não** executando a referência.

## 8. Dependências

SDD-10 (passo e fases) e SDD-12 (formato do conteúdo). SDD-27 fornece a economia (créditos, XP, níveis, fases) como interface `IEconomy` e os números de torres, inimigos e ondas; nesta etapa, um *stub* com as fórmulas de SDD-27 já basta. SDD-21 e SDD-22 fornecem o `SlotMap` e a geometria 1D; SDD-25 e SDD-26 consomem o estado.

## 9. Critérios de aceitação

- **CA-11.1:** Todos os testes de T-11.x passam no Linux e no Windows.
- **CA-11.2:** Replay: 3 partidas scriptadas (uma delas de 200 ondas, SDD-27 CA-27.11), com construções, melhorias e **`LayoutChanged` no meio da partida**, produzem `StateHash` final idêntico em 5 execuções, no Linux e no Windows. Se houver divergência **entre SOs**, o critério reprova e é preciso substituir as funções transcendentais por tabelas.
- **CA-11.3:** RNF-11.1 e RNF-11.2 medidos e registrados (Windows), com o rótulo "comprovado" ou "reprovado".
- **CA-11.4:** Cobertura ≥ 90% (RNF-11.4).

## 10. Cenários de teste

| ID | Cenário |
|---|---|
| T-11.1 | Bolt com 3 acertos: o 3º dá dano ×2 (contador por torre; só acertos reais) |
| T-11.2 | Frost: lentidão (fator 0,65) cumulativa até o piso de 45% da `SpawnSpeed`; chilled dá +25% em Bolt e Cannon e 0% no Frost |
| T-11.3 | Cannon: área de raio 36 DIP com fator 0,6; metade do dano em runner; armadura aplicada a cada alvo; dano mínimo 1 |
| T-11.4 | Mira 1D: os 4 modos e desempates (menor `X` = primeiro), alcance em slots |
| T-11.5 | Acerto instantâneo: o dano é aplicado no tick do disparo; alvo morto no mesmo tick não é candidato; pico ×3 multiplica o dano |
| T-11.6 | Limite de 44 inimigos: spawn adiado e retomado |
| T-11.7 | Vazamento em `X ≤ LeakX`: dano ao núcleo; queda exatamente quando o HP chega a 0, com recuo de 5 ondas e núcleo restaurado |
| T-11.8 | Fim de onda: a próxima começa sozinha após 1,4 s (42 ticks) de campo vazio |
| T-11.9 | Comandos: cada validação da tabela RF-11.10 vigente e o `Missing` correto; `SellTower` e `StartWave` inexistentes |
| T-11.10 | `LayoutChanged`: aplicado só entre ticks; entra no hash; torre `SEM SLOT`; `PathLength`; slots ≤ 12 |
| T-11.11 | Handles: geração inválida após remoção e reutilização de slot |
| T-11.12 | Stress: 10 000 ticks com spawn máximo e sem alocação |
| T-11.13 | Replay e hash (CA-11.2) |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Divergência de ponto flutuante entre x64 e arm64, ou entre Linux e Windows | Baixa | Médio | RF-11.1 (sem transcendentais no tick, quantização); CA-11.2 |
| Densidade de inimigos por alcance diferente no mundo 1D | Média | Médio | Alcance em slots e velocidades em DIP/s no JSON (SDD-27); playtest na E5 |
| Super hit dependente da ordem de impacto | Baixa | Baixo | Ordem determinística por `Id` de torre; documentado |
| Layout da faixa dependente do monitor e da barra quebrar o determinismo entre máquinas | Média | Alto | ADR-026: layout é estado, muda só por `LayoutChanged` entre ticks, vai ao save e ao hash; replays registram o comando |

### Decisões pendentes (lacunas reais; o Tech Lead decide ou delega)

| ID local | Questão | Padrão proposto |
|---|---|---|
| DP-11.1 | **Resolvida (ADR-031).** O que fazer, num `LayoutChanged`, com inimigos já em campo cujo `X` fica fora do novo `[LeakX, SpawnX]` (por exemplo, a faixa ficou mais curta) | Limitar o `X` ao novo `SpawnX` sem recalcular o resto; nenhuma morte nem vazamento imediato |
| DP-11.2 | **Resolvida (ADR-031: `max(1, floor(resultado))`).** Arredondamento do dano com `double`: manter `RoundToEven(d)` antes da armadura (SDD-11 original) ou usar `max(1, d − armadura)` sem arredondar (como escrito em SDD-27 RF-27.4) | Sem arredondar, conforme SDD-27 |
| DP-11.3 | **Resolvida (ADR-031).** Ordem de aplicação dos fatores (super hit, pico, Canhão×Rápido, chilled): o produto é o mesmo, mas a ordem das multiplicações em `double` altera os bits | Fixar a ordem do pseudocódigo de RF-11.6 |
| DP-11.4 | **Resolvida (ADR-031; SDD-27 corrigida).** SDD-27 RF-27.4 e DP-27.6 ainda descrevem projétil teleguiado e velocidades de projétil, o que contradiz o acerto instantâneo (ADR-027) | Seguir ADR-027; revisar SDD-27 |
