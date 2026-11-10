# SDD-27: Economia idle: créditos, torres, ondas automáticas, fases, missões e prestígio

**Status:** Rascunho para revisão · **Etapa:** E3 (núcleo da economia e do combate idle) e E6 (missões, regiões e prestígio) · **Depende de:** SDD-11, SDD-12 · **Bloqueia:** SDD-25, SDD-26

## 1. Identificador e título

SDD-27: Regras puras do jogo idle "Azure Defense": créditos, formatação numérica, torres e melhorias, inimigos e ondas automáticas, núcleo, fases e estrelas, XP, missões diárias, prestígio "Migrar", regiões e baús.

## 2. Objetivo

- Transformar o combate de SDD-11 em um jogo **idle** (ADR-019), no qual as ondas se encadeiam sozinhas e o jogador acompanha de relance, melhora torres e ocasionalmente interage (SDD-26).
- Concentrar toda a lógica de economia e progressão em `SideWar.Core` e `SideWar.Runtime`, determinística (Rng PCG32 com seed persistida) e testável no Linux.
- Manter todos os números em JSON de conteúdo (SDD-12). **Todo valor numérico desta ficha é inicial e ajustável no playtest.**

## 3. Contexto técnico

- **Decisões do Tech Lead:** ADR-019 (gênero idle), ADR-020 (faixa de 96 DIP acima da barra de tarefas, mundo 1D em DIP, slots = botões da barra; SDD-21/22), ADR-021 (painel e cards Skia sem ativação; visual em SDD-24), ADR-022 (offline analítico; SDD-25) e ADR-024 (perguntas Azure e picos são núcleo; SDD-26).
- **Design aprovado** (artefato "Azure Defense — Idle de barra", somente leitura): painel com abas Arsenal, Campanha, Missões e Migrar; faixa; estados de presença; relatório de retorno. Os números de exemplo do design (48,2K, LV14, 1,2K, "Onda 37", "+8 Reservas") são ilustrativos. Valem as fórmulas desta ficha. Conferência: `floor(37²/170) = 8`, o que bate com o exemplo "+8 Reservas".
- **Mundo 1D:** os inimigos entram pelo portal (ao lado da bandeja) e andam em uma dimensão, em DIP, até o núcleo. Cada slot é um botão da barra de tarefas e tem uma posição x. Nada de caminho curvo, faixas (`lane`) ou perspectiva.
- **Determinismo (SDD-11 RF-11.1):** transcendentais são proibidos no tick. Todas as potências desta ficha (1,5ⁿ, 1,18ᵏ, 1,12ᵏ, 1,09ᵏ, 1,07ᵏ, 1,04ᵏ) vêm de tabelas `PowTable` construídas **por multiplicação iterativa** (`t[k] = t[k−1] × fator`, IEEE 754), sem `Math.Pow`. Isso é determinístico entre SOs.
- **Tipos:** créditos, XP e Reservas são `long` com adição saturante. HP e dano passam a ser `double` (potências de até 1,12⁹⁹⁸ ≈ 10⁴⁹ não cabem em `int`). Isso substitui SDD-11 RF-11.1 (HP e dano `int`); a quantização passa a valer só para valores de tabela.
- **Reaproveitado:** `Rng` PCG32 (SDD-11 RF-11.13), `StateHash` (RF-11.12; passa a incluir créditos, torres, onda, fase, Reservas e o Rng), perfil de mistura de inimigos (SDD-12 RF-12.8), armadura e "chilled" (SDD-11 RF-11.6).

## 4. Escopo e exclusões

**Escopo:** `Credits` (long, formatação, taxa), `PowTable`, `TowerCatalog` e melhorias, `IdleWaveBuilder`, `EnemyCatalog`, regras do núcleo (queda e recuo), `StageCatalog` (10 fases), estrelas, XP e nível, `MissionService`, `PrestigeService`, `Regions`, baús e o JSON de conteúdo correspondente.

**Exclusões:** presença, auto-escala em Ausente/Offline e cálculo offline (SDD-25); pico, caixas, chefe com pergunta e banco de perguntas (SDD-26); posições dos slots e desenho da faixa (SDD-21/22); visual do painel e dos cards (SDD-24); formato dos arquivos (SDD-14); menu e atalhos (SDD-15).

## 5. Requisitos funcionais

### RF-27.1: Créditos, XP e Reservas (tipos e ganhos)

- `Credits`, `Xp` e `Reservas` são `long`, com adição saturante (sem estouro).
- **Carry fracionário:** ganhos com multiplicadores (RF-27.12, SDD-25 RF-25.4) geram frações. O `Credits` mantém um acumulador `double carry` (persistido) para não perder frações: `ganho = floor(valor + carry)`; `carry = valor + carry − ganho`.
- Multiplicador total de créditos: `(1 + 0,01 × Reservas) × multPresença`. O de XP: `multPresença` (as Reservas **não** afetam o XP). `multPresença` vem de SDD-25 RF-25.3.
- **Créditos iniciais** da primeira partida: valor em `rules.json`, proposto 100 (DP-27.1).
- Custos e danos acima de `long.MaxValue/2` são tratados como inalcançáveis e exibidos como "—".

### RF-27.2: Formatação numérica pt-BR (`CreditFormatter.Format(double|long)`)

- Menor que 1000: inteiro sem separador (`"999"`).
- A partir de 1000: `tier = floor(log₁₀₀₀(v))` por **comparações com a tabela de potências de 1000** (sem `Math.Log`); `m = v / 1000^tier`.
- Três algarismos significativos, vírgula decimal, sem zeros à direita: `m < 10` → 2 decimais; `m < 100` → 1 decimal; `m ≥ 100` → 0 decimais. Arredondamento: metade para longe do zero.
- Se o arredondamento chegar a 1000 (por exemplo 999,95K), promove o sufixo (`"1M"`).
- Sufixos por tier: 1 = `K`, 2 = `M`, 3 = `B`, 4 = `T`, e depois `aa`, `ab`, …, `az`, `ba`, …, `zz` (tiers 5 a 680).
- Exemplos: `48200 → "48,2K"`, `1200 → "1,2K"`, `186000 → "186K"`, `1000 → "1K"`, `999950 → "1M"`, `12345 → "12,3K"`.
- `long.MaxValue` (≈ 9,22 × 10¹⁸) vira `"9,22ab"`; o `double` de dano e custo usa a mesma função e passa de `zz` apenas em valores que o jogo trata como "—".
- A cultura é fixa (`pt-BR`, vírgula decimal), **independente** da cultura do Windows.

### RF-27.3: Taxa "+N/s" (`RateMeter`)

- Média móvel dos créditos ganhos nos últimos **10 s de SimTime**: anel de 10 baldes de 1 s (30 ticks cada); `taxa = soma(baldes) / 10`.
- Mantém **duas séries**:
  - `taxaExibida`: créditos efetivamente ganhos (com multiplicador de presença). É o "+N/s" da UI.
  - `taxaRenda`: créditos ganhos **antes** do multiplicador de presença, mas já com o multiplicador de Reservas. É a base de "min de renda" (RF-27.13). Em Ausente, o valor registrado é o **valor bruto antes do ×0,25**.
- Pausada a simulação, os baldes não avançam. Sem ganhos nos 10 s, a taxa é 0.

### RF-27.4: Catálogo de torres (`towers.json`; substitui SDD-12 RF-12.4)

| id | nome na UI | tema Azure | base de custo | dano₁ | cadência (s) | alcance (slots) | especial |
|---|---|---|---|---|---|---|---|
| `bolt` | Raio | Máquina Virtual (Azure VM) | 45 | 18 | 0,7 | 3 | super hit: a cada 3º acerto, dano ×2 |
| `frost` | Gelo | WAF no Application Gateway | 65 | 6 | 0,9 | 2 | lentidão de 35% (fator 0,65), piso de 45% da velocidade de spawn; "chilled" (+25% de dano das demais) |
| `cannon` | Canhão | Azure DDoS Protection | 90 | 60 | 2,1 | 2 | área de raio 36 DIP, fator 0,6; metade do dano contra `runner` |
| `scaleset` | Scale Set | VM Scale Sets | 400 | 14 × (1 + 0,15 × nº de torres Raio) | 0,7 | 3 | desbloqueia na fase 09 (RF-27.9) |

- **Alcance em DIP** = `slots × 48`, medido em 1D a partir do centro da torre: um inimigo está no alcance se `|xInimigo − xTorre| ≤ alcanceDIP`. Raio e Scale Set: 144 DIP; Gelo e Canhão: 96 DIP.
- **Scale Set:** o bônus de `0,15 × nº de torres Raio` usa a contagem **atual** de torres Raio e é reavaliado a cada disparo. Torres `SEM SLOT` (SDD-21 RF-21.8) **não atiram**, **não entram** nesta contagem nem no DPS offline (ADR-032).
- **Armadura** e **chilled +25%** mantidos de SDD-11 RF-11.6. Ordem do dano (ADR-031) (tudo em `double`): dano(nível) → super hit ×2 → Canhão contra runner ×0,5 → chilled ×1,25 → pico ×3 → menos armadura → `max(1, floor(resultado))`.
- **Super hit** conta apenas acertos reais, como em SDD-11 RF-11.6.
- **Lentidão (Gelo):** `speed = min(speed, max(speed × 0,65, spawnSpeed × 0,45))`.
- **Acerto instantâneo (ADR-027):** não há projétil nem velocidade de projétil; o dano é aplicado no disparo e o único efeito visual é o número flutuante.
- **Mira:** `First` (o inimigo mais próximo do núcleo dentro do alcance 1D) como padrão; os modos de SDD-11 RF-11.5 continuam existindo no Core, e a UI deles fica para SDD-24 (DP-27.6). Substitui SDD-11 RF-11.5 (distância 1D, sem achatamento).

### RF-27.5: Construção e melhoria (substitui SDD-11 RF-11.7, RF-11.9 e RF-11.10 na parte de torres)

- **Construir** em um slot livre (slots = botões da barra, SDD-21/22): `custo = ceil(base × 1,5^n)`, em que `n` é o número de torres **já existentes, de qualquer tipo**, **inclusive as `SEM SLOT`** (ADR-032). Exemplo com Raio: n=0 → 45; n=1 → 68; n=2 → 102.
- **Melhorar** L → L+1: `custo = ceil((base × 1,2) × 1,18^(L−1))`. Exemplo com Raio: L1→2 = 54; L2→3 = 64. Níveis de **1 a 999** (substitui o máximo de 5 de SDD-11 RF-11.7 e de SDD-12 `upgrade.maxLevel`).
- **Dano:** `dano(L) = dano₁ × 1,12^(L−1)`. O dano é **recalculado a partir da base** pela tabela (substitui o arredondamento cumulativo de SDD-11 RF-11.7). Cadência e alcance **não** mudam com o nível.
- Limite de torres: o número de slots disponíveis (SDD-21/22). Os limites por tipo de SDD-11 RF-11.10 (Gelo 3, Canhão 4) **não** existem mais (DP-27.3).
- **Venda e mudança de posição** não fazem parte desta ficha (DP-27.3).
- Comandos: `BuildTower(type, slot)`, `UpgradeTower(id)`, `SetTargeting(id, mode)`. Validações em ordem: slot livre (só no Build), torre desbloqueada, `credits ≥ custo`. Resultado `CommandResult(Status, Reason, Missing)`.
- Melhorias exigem créditos `long`; a `PowTable` cobre até 999, e o custo acima de `long.MaxValue/2` é "inalcançável" (RF-27.1). Na prática, o nível alcançável fica na casa das centenas, e o teto de 999 é só o limite formal.
- Contador de "melhorias manuais" alimenta a missão (RF-27.11). Melhorias da auto-escala (SDD-25) **não** contam (DP-27.7).

### RF-27.6: Inimigos (substitui SDD-12 RF-12.5 e RF-11.8)

Valores-base mantidos de SDD-12 RF-12.5; velocidades em **DIP/s** substituem `traversalSeconds` e **prevalecem** sobre SDD-22 RF-22.2 (ADR-031).

| id | hpBase | ouroBase | xpBase | armadura | dano ao núcleo | velocidade (DIP/s) |
|---|---|---|---|---|---|---|
| `grunt` | 42 | 9 | 5 | 0 | 1 | 45 |
| `runner` | 27 | 10 | 6 | 0 | 1 | 80 |
| `swarm` | 22 | 5 | 3 | 0 | 1 | 48 |
| `armored` | 120 | 18 | 10 | 18 | 2 | 34 |
| `tank` | 120 | 20 | 12 | 0 | 2 | 30 |
| `boss` | 620 | 110 | 65 | 0 | 10 | 25 |

Para a onda `w` (1 a total) da fase de índice `i` (0 a 9; a fase 01 tem `i = 0`, DP-27.8) na região `r`:

```
HP(w)   = hpBase × 1,09^(w−1) × (1 + 0,05 × i) × multRegiãoHP        (double)
ouro(w) = ouroBase × 1,07^(w−1)                                       (antes dos multiplicadores)
XP(w)   = xpBase × 1,04^(w−1)                                         (antes dos multiplicadores)
```

- Sem `hpScale`, `hpGrowth`, `speedGrowth`, `countScale` ou `bossStrength` de `stages.json` (SDD-13 RF-13.3): a velocidade **não** cresce com a onda.
- A armadura é constante (18 no `armored`), e o seu peso relativo cai com o dano crescente.
- `multRegiãoHP`: RF-27.10.

### RF-27.7: Ondas automáticas (`IdleWaveBuilder`; substitui SDD-12 RF-12.8 na quantidade e RF-11.10 `StartWave`)

- **Quantidade:** `N(w) = min(30, 6 + floor(w/3))` (6 na onda 1; 30 a partir da onda 72). Em onda de chefe, `regular = max(3, round(N × 0,6))` e o chefe é inserido no índice `floor(len/2)`, como em SDD-12 RF-12.8.
- **Mistura:** o perfil de SDD-12 RF-12.8 (grupos `runner`, `tank`, `swarm`, `armored` com `start`, `slope` e `cap`; o resto é `grunt`; ordenação por `(pos, ordemDoGrupo)`) usando `p = (w − 1) / (total − 1)`, com `total` = ondas da fase.
- **Intervalo entre spawns:** `max(0,28; 0,85 − 0,5p)` s (SDD-12 RF-12.8, sem `spawnIntervalFactor`).
- **Chefe:** a cada **20 ondas** (20, 40, 60, 80, 100). Substitui `bossWavesFor`. Quem decide se o chefe entra é SDD-26 (Presente) ou SDD-25 (Ausente: farm).
- **Encadeamento:** a próxima onda começa **1,4 s** após limpar (inimigos zerados). Substitui o comando `StartWave`, o botão ONDA, a fase `Build` e o descanso de SDD-11 RF-11.4 passo 9 (o descanso vira o intervalo de 1,4 s). O `MatchPhase` de SDD-10 RF-10.2 reduz-se a `Wave` (com o intervalo) mais um estado terminal "fase concluída" (RF-27.9).
- A agenda é montada **uma vez por onda**, no início, com os resultados de `PowTable`.
- Limite de inimigos em campo e capacidade dos stores: mantidos (SDD-11 RF-11.2).

### RF-27.8: Núcleo, queda e recuo

- **HP do núcleo:** `100 + 5 × (nível − 1)` (DP-27.9), **restaurado por completo no início de cada onda**.
- Vazamento: o inimigo que chega ao núcleo causa o "dano ao núcleo" da tabela de RF-27.6 e é removido.
- **Queda** (HP chega a 0): a onda atual é abandonada, o núcleo é restaurado e a partida **volta 5 ondas**: `ondaNova = max(onda − 5, ondaSeguinteAoÚltimoChefeVencido, 1)`. **Sem perda de créditos.** Conta como "perdeu vida" para as estrelas (RF-27.9). Todos os inimigos em campo são descartados.
- "Chefe vencido" = onda de chefe concluída (o chefe morreu) na fase atual. Persistido por fase.

### RF-27.9: Fases, estrelas, XP e nível (substitui SDD-13 RF-13.1 `CheckLevelUp`/`FinishWave`, RF-13.3, RF-13.5 e RF-13.6; substitui SDD-19 RF-19.9)

**Catálogo `stages.json`** (10 fases; escrito explicitamente; tema AZ-900 de SDD-13 RF-13.3 mantido como `stageTopic`):

| id | título (design) | ondas | `stageTopic` (SDD-13) |
|---|---|---|---|
| 1 | Cloud sob demanda | 60 | Computação em nuvem |
| 2 | Onde a carga vive | 60 | Modelos de implantação |
| 3 | Controle da máquina | 60 | IaaS |
| 4 | Plataforma gerenciada | 80 | PaaS |
| 5 | Aplicativo pronto | 80 | SaaS |
| 6 | A fronteira da responsabilidade | 80 | Responsabilidade compartilhada |
| 7 | Medir antes de gastar | 80 | Consumo |
| 8 | Crescer para cima (provisório) | 100 | Escala vertical |
| 9 | Crescer para os lados (provisório) | 100 | Escala horizontal |
| 10 | Elástico de verdade (provisório) | 100 | Elasticidade |

- Os títulos das fases 08 a 10 são **provisórios** e marcados como decisão pendente (DP-27.10).
- **Conclusão:** concluir a fase = vencer a última onda (a última é sempre de chefe: 60, 80 ou 100). Concluir desbloqueia a próxima, e a **partida continua na fase seguinte, mantendo torres e créditos, com a onda reiniciada em 1**. Concluir a fase 10 não desbloqueia nada: a partida fica em loop na última onda (farm) e o prestígio continua disponível (DP-27.11).
- **Desbloqueio de torre:** o Scale Set desbloqueia ao **entrar na fase 09** pela primeira vez (persistido no perfil).
- **Estrelas por fase** (máximo, nunca diminui): 1 = concluída; 2 = acertou ≥ 2 perguntas de chefe da fase; 3 = acertou todas as perguntas de chefe da fase **e** nenhuma queda do núcleo. As respostas vêm do ledger de SDD-26 (acerto = `correct`; tempo esgotado e erro = não acertou); a contagem de quedas vem de `StageRun.Falls`. Avalia-se da maior para a menor.
- **`StageRun`:** `(stageId, attempt, falls, ondaMax, chefesVencidos)`. `attempt` incrementa em cada nova passagem pela fase (entrada nela e cada Migrar). As estrelas são consolidadas no `Profile` ao concluir a fase.
- **XP e nível:** `xp` ganho nas mortes (RF-27.6 × multiplicadores). `enquanto xp ≥ nível × 45: xp −= nível × 45; nível += 1`. Não há bônus de créditos nem bônus de fim de onda (substitui SDD-13 RF-13.1). O HP do núcleo cresce com o nível (RF-27.8).
- Não existe mais derrota de fase, `Result`, REPETIR, PRÓXIMA ou checkpoint anti-farm (substitui SDD-13 RF-13.6 e SDD-10 RF-10.3 `Result`).
- **Seed:** a seed do `Rng` é gerada na primeira partida, persistida e nunca reiniciada; o hash de SDD-13 RF-13.7 deixa de ser usado.

### RF-27.10: Regiões e prestígio "Migrar" (`PrestigeService`)

- **Disponibilidade:** `ondaMax` da fase atual ≥ **15**.
- **Reservas ganhas:** `floor( floor(ondaMax² / 170) × multReservasRegião )`, com a região **da partida que está terminando** (DP-27.12). Exemplo: ondaMax 37 → 8 (Brazil South).
- **Efeito da produção:** cada Reserva dá **+1% de produção de créditos permanente**, aditivo (`1 + 0,01 × Reservas`; RF-27.1).
- **Migrar reinicia** ondas, torres e créditos da fase atual (onda 1, sem torres, créditos iniciais) e **mantém** nível, XP, estrelas, perguntas respondidas (contadores do perfil; o ledger de SDD-26 passa a `attempt + 1`), fases desbloqueadas, regiões desbloqueadas, Reservas e missões do dia.
- **Regiões** (`regions.json`):

| id | HP dos inimigos | Reservas | desbloqueio |
|---|---|---|---|
| Brazil South (padrão) | ×1 | ×1 | sempre |
| East US | +40% (×1,4) | ×1,5 | na 1ª migração |
| West Europe | +100% (×2,0) | ×2,2 | com 30 Reservas acumuladas |

- A região é escolhida ao migrar entre as desbloqueadas.

### RF-27.11: Missões diárias (`MissionService`)

- **4 missões por dia** sorteadas de um pool de 6, com reinício à **meia-noite local** (data local em `yyyy-MM-dd`, persistida).
- **Sorteio:** PCG32 com seed derivada `hash(seedPersistida, aaaaMMdd)`. Não consome o `Rng` principal, e o resultado não depende da ordem dos eventos do dia.

| Missão | Meta | Recompensa |
|---|---|---|
| Responda 3 perguntas de chefe | 3 | baú raro |
| Escale 5 picos de demanda | 5 | 10 min de renda |
| Fique presente por 60 min | 60 min | 2 Reservas |
| Colete 10 caixas de suprimentos | 10 | 500 XP |
| Derrote 200 inimigos | 200 | 5 min de renda |
| Melhore torres 10 vezes | 10 | 300 XP |

- Contagem: "responda" conta cada pergunta de chefe respondida (acerto ou erro; tempo esgotado não conta); "picos" conta cliques que escalaram (SDD-26); "presente" soma minutos de estado Presente no dia (não precisam ser contínuos; SDD-25); "caixas" conta as coletadas (SDD-26); "derrote" conta mortes por torres, inclusive no farm; "melhore" conta melhorias manuais (RF-27.5).
- **Estados:** `Em andamento → Concluída → Coletada`. A recompensa só é entregue ao **coletar** (botão COLETAR da aba Missões). Badge do nav = **concluídas e não coletadas**.
- Missões concluídas e não coletadas ao virar o dia: coletadas automaticamente na virada (DP-27.13).
- Reservas de missão somam ao total e entram na produção na hora.

### RF-27.12: Multiplicadores (resumo)

| Origem | Aplica a | Valor |
|---|---|---|
| Reservas (RF-27.10) | créditos | `1 + 0,01 × Reservas` (aditivo entre Reservas) |
| Presença (SDD-25 RF-25.3) | créditos e XP | ×1,5 / ×2,0 / ×2,5 (Presente), ×0,25 (Ausente e Offline) |
| Região (RF-27.6) | HP dos inimigos | ×1 / ×1,4 / ×2,0 |
| Pico (SDD-26) | dano das torres | ×3 por 60 s |
| Acerto de chefe (SDD-26 RF-26.6) | HP do chefe, no spawn | ×0,4 |

Os multiplicadores de Reservas e de presença se **multiplicam** entre si.

### RF-27.13: Baús e "minutos de renda"

- **min de renda** = `taxaRenda` (RF-27.3, sem multiplicador de presença) × 60 × minutos, lida no momento da coleta.
- **Baú comum** (acerto de chefe, SDD-26): 5 min de renda + 200 XP.
- **Baú raro** (missão): 15 min de renda + 1 Reserva.
- O baú é entregue de uma vez (sem abertura interativa nesta ficha; animação em SDD-24).
- Os ganhos de baú **não** recebem multiplicador de presença (já são renda "sem presença").

### RF-27.14: Conteúdo (SDD-12 estendido)

Novos arquivos em `Content/`, embutidos e cobertos pelo `ContentHash`: `towers.json` (reescrito), `enemies.json` (reescrito), `waves.json` (perfil de mistura, intervalo, chefe a cada 20), `stages.json` (10 fases acima), `regions.json`, `missions.json`, `economy.json` (bases de custo, fatores 1,5/1,2/1,18/1,12/1,09/1,07/1,04, limiar de XP 45, núcleo 100 + 5, recuo 5, descanso 1,4 s, janela da taxa 10 s, créditos iniciais). O `ContentValidator` (SDD-12 RF-12.9) valida faixas, IDs e que `Σ` do perfil de mistura não exceda a quantidade. Os arquivos `map.strip-a.json` e o modo Clássico de SDD-12/13 ficam obsoletos (RF-27.15).

### RF-27.15: Conflitos com fichas anteriores (sem editar as fichas antigas)

| Ficha e RF | Situação |
|---|---|
| SDD-11 RF-11.1 | HP e dano passam a `double`; tabelas por multiplicação iterativa |
| SDD-11 RF-11.3, RF-11.5, RF-11.9 | `PathTable` e posicionamento 2D substituídos pelo mundo 1D e slots (ADR-020) |
| SDD-11 RF-11.4 (passos 1, 9) e RF-11.10 (`StartWave`) | ondas automáticas |
| SDD-11 RF-11.6, RF-11.7, RF-11.8 | números novos (RF-27.4 a RF-27.6) |
| SDD-11 RF-11.13 | o `Rng` passa a ser usado pelas regras (SDD-26) |
| SDD-12 RF-12.2 a RF-12.8 | mapa, regras, torres, inimigos, ondas e `bossWavesFor` substituídos |
| SDD-13 RF-13.1 a RF-13.7 | economia, modos, catálogo, estrelas, checkpoint e seed substituídos |
| SDD-10 RF-10.2, RF-10.3 | máquina de fases e `RunState.Result` simplificados |
| SDD-19 RF-19.9 | estrelas (substituída por RF-27.9) |

## 6. Requisitos não funcionais

- **RNF-27.1:** Toda a lógica desta ficha é pura, determinística e roda no Linux. Mesmo `seed`, conteúdo e comandos geram o mesmo `StateHash` entre execuções e SOs.
- **RNF-27.2:** Nenhum número de balanceamento no código C# fora do JSON de conteúdo (SDD-12 RNF-12.1).
- **RNF-27.3:** `Tick()` mantém 0 B alocados em regime e as metas de SDD-11 RNF-11.1 (meta; medição na E3 com os novos números).
- **RNF-27.4:** `CreditFormatter` sem alocação evitável: ≤ 1 string por chamada e cache por valor exibido na HUD.
- **RNF-27.5:** Cobertura de linhas ≥ 90% em `Economy`, `CreditFormatter`, `PrestigeService`, `MissionService` e `IdleWaveBuilder`.

## 7. Estratégia de implementação

1. `PowTable`, `CreditFormatter`, `RateMeter` e `Credits` com testes de tabela (Linux).
2. JSON de torres, inimigos, ondas e economia, mais `ContentValidator` atualizado.
3. Adaptar a `Simulation` ao mundo 1D, a `TowerSystem` (alcance 1D, Scale Set, área) e ao `IdleWaveBuilder` com ondas automáticas.
4. Núcleo, queda, recuo, XP, nível e fases (E3).
5. Estrelas, missões, prestígio e regiões (E6).
6. Teste de simulação: uma partida scriptada de 200 ondas com hash fixado.

## 8. Dependências e decisões pendentes

**Dependências:** SDD-11 (combate, adaptado), SDD-12 (conteúdo e validador). Consumidores: SDD-25 (presença e offline), SDD-26 (eventos) e SDD-24 (UI). SDD-21/22 fornecem slots e a faixa. SDD-14 grava o estado novo.

**Decisões pendentes** (lacunas reais; a ficha propõe um padrão para o Tech Lead confirmar):

| ID | Questão | Padrão proposto |
|---|---|---|
| DP-27.1 | Créditos iniciais da 1ª partida | 100 |
| DP-27.2 | Arredondamento dos custos | `ceil` |
| DP-27.3 | Venda, mudança de posição e limite por tipo de torre | inexistentes no v1 |
| DP-27.4 | HP e dano `double` em vez de `int` | aceito (RF-27.1, RF-27.4) |
| DP-27.5 | Consumo de tempo por onda vencida no offline | duração + 1,4 s (SDD-25) |
| DP-27.6 | Modos de mira na UI (sem velocidade de projétil: ADR-027, ADR-031) | só `First` na UI |
| DP-27.7 | Melhorias da auto-escala contam na missão "Melhore torres 10 vezes" | não |
| DP-27.8 | `índiceFase` 0-based (fase 01 = 0) | 0-based |
| DP-27.9 | HP do núcleo: `100 + 5 × (nível − 1)` ou `100 + 5 × nível` | `100 + 5 × (nível − 1)` (nível 1 = 100, como SDD-13) |
| DP-27.10 | Títulos das fases 08 a 10 | provisórios |
| DP-27.11 | Comportamento ao concluir a fase 10 | farm da onda 100 com Migrar disponível |
| DP-27.12 | Região usada no multiplicador de Reservas | a da partida que termina |
| DP-27.13 | Missão concluída e não coletada ao virar o dia | coleta automática na virada |
| DP-27.14 | O bônus de recompensa de level-up (+20 créditos em SDD-13) | removido |

## 9. Critérios de aceitação

- **CA-27.1 (T-27.1):** `CreditFormatter` passa na tabela (RF-27.2), incluindo promoção de sufixo, `long.MaxValue` e todos os tiers até `zz`.
- **CA-27.2 (T-27.2, T-27.3, T-27.4):** Custos de construção (45, 68, 102, … para o Raio; bases 65/90/400), custos de melhoria (L1→2 = 54 e L2→3 = 64 para o Raio) e dano(L) conferem com valores fixados à mão para L = 1, 2, 10, 100 e 999.
- **CA-27.3 (T-27.5):** HP, ouro e XP por onda conferem com a tabela fixada para as ondas 1, 20, 60 e 100, nas fases índice 0 e 9 e nas 3 regiões.
- **CA-27.4 (T-27.5, T-27.6):** `N(w)` e a mistura conferem nas ondas 1, 20, 60, 72 e 100; chefes só em 20, 40, 60, 80 e 100.
- **CA-27.5 (T-27.7):** Queda: recuo de 5 ondas, nunca abaixo de (último chefe + 1) nem de 1; núcleo restaurado; créditos intactos; `falls` incrementado.
- **CA-27.6 (T-27.8):** Conclusão da fase mantém torres e créditos, reinicia a onda em 1 e desbloqueia a seguinte; o Scale Set desbloqueia na entrada da fase 09; XP, nível (`enquanto xp ≥ nível × 45`) e HP do núcleo `100 + 5 × (nível − 1)` conferem (T-27.9).
- **CA-27.7 (T-27.8):** Estrelas 1, 2 e 3 nas fronteiras (1 acerto, 2 acertos, todos os acertos com e sem queda); as estrelas nunca diminuem.
- **CA-27.8 (T-27.11):** Prestígio: disponibilidade em ondaMax 14/15; `floor(ondaMax²/170)` com 15, 37 e 100; multiplicadores regionais; o que é mantido e o que é reiniciado.
- **CA-27.9 (T-27.10):** Missões: sorteio determinístico por dia (mesma seed e data → mesmas 4), virada à meia-noite local, badge, coleta e recompensas.
- **CA-27.10 (T-27.12):** `RateMeter` com FakeClock: janela de 10 s e as duas séries.
- **CA-27.11 (T-27.14):** Replay: partida scriptada de 200 ondas com `StateHash` final idêntico em 5 execuções, no Linux e no Windows (resultado **comprovado** ou **reprovado**).

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-27.1 | `CreditFormatter`: tabela completa | Linux |
| T-27.2 | `PowTable`: valores e ausência de `Math.Pow` | Linux |
| T-27.3 | Custos de construção e melhoria; saturação acima de `long.MaxValue/2` | Linux |
| T-27.4 | Torres: alcance 1D, super hit, lentidão com piso, área e metade vs `runner`, Scale Set com N Raios | Linux |
| T-27.5 | Inimigos e ondas: HP, ouro, XP, quantidade, mistura, intervalo, chefe a cada 20 | Linux |
| T-27.6 | Encadeamento: 1,4 s após limpar; sem comando `StartWave` | Linux |
| T-27.7 | Queda e recuo; vazamento; núcleo restaurado a cada onda | Linux |
| T-27.8 | Fases, conclusão, desbloqueios e estrelas | Linux |
| T-27.9 | XP, nível e HP do núcleo | Linux |
| T-27.10 | Missões diárias, badge e coleta | Linux |
| T-27.11 | Prestígio, regiões e Reservas | Linux |
| T-27.12 | `RateMeter` e minutos de renda | Linux |
| T-27.13 | Validador de conteúdo: JSON com cada erro possível | Linux |
| T-27.14 | Replay e hash (CA-27.11) | Linux/Windows |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Crescimento exponencial (1,18ᵏ de custo contra 1,12ᵏ de dano) travar a progressão ou estourar o `long` | Alta | Médio | Adição saturante; custos "inalcançáveis"; ajuste por JSON no playtest (E5) |
| Fases de 60 a 100 ondas e prestígio em onda 15 desbalanceados (ondaMax²/170) | Alta | Médio | Todos os números em JSON; playtest; DP-27.x |
| `double` para HP e dano divergir entre SOs | Baixa | Médio | Só `+ − × ÷` e tabelas por multiplicação iterativa; CA-27.11 |
| Mundo 1D mudar a densidade de inimigos por alcance | Média | Médio | Alcance em slots e velocidades em DIP/s no JSON |
| Fuso horário ou mudança de relógio alterar as missões | Média | Baixo | Data local persistida; sorteio por seed derivada |
| Perguntas insuficientes por tema para as 3 a 5 de chefe sem repetição (SDD-26) | Média | Médio | Validador de banco (SDD-26 RF-26.9) |
| Cálculo de "dano" do Scale Set com muitas torres Raio dominar o jogo | Média | Médio | Fator 0,15 em JSON; playtest |
