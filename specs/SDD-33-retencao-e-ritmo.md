# SDD-33: Retenção e ritmo (bênçãos de marco, metas e quase-recorde, respiro adaptativo, desafio diário)

**Status:** PROPOSTA (ficha SDD-33; só documento, sem código) · **Etapa:** ciclo "TD" · **Depende de:** SDD-28 (partida TD), SDD-29 (runas, corrida contínua §20), SDD-10/11 (núcleo determinístico, ADR-026), SDD-14 (persistência), SDD-24 (painel e cards), TD.30b/TD.30c (executadas antes) · **Referencia, sem conflitar:** SDD-32 (cartão de fim, em escrita por outro worker) e SDD-31 (torres Azure) · **Não edita** SDD-28/29/31/32, `DECISOES.md` nem código. Fichas de execução: TD.33a, TD.33b, TD.33c (§12).

> Todo número é **valor inicial ajustável por JSON** e **não medido** (README §2.3); entra em playtest e no `TdSanityBot` (TD.33a) antes de valer. Base de código descrita: HEAD `ca25fe1` (árvore com alterações locais de outras fichas em `TdMatchState.Bonus.cs`, `TdSanityBot.cs` e UI; o estado delas não é tratado aqui).

## 1. Objetivo e diagnóstico

Visão do usuário (memória `visao-jogar-no-trabalho`, critério de tudo): **discreto, infinito (delay 0, torres mantidas), cantinho, sem foco, boss key**. Feedback após jogar: *"tá faltando algo pra me prender; eu desgostei quando ficou difícil; tem que ser difícil-zinho"*.

Diagnóstico ancorado no código:

| Sintoma | Causa no código | Efeito |
|---|---|---|
| Sem decisão quando a curva aperta | `AwakenRune` sorteia a runa pelo `Rng` (`TdMatchState.Runes.cs`, `frontier[NextUInt % count]`); as torres só têm compra/venda/mira | O jogador não tem como "reagir" a um aperto; só gasta fragmentos às cegas |
| Perder não rende nada | Derrota encerra a corrida (`TdMatchState.Result.cs`); o perfil guarda recorde por **fase** (`BestScore`) e `InfiniteRuns` por seed, sem meta visível durante a corrida | Nada a "quase bater", nada a levar para a próxima corrida |
| Dificuldade é um muro | HP por onda `1,05^(onda−1)` **reinicia a cada fase** (`hpWaveGrowth`, `ScaledHp`), mas cada fase soma +5% (`hpStageStep`) e **cada volta completa soma ×1,25** (`CycleHpGrowth`, multiplicação iterativa); a renda é linear (`waveBonusBase` 20 + `waveBonusPerWave` 4 por onda; runas `credits` +3) e o campo tem teto de 44 inimigos (`maxEnemiesOnField`) | Depois de 1–2 ciclos o HP cresce exponencial contra renda linear: a transição "ok → impossível" é abrupta e não há válvula |

Esta ficha acrescenta **cinco peças**, todas **opcionais por `TdMatchOptions`** (§9) e sem depender das torres do SDD-31: **(A)** bênçãos de marco; **(B)** metas e quase-recorde; **(C)** respiro adaptativo; **(D)** desafio diário; **(E)** dinheiro muito visível (pedido do usuário: *"eu gosto de ver meu dinheiro subindo"*, só apresentação). Princípio: o jogador ganha **decisões raras e baratas** (uma a cada ~5 ondas, sem pausa) e **motivos para uma corrida a mais**, sem virar "modo fácil" nem exigir atenção.

## 2. Terminologia

- **Onda da corrida** `RunWave` (G): contador acumulado de ondas **concluídas** desde o início da corrida, atravessando fases e ciclos (fases 1–6 somam 77 ondas por ciclo: 10+12+12+14+14+15). Hoje só existem `WaveNumber` (dentro da fase) e `StageNumber`; `RunWave` é novo (RF-33.15).
- **Marco de bênção:** G múltiplo de 5, **e** a primeira onda de chefe concluída da corrida (chefes hoje: fim das fases 3, 5 e 6, ondas 12, 14 e 15; o 1º é G = 34).
- **Recorde:** `BestRunWave` do perfil (novo, §RF-33.13): maior G já alcançado em uma corrida.

## 3. Escopo e exclusões

**Dentro:** catálogo e fluxo de bênçãos; dinheiro visível (peça E, só apresentação); chip de meta, marcos e linha de quase-recorde; respiro pós-chefe e ajuste adaptativo; desafio diário local; opções de partida e seed; perfil v4; bots; fichas.

**Fora:** rede, ranking online, relógio real dentro do Core; novos inimigos, torres ou fases (SDD-31); reabrir custo crescente, runas de fragmentos ou economia (SDD-29); qualquer texto de ensino/quiz (RF-28.17); alterar `hpWaveGrowth`/`CycleHpGrowth` (só **medir** e propor, DP-33.6).

## 4. Peça A: Bênçãos de marco

### RF-33.1: Gatilho e oferta

- Ao **concluir** a onda G de marco (evento `WaveCompleted`, processado no mesmo passo, antes do `BeginWave` da próxima), o Core cria **uma oferta** com **3 cartas** e emite `BlessingOffered` (A = índice da oferta k, B = G).
- Há **no máximo 1 oferta pendente**. Se surgir um novo marco com oferta ainda pendente, a antiga é **resolvida pela regra de expiração** (RF-33.3) **antes** de abrir a nova.
- Corrida contínua (`ContinuousWaves`): a oferta **não pausa nem atrasa** a onda seguinte (delay 0 preservado).
- **CA-33.1:** em G = 5, 10, 15 … e na 1ª onda de chefe concluída (G = 34, que não é múltiplo de 5), há exatamente uma oferta; G = 34 **e** múltiplo de 5 não gera duas; `Classic` e corrida sem `Blessings` nunca geram oferta (T-33.1).

### RF-33.2: Catálogo (14 bênçãos, só o que o Core já tem)

Só usa: 5 torres (`bolt`, `frost`, `cannon`, `balancer`, `scaleset`: ids e `stats` conferidos na TD.33a), runas (canais `rangeBp`, `boltDamageBp`, `cannonDamageBp`, `frostSlowTenths`, `abilityCooldownBp`, `firewallTenths`, `waveCredits`, `integrity`), créditos, fragmentos, habilidades e integridade do núcleo. **Reusa os canais de efeito das runas** (soma em bp com o valor da runa), sem mecânica nova.

| Id | Nome | Efeito inicial | Duração | Pilha máx. | Cat. |
|---|---|---|---|---|---|
| `bolt` | Raio afiado | +15% dano do Raio (`boltDamageBp` +1500) | corrida | 2 | Torre |
| `frost` | Gelo profundo | +0,5 s de lentidão do Gelo (`frostSlowTenths` +5) | corrida | 2 | Torre |
| `cannon` | Canhão pesado | +15% dano do Canhão (`cannonDamageBp` +1500) | corrida | 2 | Torre |
| `balancer` | Nós firmes | +1 inimigo retido por nó do Balanceador (`nodeHolds` +1) | corrida | 1 | Torre |
| `scaleset` | Aura maior | +20% do bônus de aura do Scale Set | corrida | 2 | Torre |
| `range` | Mira longa | +6% de alcance em todas as torres (`rangeBp` +600) | corrida | 2 | Geral |
| `gift` | Torre grátis | recebe créditos = preço da próxima compra da torre **mais barata** desbloqueada (não altera a tabela de custo nem a venda) | única | 3 | Geral |
| `vault` | Cofre de renda | renda de fim de onda (`WaveBonus` e `waveCredits`) **×2** nas próximas 3 ondas | 3 ondas | 1 | Geral |
| `reserve` | Fundo de reserva | +créditos = 5 × o `WaveBonus` da próxima onda | única | 2 | Geral |
| `mend` | Reparo | +3 de integridade (limitado ao máximo) | única | 3 | Geral |
| `plating` | Núcleo reforçado | +2 de integridade **máxima** e +2 de integridade | corrida | 2 | Geral |
| `shard` | Fragmentos | +2 fragmentos de runa (o despertar segue sorteado, RF-29) | única | 3 | Geral |
| `haste` | Recarga rápida | −10% de recarga das habilidades (`abilityCooldownBp` −1000) | corrida | 2 | Geral |
| `ward` | Escudo de onda | o **próximo** vazamento não causa dano ao núcleo (1 carga; não anula o "sem vazar" da runa) | até usar | 1 | Geral |

- **Pilha:** a mesma bênção soma até a "Pilha máx." e depois sai do pool. **Teto agregado** por canal: ≤ +45% de dano por torre e ≤ +12% de alcance somando runas e bênçãos (RNF; evita monocultura).
- **Elegibilidade (igual para todos os jogadores no mesmo ponto da corrida):** carta de categoria **Torre** só entra se a torre estiver **desbloqueada** pelo estágio atual (`unlockStage`), **não** se o jogador a possui (assim a oferta independe das compras, importante para o diário, RF-33.11).
- **CA-33.2:** cada efeito soma exatamente o valor da tabela no canal correto; `gift`/`reserve`/`mend` pagam uma vez; `vault` dobra exatamente 3 conclusões de onda; `ward` é consumida no 1º vazamento e não existe vazamento anulado sem carga; teto agregado respeitado (T-33.2).

### RF-33.3: Escolha, não bloqueio e expiração

- **Comando novo** `ChooseBlessing(int Slot)` (Slot 0–2), entra no log; rejeitado sem oferta pendente (`NotAvailable`) ou com slot inválido, **sem alterar o estado nem o `Rng`**. Vale em Onda, em Preparo e em pausa; **não troca de fase** e **não consome tick**.
- **A carta 0 é sempre da categoria Geral** (nunca "vazia"): assim a expiração é inofensiva.
- **Expiração (proposta, DP-33.2):** a oferta tem `offerTicks` = **60 s de mundo** (1.800 ticks), que **só correm com o usuário presente** (usa o `SetPresence` já no log, `TdMatchState.Presence.cs`; ausente/boss key = relógio parado, a oferta espera). Ao zerar, vale a **carta 0** e o evento `BlessingChosen` sai com `B = auto`. Escolher cedo não dá vantagem de tempo; não escolher não custa nada além de abrir mão da decisão. (O briefing sugeria 30 s: 60 s é a recomendação porque o jogador está trabalhando; o valor é `rules.blessings.offerSeconds`.)
- **CA-33.3:** escolher aplica o efeito e fecha a oferta; sem escolha, após 1.800 ticks presentes aplica a carta 0; ausente por 10 min a oferta continua intacta; escolha sem oferta é rejeitada sem mudar o hash (T-33.3).

### RF-33.4: Sorteio determinístico e ordem de consumo do `Rng`

- **Fluxo próprio por oferta**, que **não consome** o `_rng` principal (nem o de `BeginWave`/`AwakenRune`): `offerRng(k) = Rng.Seed(_blessSeed, 0x3300 + k)`, onde `_blessSeed` é o `State` do `_rng` principal **lido** (não avançado) em `Create` e `k` é o índice da oferta (1, 2, …).
- Algoritmo (ordem **fixa e documentada**): (1) monta o pool elegível **na ordem do catálogo**, pilha não cheia; (2) carta 0 = `NextUInt() % n` entre as de categoria Geral; (3) carta 1 e carta 2 = `NextUInt() % n` no pool restante, **sem repetição** (remoção do escolhido, partial Fisher-Yates). Total: **3 `NextUInt` por oferta** (menos se o pool tiver < 3 cartas: fallback `mend`, `shard`, `gift`, que nunca saem do catálogo por pilha dupla: pilha 3).
- Por ser **por índice de oferta**, a oferta k **não depende** do histórico de escolhas anteriores além do que a pilha exclui; o `StateHash` só ganha o índice `k` (RF-33.15), não um estado de `Rng`.
- **CA-33.4:** mesma seed ⇒ mesmas ofertas; o hash do `_rng` principal ao fim de uma corrida **sem** bênçãos é idêntico ao de hoje, e com bênçãos o `_rng` principal avança **igual** (as ofertas não o tocam); 3 `NextUInt` por oferta (T-33.4).

## 5. Peça B: Metas e quase-recorde

### RF-33.5: Chip com o próximo marco

- O chip atual ("Fase N · Onda n/Total", TD.28b) ganha **uma segunda linha discreta**, fonte menor, texto em baixo contraste: `onda 20 · faltam 3 para o recorde` (G e distância a `BestRunWave`); depois do recorde: `onda 41 · recorde novo`; sem recorde (1ª corrida): `onda 7 · próximo marco: 10`. **Nunca** pisca nem anima; atualiza na troca de onda. Texto ≥ 11 DIP (é legenda do chip, não ação).
- **CA-33.5:** G = 20 com `BestRunWave` = 23 mostra "faltam 3 para o recorde"; G ≥ recorde mostra "recorde novo"; com `BestRunWave` = 0 mostra o próximo marco; o chip não ganha foco nem altera a hit-test (T-33.5).

### RF-33.6: Marcos de onda com recompensa pequena

- Marcos **10, 25, 50, 100** (e a cada 100 depois): ao concluir a onda G, o Core paga `milestoneFragments` (1, 1, 2, 3) e `milestoneCredits` (= 3 × `WaveBonus` da onda) e emite `MilestoneReached` (A = G, B = fragmentos). Aviso **discreto de 4 s** no cantinho (mesmo estilo do `StageCleared`); som **opcional** só como ideia (§13).
- Marcos de recompensa e de bênção coexistem (G = 10 e G = 50 pagam recompensa **e** abrem oferta).
- **CA-33.6:** G = 10, 25, 50, 100 pagam exatamente o valor da tabela uma vez; replays idênticos; sem marco em Classic (T-33.6).

### RF-33.7: Quase-recorde no cartão de fim

- Contrato com o SDD-32 (cartão de fim, **em escrita**): o Core expõe `RunWave` e o resumo da corrida; o **host** injeta `BestRunWave` (leitura do perfil). O cartão ganha **uma linha** `Faltaram N ondas para o recorde` quando `RunWave < BestRunWave` e `Recorde novo: onda G!` quando supera; nada mais. A ficha TD.33b **só acrescenta essa linha se o cartão do SDD-32 já estiver mergeado**; caso contrário entrega o dado (`TdRunSummary.RunWave`) e a linha fica pendente sem bloquear.
- **CA-33.7:** com recorde 30 e derrota em G = 27, o texto traz "Faltaram 3 ondas"; derrota em G = 31 traz "Recorde novo"; o perfil só grava `BestRunWave` quando supera (T-33.7).

## 6. Peça C: Respiro adaptativo

### RF-33.8: Onda de alívio pós-chefe (autoral, visível)

- Depois de **cada chefe** (onda com inimigo de traço `boss` concluída), a **próxima onda** tem HP dos não-chefes **−20%** (`reliefHpBp` 8000), `bonusFactor` mínimo 1,5 (como as ondas `rest` do `td-waves.json`) e **+1 fragmento** se concluir sem vazar. Aviso discreto "Respiro" de 4 s (é **autoral e honesto**: ritmo do jogo, não "modo fácil").
- **CA-33.8:** a onda seguinte a cada chefe tem o HP −20% dos não-chefes; a segunda onda depois do chefe volta ao normal; o fragmento só sai sem vazar (T-33.8).

### RF-33.9: Ajuste suave da próxima onda (invisível)

- No `BeginWave`, o Core calcula `paceBp` a partir das **últimas 3 ondas concluídas** (janela fixa; menos de 3 = o que houver), contando **ondas com vazamento** `L` (`TdActiveWave.Leaked` já existe):

| L na janela | Ajuste de HP dos não-chefes |
|---|---|
| 0 (janela limpa, 3 ondas) | **+4%** (`paceBp` 10400) |
| 1 | 0% |
| 2 | **−5%** (9500) |
| 3 | **−10%** (9000) |

- **Piso 0,90 e teto 1,04** do multiplicador total; aplicado por **inteiros** (`hp × paceBp / 10000.0` sobre o valor de `ScaledHp`, sem `Math.Pow`); fixado em `TdActiveWave.HpFactorBp` (entra no hash) para a onda **inteira**, mesmo com sobreposição. **Chefes não escalam** (como hoje).
- **Nunca visível** como "modo fácil": sem texto, ícone, cor ou dica; a pré-visualização de onda mostra tipos e contagens (não HP); o recorde e as metas contam em **ondas**, então um jogador mais leve **não** é mais premiado.
- Preserva "difícil-zinho": só o **lado de baixo** é apertado (+4%) quando o jogador vai bem, e o lado bom (−10%) quando está afundando; **não muda** `hpWaveGrowth` (o muro exponencial segue; ver DP-33.6).

### RF-33.10: Defesa contra "vazar de propósito"

Vazar para ganhar −10% custa muito mais do que dá. Camadas, todas obrigatórias:
1. **Gate de integridade:** o ajuste **negativo só vale com `Integrity ≤ 60%` do máximo** (`reliefGateBp` 6000); com o núcleo cheio, vazar de propósito não rende nada.
2. **Custo real do vazamento:** dano ao núcleo (finito e só se cura por fase `StageClearHeal`, `mend`, Snapshot), perde `waveBonus` de "sem vazar" (`noLeakBase`/`noLeakPerWave`), perde o bônus do Cofre e a runa "sem vazar" da fase (`_hasLeaked`).
3. **Janela com memória:** depois de aplicar −5% ou −10%, o **+4% retorna** na onda seguinte se ela não vazar, e a janela é de **3 ondas**: alternar vazar/não vazar fica em média ≥ 0% (L=1 dá 0%).
4. **Efeito pequeno e teto:** no máximo −10% de HP, contra +25% do ciclo e +5% da fase; o piso de 0,90 não cresce com ondas de vazamento seguidas.
5. **Desafio diário:** o ajuste **adaptativo é desligado** (RF-33.11), para a comparação de "melhor onda do dia" ser limpa.
- **CA-33.9/10:** (a) janela limpa dá ×1,04, L=2 com integridade ≤ 60% dá ×0,95, L=3 ×0,90; (b) o mesmo L com integridade > 60% dá ×1,0; (c) multiplicador nunca fora de [0,90; 1,04]; (d) bot "vazador" (vaza 1 onda a cada 3 de propósito) **não** supera em ondas alcançadas o mesmo bot sem vazar (T-33.9/T-33.10).

## 7. Peça D: Desafio diário (só local)

### RF-33.11: Seed do dia e regras

- `dayNumber = floor((agoraUTC − 3 h) / 24 h)` em dias desde 1970-01-01 (**fuso fixo UTC−3**, sem tabela de fusos; o Brasil não tem horário de verão desde 2019). **Só o host** lê o relógio; o Core recebe `dayNumber` em `TdMatchOptions` (RNF: Core sem relógio real).
- `SeedState = Mix64(dayNumber ^ 0x5D1A7E33)` (SplitMix64, sem `Math.Random`), `SeedStream = 33`; `Rng.Seed(SeedState, SeedStream)` é o `Rng` de `Create`. Todos os jogadores do mesmo dia compartilham o `Rng` inicial; as ondas são **autorais** (JSON) e os spawns e jitters saem do `_rng`; as **ofertas** saem do fluxo por índice (RF-33.4) e a elegibilidade independe de compras ⇒ **mesmas ondas e mesmas ofertas** (a pilha cheia pode trocar uma carta; o sorteio base é o mesmo).
- Regras do diário: começa na fase 1, `Retention` completo **exceto** o ajuste adaptativo (RF-33.10, item 5), sem tutorial, sem pergunta-bônus que pague créditos (a pergunta é por tempo real e quebraria a comparação; DP-33.5), corrida contínua.
- **CA-33.11:** o mesmo `dayNumber` produz a mesma sequência de ondas, spawns e ofertas; 23:59 e 00:01 (UTC−3) caem em `dayNumber` consecutivos; a mudança do fuso do SO não altera o dia (T-33.11).

### RF-33.12: Melhor onda do dia, sequência e recorde pessoal

- **Melhor onda do dia:** maior G do `dayNumber` corrente. **Sequência (streak):** conta o dia quando a corrida diária chega à **onda 5** (evita contar abrir e fechar): se `dayNumber == último+1` soma 1; se `== último` mantém; se maior que `último+1` volta a 1; se **menor** que `último` (relógio recuado) **ignora**. **Recorde pessoal diário** = melhor G de qualquer dia + maior sequência.
- Sem rede: tudo no perfil local; **editável pelo usuário** (não é objetivo defender contra edição do arquivo local).
- **CA-33.12:** dias consecutivos somam, um dia de pausa zera para 1, retrocesso de relógio não altera; recordes só sobem (T-33.12).

### RF-33.13: Persistência (`SideWar.Persistence`, perfil TD)

- `TdProfileData.SchemaVersion` **3 → 4** (`sidewar.td-profile`), migração `V3ToV4` em `TdProfileMigrations` (registrar `.Register(Schema, 3, V3ToV4)`; **só acrescenta campos com padrão**, nunca remove; arquivo v3 abre sem perda; `.bak` e quarentena do `VersionedDocumentStore` inalterados).
- Campos novos: `BestRunWave` (int), `Daily` { `LastDay`, `Streak`, `BestStreak`, `BestWaveEver`, `Days`: lista de { `Day`, `BestWave` } **podada em 30 dias**, `BestRunWave`/diário gravados **só quando superados** e também a cada 5 ondas (sobrevive a queda do app) }.
- O campo legado `InfiniteRuns` (melhor onda por seed) permanece; o diário **não** o reaproveita (cresceria sem limite): recomendação em DP-33.4.
- **CA-33.13:** v3 abre como v4 com zeros; gravar e reler preserva tudo; `Days` mantém só 30 dias; `BestRunWave` nunca desce (T-33.13).

### RF-33.14: Opções de partida e replay

- `TdMatchOptions` (hoje `ContinuousWaves`, `PersistentRun`, `StageClearHeal`) ganha, **no fim** do record: `bool Blessings = false`, `bool Milestones = false`, `bool Relief = false` (RF-33.8), `bool AdaptivePace = false` (RF-33.9) e `int DailyDay = 0` (0 = corrida normal; >0 = desafio, só rótulo e regra de RF-33.11, não entra no `Rng`). Preset novo **`TdMatchOptions.Retention`** = `Default` + as quatro flags `true`; **`Default` e `Classic` não mudam** (DP-33.1).
- `TdReplayScript` já carrega `SeedState`, `SeedStream` e `Options`; o diário usa esses campos (log do diário reproduz com a mesma seed e `DailyDay`). `Options` nulo continua = `Classic` (logs antigos intactos). `ChooseBlessing` é um novo `IGameCommand` serializável no log.
- **CA-33.14:** `TdReplayRunner` reproduz um diário e uma corrida com bênçãos com o mesmo `StateHash`; script sem `Options` mantém o hash antigo (T-33.14).

## 7b. Peça E: Dinheiro muito visível (só apresentação)

Pedido do usuário. **Nada desta peça entra no Core, no `StateHash`, no `Rng` nem no log** (RF-33.23): lê `Credits`, os eventos já emitidos (`EnemyKilled`, `WaveBonus`, `BonusAnswered`, bônus de Cofre/marco) e o relógio de **apresentação** do Runtime.

### RF-33.18: Contador de créditos grande, com contagem animada

- O valor de créditos no chip usa fonte **≥ 16 DIP**, cor **âmbar** vinda da `Palette` (token existente de créditos; sem cor literal), **números tabulares** (largura fixa por dígito, sem o chip "dançar"). A exibição **sobe suavemente** até o novo valor (ease-out, ≈ 400 ms; ganhos grandes limitam-se a 600 ms) em vez de saltar; **queda** (compra) é imediata e sem animação.
- O valor **real** (o do Core) é sempre o que vale para comprar/validar; a animação é só o número desenhado e **nunca atrasa** nenhuma ação.
- **CA-33.18:** +500 com valor exibido 1.000 passa por valores intermediários crescentes e termina exatamente no valor do Core em ≤ 600 ms; compra reduz na hora; fonte ≥ 16 DIP, tabular e âmbar da `Palette` (T-33.18).

### RF-33.19: Números flutuantes "+N" agregados

- Ao ganhar créditos (abate, bônus de onda, pergunta-bônus, "sem vazar", Cofre, marco) surge um **"+N" pequeno** (≥ 11 DIP, âmbar com transparência) que **sobe ≈ 12 DIP e some em ≈ 700 ms** perto do contador. **Agregação:** ganhos dentro de **400 ms** somam num único número (para 40 abates numa onda não haver 40 textos); no máximo **3 números simultâneos**; os excedentes se fundem no mais recente.
- Janela de desenho **sem foco, click-through** (como o resto do cantinho); nenhum número sobre a faixa de torres (não cobre jogo nem a caixa de digitação do usuário).
- Em **modo discreto** (RF-33.21) só os "+N" de ganhos **grandes** (≥ `bigGainCredits`, padrão 50) aparecem.
- **CA-33.19:** 10 abates em 300 ms geram 1 número com a soma; ganhos separados por > 400 ms geram números distintos; nunca mais de 3 vivos; não recebe foco nem cliques (T-33.19).

### RF-33.20: Indicador de renda "+X/s" (opcional)

- Média móvel de **~10 s de relógio de jogo** (`Tick`, não relógio real; 300 ticks): `renda = (créditos ganhos na janela) / 10`, só **ganhos** (compras e vendas não entram), mostrada discreta, abaixo do contador: `+12/s`. Opção **`td.showIncome`**, padrão **desligado** (Configurações). Pausa/ausência/boss key não contam (o `Tick` não anda).
- **CA-33.20:** 300 créditos ganhos uniformes em 10 s de jogo mostram `+30/s`; compra não altera o valor; opção desligada não desenha nem calcula (T-33.20).

### RF-33.21: Brilhos de "dá para comprar" e de patrimônio; modo discreto

- **"Dá para comprar":** quando `Credits ≥ custo` da **torre selecionada** (ou, sem seleção, da **mais barata desbloqueada**, preço da curva atual `towerCostTable`) e **antes não era**, o botão Torres ganha **um brilho âmbar de ≈ 800 ms**. **Histerese:** só dispara de novo depois de os créditos caírem abaixo do limiar (cruzamento ↑ por vez); nunca pisca em loop.
- **Patrimônio:** ao cruzar **1.000, 10.000 e 100.000 ¢** (depois a cada ×10), o contador ganha **um brilho de ≈ 1 s**; cada marco **uma vez por corrida**.
- **Modo discreto** (`td.discreetMode`, novo, padrão desligado): desliga brilhos e a contagem animada (valor salta), mantém "+N" só nos grandes e o contador grande; é o respiro para o usuário que quer o mínimo de movimento.
- **CA-33.21:** crédito 99→100 com custo 100 dispara 1 brilho; ficar acima não repete; cair para 80 e voltar a 100 dispara outro; 999→1.000 brilha o contador uma vez; modo discreto não anima (T-33.21).

### RF-33.22: Boss key, visibilidade e custo de CPU

- **Boss key** (Ctrl+Alt+H): contador animado, "+N", brilhos e renda **param e somem**; ao voltar, o contador aparece **no valor real, sem animar** a diferença acumulada. **Sem custo oculto:** com o jogo oculto, em ausência de render ou com render parado (TD.28c), **nenhum timer de animação roda**; as animações só agendam quadros enquanto houver algo a animar (idle: 0 redesenhos extras) e **respeitam o fps estável do TD.28c** (sem elevar a taxa só por causa do dinheiro).
- **CA-33.22:** com boss key ligada, o número de ticks de animação é 0; ao desligar, o contador mostra o valor real imediatamente; com contador estável, nenhum redesenho agendado pela peça E (T-33.22).

### RF-33.23: Só apresentação (determinismo)

- Nenhum estado novo em `TdMatchState`, nenhum comando, evento ou campo no hash; o valor exibido, a fila de "+N", a média de renda e os limiares vivem no Runtime/UI (modelo de apresentação, `TdUiModel`). Reiniciar/replay não depende deles: o replay reproduz o mesmo hash **com a peça E ligada ou desligada**.
- **CA-33.23:** o `StateHash` de uma corrida é idêntico com a peça E ligada, desligada e em modo discreto; `git diff` da TD.33d não toca `src/SideWar.Core` (T-33.23).

## 8. Requisitos transversais

### RF-33.15: Determinismo, `StateHash` e `Rng` (ADR-026)

- **Compatibilidade:** com **todas** as flags falsas o `ComputeStateHash` e o fluxo do `Rng` são **byte a byte idênticos** aos de hoje: os campos novos só entram no hash **quando alguma flag está ligada** (bloco condicional no fim de `TdMatchState.Hash.cs`, depois de `_rng`). Goldens e testes existentes ficam intactos (`Default`/`Classic`).
- **Campos novos no hash (flags ligadas):** `RunWave`, `_offerIndex` (k), oferta pendente (3 ids + ticks restantes), lista de bênçãos ativas (id + pilha + ondas restantes + cargas), `_pendingReliefWave`, `TdActiveWave.HpFactorBp`, contador de janela (as 3 últimas ondas: vazou/não). Os contadores informativos (marcos mostrados, textos) ficam **fora** do hash.
- **`Rng` principal:** nenhuma peça consome (§RF-33.4); `AwakenRune`, `BeginWave` e jitter seguem na mesma ordem. **`Math.Pow` proibido**; bp inteiro, ordem de iteração fixa.
- **Ordem no passo de conclusão de onda (fixar):** `CompleteWaves` → `WaveBonus`/runas → recompensa de marco (RF-33.6) → expiração/abertura de oferta (RF-33.1) → respiro/ajuste da próxima onda → `SettlePhase`/`BeginWave`.

### RF-33.16: Impacto nos bots

- `TdSanityBot` ganha política de bênção: **carta 0** (baseline "inofensiva") e **heurística** (prefere `mend` com núcleo ≤ 60%, `range`/torre do tipo mais comprado senão); emite `ChooseBlessing` no log **com atraso fixo** de 5 s de mundo, determinístico. Roteiros do `TdBotPlans` não mudam.
- Medição (TD.33a, 20 seeds, como TD.22): ondas alcançadas na corrida contínua com e sem `Retention`; **meta de projeto** (a medir): bênçãos elevam a mediana de `RunWave` de forma **moderada** (não infinita) e o ajuste adaptativo **reduz a variância** (menos mortes súbitas), sem alterar a patente.
- **CA-33.16:** o bot completa 20 seeds com `Retention` sem rejeições de comando; o bot "vazador" (RF-33.10) não ganha; nenhuma corrida de 150 ondas tem `RunWave` incoerente (T-33.16).

### RF-33.17: UI discreta (peça A e avisos; TD.33b)

- **Cantinho, sem foco, sem pausar:** janela `no-activate` como o HUD/chip; **3 cards legíveis ≥ 14 DIP** (nome em ≥ 14 DIP, 1 linha de efeito curta), lado a lado acima da faixa, ao lado do chip; **hover** mostra a descrição completa (tooltip do HUD, TD.14); **clique** escolhe; sem animação que roube olhar (um brilho de 1 s, ideia §13). Após escolher, os cards somem; com oferta pendente, um **ponto** no chip lembra (sem som).
- **Boss key** (`TdBossKey`, Ctrl+Alt+H): esconde os cards, o relógio da oferta fica parado (RF-33.3), nada acumula ao esconder.
- **Atalhos opcionais** (DP-33.7): `hotkeys.PickBlessing1/2/3` globais para escolher sem mouse (sem foco); padrão desligado.
- **CA-33.17:** com oferta, os cards aparecem sem tomar foco do app ativo; o hover mostra o texto; a escolha por clique ou atalho envia `ChooseBlessing`; com boss key ligada nada é desenhado e o timer não corre (T-33.17).

## 9. Riscos

| # | Risco | Mitigação |
|---|---|---|
| R1 | Bênçãos empilhadas quebram o balanceamento (monocultura de dano) | Pilha máx. por carta, teto agregado por canal (RF-33.2), 14 cartas simples; bots medem antes de valer |
| R2 | Oferta ignorada vira "perde-perde" para quem trabalha | Timer só com presença, carta 0 sempre Geral e boa, nunca bloqueia |
| R3 | Ajuste adaptativo sentido como "dificuldade que mente" | Invisível, pequeno (−10%/+4%), com gate de integridade; recorde em ondas |
| R4 | Explorar o ajuste vazando de propósito | RF-33.10 (gate 60%, custo real, memória, teto, desligado no diário) |
| R5 | Mudar o hash quebra goldens/replays | Campos só com flag; `Default`/`Classic` intactos (RF-33.15) |
| R6 | Relógio do SO manipulado (streak/diário) | Tudo local; não há prêmio ligado; relógio recuado ignorado (RF-33.12) |
| R7 | Conflito de arquivos com TD.30/32/31 | Ordem global (§12.4) e arquivos distintos em TD.33a/b/c |
| R8 | O "muro" real (×1,25 por ciclo contra renda linear) persiste | Bênçãos de renda + respiro ajudam; **medir** e decidir `CycleHpGrowth` na DP-33.6 |
| R9 | Pool de oferta com < 3 cartas | Fallback `mend`/`shard`/`gift` (pilha 3), teste dedicado |
| R11 | Peça E gasta CPU/redesenho ou distrai | Animação só quando há algo a animar, parada com boss key/oculto, modo discreto, 3 números no máximo (RF-33.19/22) |
| R10 | Perfil v4 e arquivo v3 de outra versão do app | Migração só acrescenta campos; `.bak` do store mantido |

## 10. Cenários de teste

| Id | Descreve |
|---|---|
| T-33.1 | Ofertas em G = 5, 10, …, e na 1ª onda de chefe (G=34); nenhuma sem flag |
| T-33.2 | Cada uma das 14 bênçãos aplica o efeito no canal certo; pilha e teto agregado; `ward`/`vault`/`gift` |
| T-33.3 | `ChooseBlessing` válido/ inválido; expiração em 1.800 ticks presentes; ausente = relógio parado |
| T-33.4 | Mesma seed = mesmas ofertas; 3 `NextUInt` por oferta; `_rng` principal inalterado; pool curto |
| T-33.5 | Chip: "faltam N", "recorde novo", sem recorde; sem foco |
| T-33.6 | Marcos 10/25/50/100 pagam uma vez; coexistem com a oferta |
| T-33.7 | Linha de quase-recorde no cartão do SDD-32 (quando mergeado) e gravação de `BestRunWave` |
| T-33.8 | Onda de respiro pós-chefe (−20%, fragmento sem vazar) |
| T-33.9 | Ajuste adaptativo: tabela, piso 0,90/teto 1,04, chefes intactos |
| T-33.10 | Bot vazador não supera o bot normal; gate de 60% |
| T-33.11 | Mesmo dia = mesma seed/ondas/ofertas; fronteira UTC−3; diário sem adaptativo |
| T-33.12 | Streak (+1, mantém, zera, relógio recuado); recordes |
| T-33.13 | Migração v3→v4, round-trip, poda de 30 dias |
| T-33.14 | Replay do diário e de corrida com bênçãos; `Options` nulo = hash antigo |
| T-33.15 | Hash idêntico ao de hoje com todas as flags falsas (golden antigo passa) |
| T-33.16 | 20 seeds do bot com `Retention` |
| T-33.17 | UI: cards ≥ 14 DIP, hover, boss key, atalhos |
| T-33.18 | Contador grande e contagem animada (valor final exato, ≤ 600 ms) |
| T-33.19 | "+N" agregados em 400 ms, máx. 3, sem foco |
| T-33.20 | Renda `+X/s` (janela de 300 ticks), opção desligada |
| T-33.21 | Brilho "dá para comprar" com histerese; marcos de patrimônio; modo discreto |
| T-33.22 | Boss key: 0 animações, volta sem animar; 0 redesenho em idle |
| T-33.23 | `StateHash` igual com a peça E ligada/desligada; Core intocado |

## 11. Critérios de aceitação gerais

1. Todos os CA-33.x (A a E) passam; testes existentes (Core, Runtime, Persistence, goldens TD e idle) **sem alteração de baseline** com `Default`/`Classic`.
2. Com `Retention`, 20 seeds do bot rodam com replay idêntico (mesmo `StateHash` em 2 execuções).
3. Nenhuma peça tira foco, pausa a corrida ou exige clique para a onda seguinte começar.
4. Nenhum número entra como "medido": meta de bot é **medida** na TD.33a.

## 12. Divisão em fichas

### 12.1 Resumo e dependências

| Ficha | Escopo | Depende de | Ambiente |
|---|---|---|---|
| **TD.33a** | **Core:** `RunWave`; catálogo e efeitos (`td-blessings.json` + modelo + validador); oferta/`ChooseBlessing`/expiração; marcos (RF-33.6); respiro e ajuste (RF-33.8/9/10); `TdMatchOptions` (+ preset `Retention`) e hash condicional; `TdReplay`; `TdSanityBot` (política de bênção); testes T-33.1 a 33.4, 33.6, 33.8 a 33.10, 33.14 a 33.16 | TD.30b, TD.30c, TD.32a/b | 🟢 |
| **TD.33b** | **UI:** cards das bênçãos (≥ 14 DIP, hover, ponto no chip), chip de meta, avisos de marco e respiro, linha de quase-recorde (RF-33.7, se o cartão do SDD-32 existir), atalhos opcionais, boss key; evidência visual; T-33.5, 33.7, 33.17 | TD.33a, TD.32b | 🟡 |
| **TD.33d** | **Dinheiro visível (peça E, só UI/Runtime, não toca o Core):** contador grande âmbar tabular com contagem animada, "+N" agregados, renda `+X/s` (`td.showIncome`), brilhos de compra e patrimônio, `td.discreetMode`, respeito à boss key e ao fps do TD.28c; T-33.18 a 33.23 | TD.33b (mesmo chip/renderer; segue depois da TD.33c para não disputar `TdUiRenderer.cs`) | 🟡 |
| **TD.33c** | **Desafio diário:** perfil v4 e migração (RF-33.13), relógio UTC−3 no host, seed do dia, entrada de menu e rótulo, streak e recordes (RF-33.12), gravação de `BestRunWave`; T-33.11 a 33.13 | TD.33a (opções), TD.33b (UI) | 🟡 |

### 12.2 Arquivos prováveis

- **TD.33a:** `src/SideWar.Core/Content/td-blessings.json` (novo), `TdContentModels.cs`/`TdContentLoader.cs`/`TdContentValidator.cs`/`TdContentJson.cs`, `td-stages.json` (bloco `rules.blessings` e `rules.pace`), `src/SideWar.Core/Td/TdTypes.cs` (`TdMatchOptions`, `TdEventType` anexos ao fim, `TdActiveWave.HpFactorBp`), `TdCommands.cs`, `TdMatchState.cs`, novo `TdMatchState.Blessings.cs`, `TdMatchState.Tick.cs`, `TdMatchState.Hash.cs`, `TdReplay.cs`, `TdSanityBot.cs`, `TdBotPlans.cs`, testes em `tests/SideWar.Core.Tests`.
- **TD.33b:** `src/SideWar.Runtime/Td/TdUiController*.cs`, `TdUiModel.cs`, `src/SideWar.Rendering/Scene/TdUiRenderer.cs`, hotkeys em `SideWar.App` e testes `tests/SideWar.Runtime.Tests/Td`.
- **TD.33d:** `src/SideWar.Runtime/Td/TdUiModel.cs` (modelo de apresentação: valor exibido, fila de "+N", renda, limiares), `TdUiController*.cs`, `src/SideWar.Rendering/Scene/TdUiRenderer.cs` (contador, flutuantes, brilhos), `Palette`, settings `td.showIncome` e `td.discreetMode` e tela de Configurações; testes em `tests/SideWar.Runtime.Tests/Td`. **Nenhum arquivo em `src/SideWar.Core`.**
- **TD.33c:** `src/SideWar.Persistence/Profile/TdProfileData.cs`, `TdProfileMigrations.cs`, `TdProfileSerializer.cs`, `TdProfileStore.cs`, host `TdGameHost`/`TdProfileRecorder` (relógio e seed), menu em `TdUiController`, testes de Persistence e Runtime.
- **Atenção a conflitos:** `TdUiController*.cs`, `TdUiRenderer.cs`, `TdMatchState.Bonus.cs` e `TdSanityBot.cs` têm alterações locais não commitadas de outras fichas; TD.33a/b só começam depois delas serem integradas (ordem abaixo).

### 12.3 Ordem global (obrigatória, uma por vez)

**TD.30b → TD.30c → TD.32a/b → TD.33a → TD.33b → TD.33c → TD.33d → TD.31a..e.** (TD.33d pode rodar logo após a TD.33b se a TD.33c atrasar: só depende do chip/renderer da 33b.) A SDD-33 **não depende** do SDD-31 (só usa as 5 torres atuais); quando as torres Azure chegarem, a TD.31e **acrescenta** bênçãos novas ao catálogo (ex.: Cofre, Fila) em ficha própria, sem mudar o fluxo.

## 13. Ideias pequenas (propostas, **não aprovadas**)

- **Números de dano discretos** (flutuantes minúsculos de dano em inimigos, distintos dos "+N" de créditos da peça E; opcionais em Configurações, só acima de um limiar).
- **Brilho ao despertar runa** (1 s, borda do chip) para dar peso a cada fragmento gasto.
- **Som opcional em marcos** (um "plim" curto, padrão desligado, respeitando "discreto").
- **Patentes como certificações Azure:** AZ-900 (Estagiário), AZ-104 (Júnior/Pleno), AZ-305 (Sênior/Arquiteto), mantendo os limiares de `TdRank` (só renomeia/arte; flavor, sem quiz, RF-28.17).
- Resumo semanal local (melhor onda da semana) e cor sazonal do chip.

## 14. Decisões em aberto (com recomendação; nenhuma arquitetura decidida aqui)

| DP | Pergunta | Recomendação |
|---|---|---|
| DP-33.1 | Novas flags entram em `TdMatchOptions.Default` ou em preset novo? | **Preset `Retention`** e `Default` intacto (hash/goldens/bots sem mudança); migrar o host para `Retention` ao fim da TD.33b |
| DP-33.2 | Expiração da oferta (briefing propôs 30 s) | **60 s de mundo só com presença**, aplicando a carta 0 (Geral); valor em JSON |
| DP-33.3 | Catálogo: 14 cartas, pilhas e tetos | Aprovar como **valores iniciais**; medir na TD.33a antes de fixar |
| DP-33.4 | Onde guardar a melhor onda do diário | **Campo próprio com poda de 30 dias** (não `InfiniteRuns`, que cresceria por seed) |
| DP-33.5 | Pergunta-bônus no desafio diário | **Desligada** (cadência por tempo real quebra a comparação) |
| DP-33.6 | O "muro" de `CycleHpGrowth` ×1,25 e renda linear | **Medir primeiro** (bot, 20 seeds, ciclos 1 a 3) e propor ajuste em ficha à parte; não mexer aqui |
| DP-33.7 | Atalhos globais para escolher a bênção sem mouse | **Opcionais, padrão desligado** (cuidado com conflitos de Ctrl+Alt com outros apps) |
| DP-33.8 | Streak conta o dia na onda 5 ou ao abrir | **Onda 5** (evita streak por abrir e fechar) |
| DP-33.9 | Respiro pós-chefe: −20% de HP | Aprovar como inicial; ajustar com o bot |

## 15. Referências internas

SDD-28 (RF-28.5 dano, RF-28.8 chefes, RF-28.10/21 estrelas e pontuação, RF-28.16 determinismo), SDD-29 §20 (corrida contínua) e §4–6 (runas, versão vigente é a teia de fragmentos, TD.29a), SDD-31 (torres Azure; §2 e §12.4), SDD-32 (cartão de fim, em escrita), ADR-026 (determinismo e layout no estado), SDD-14 (persistência versionada), `td-stages.json` (`hpWaveGrowth`, `hpStageStep`, `maxEnemiesOnField`, `fragments`, `bonusQuestion`), `td-waves.json` (ondas `rest`, chefes nas ondas finais das fases 3, 5, 6), `td-runes.json` (canais de efeito e `creditSources`), `td-abilities.json` (`scale`, `firewall`, `snapshot`), `Td/TdMatchState*.cs` (`BeginWave`, `SettlePhase`, `ClearStageAndAdvance`, `ScaledHp`, `CycleHpGrowth`), `Td/TdTypes.cs` (`TdMatchOptions`), `Td/TdReplay.cs`, `Persistence/Profile/TdProfileData.cs` (schema v3).
