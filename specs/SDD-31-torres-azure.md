# SDD-31: Dez torres novas ligadas a serviços Azure (Functions, Cosmos, Sentinel, Cofre, Backup, CDN, Fila, Policy, AKS, Pipeline)

**Status:** PROPOSTA com **decisões do usuário fechadas** (ficha SDD-31b, 2026-11-17; DP-31.1 a DP-31.13 estão na §2) · **Etapa:** ciclo "TD" · **Depende de:** SDD-28 (partida TD, RF-28.5/28.7/28.14), SDD-29 (runas, custo crescente, painel modal), SDD-10/11 (núcleo determinístico, ADR-026), SDD-20 (sistema visual), SDD-24 (painel e cards) e das fichas **TD.30a, TD.30b e TD.30c** (executadas antes, ver §12.4) · **Não edita** SDD-28, SDD-29, `DECISOES.md` nem código: só especifica. As fichas de execução são TD.31a a TD.31e (§12), **uma por vez** (§12.4).

> Todo número aqui é **valor inicial ajustável por JSON** e **não medido** (README §2.3). Entra em playtest e no `TdSanityBot` (bloco TD.31e) antes de valer. Base de código descrita: commit `a6d2298` (HEAD). `src/SideWar.Core/Td/TdSanityBot.cs` tem alteração local não commitada de outra ficha; o estado dele não é tratado aqui.

## 1. Objetivo

Hoje o jogador tem **5 torres** (`td-towers.json`: Raio, Gelo, Canhão, Balanceador, Scale Set) e 8 inimigos + 3 chefes. Esta ficha **acrescenta 10 torres e 3 inimigos**, cada uma com **função distinta** das 5 existentes e das outras 9, e cada uma ligada a **um serviço Azure** (só flavor, como no SDD-28 RF-28.17: nada de jogo de ensino). O objetivo de design é **decisão tática**, não mais DPS: cada torre responde a uma pergunta diferente ("o que faço contra rápidos? contra enxames? contra blindados? como ganho dinheiro? como me recupero de drenagem?").

Visão do usuário que governa tudo (memória `visao-jogar-no-trabalho`): **discreto, sem foco, infinito (corrida contínua), cantinho**. Por isso: (a) 15 opções **não** podem aparecer de uma vez (liberação por fase, §10); (b) o painel rápido precisa de **categorias discretas** em vez de rolagem (§11); (c) nenhuma torre exige microgerência nem clique durante a onda; (d) nenhuma mecânica nova depende de aleatório (§8.1).

## 2. Decisões do usuário (fixas, não reabrir)

| # | Decisão |
|---|---|
| a | Adicionar **10 torres** novas, todas com **função distinta** e ligadas a um **serviço Azure**. |
| b | Lista fechada: 1 **Functions**, 2 **Cosmos**, 3 **Sentinel**, 4 **Cofre** (Key Vault), 5 **Backup**, 6 **CDN**, 7 **Fila** (Service Bus), 8 **Policy**, 9 **AKS**, 10 **Pipeline** (DevOps). O papel de cada uma está no §6. |
| c | Inimigos novos: **3** (`fork`, `trojan`, `phishing`; o `exfil` foi **cortado**, DP-31.10). A fase 6 reaproveita os existentes (§7). |
| d | **Liberação gradual por fase** (`unlockStage`), para não haver 15 opções de saída (§10). |
| e | O painel rápido de torres (15 cards) usa **categorias Ataque, Controle, Suporte, Economia** (§11). |
| f | Divisão em fichas TD.31a a TD.31e (§12); balanceamento dos bots é **bloco separado** (TD.31e, §9). |
| g | **DP-31.1 (a):** kinds novos `control`, `economy` e `summoner` no validador, com regras próprias (sem `damage > 0`, `targets` próprio); `category` vira campo do JSON. |
| h | **DP-31.2 (a):** o Functions guarda o estado do gatilho **no `TdTower`** (entra no `StateHash`). |
| i | **DP-31.3:** teto de **2 Cofres** por partida (`maxPerPlayer` 2). Sem decaimento de renda. |
| j | **DP-31.4 (a):** a marca do Sentinel **reusa `VulnBonus`** ("o maior vale"); sem campo `MarkBp`. |
| k | **DP-31.5 (a):** estrelas e pontuação leem a **perda acumulada** de integridade; a cura do Backup não "esconde" dano. |
| l | **DP-31.6 (a):** "antes da zona" = **alcance mínimo 120** da CDN (independe de layout). |
| m | **DP-31.7 (a):** **nenhuma runa nova agora.** O risco de `rangeBp` (CDN a 260) e `waveCredits` (soma com o Cofre) fica **registrado** (§16) e é **item da TD.31e** (B-31.6). |
| n | **DP-31.8:** a Fila contra **chefes** empurra com **50%** do recuo (`bossPushbackFactorBp` 5000). |
| o | **DP-31.9:** o **AKS fica, redefinido**: pods **atacantes temporários que NÃO bloqueiam** (para não duplicar o Balanceador). RF-31.9. |
| p | **DP-31.10:** **cortar o inimigo `exfil`**. Ficam 3 inimigos (`fork`, `trojan`, `phishing`); a fase 6 reaproveita os existentes. |
| q | **DP-31.11:** **revisar as metas dos bots** para 15 torres (TD.31e, §9). |
| r | **DP-31.12:** `unlockStage` compara com o **maior estágio concluído do perfil**, não com a fase da partida (§10). |
| s | **DP-31.13 (a):** **regenerar os goldens TD a cada ficha de conteúdo** (31a, 31b, 31c, 31d), justificando no REPORT; goldens idle intactos. |
| t | **Ordem de execução:** TD.30a → TD.30b → TD.30c → TD.31a → TD.31b → TD.31c → TD.31d → TD.31e, **uma por vez** (§12.4). |

## 3. Estado atual do Core (o que já suporta)

Fatos verificados no HEAD, base para a viabilidade de cada torre:

| Item | Hoje | Consequência |
|---|---|---|
| Tipos de torre (`kind`) | `attack`, `blocker`, `support` (`TdContentValidator.TowerKinds`); `targets` ∈ `ground`/`both`/`none`; `support` ⇔ `targets = none`; `attack` exige `damage > 0` e `interval` em [0,1; 5] s | Torres de **controle** (Sentinel, Policy, Fila) e de **economia** (Cofre) **não cabem** nas regras atuais do validador: por decisão (DP-31.1, a) entram os kinds `control`, `economy` e `summoner`. |
| Stats | `Dictionary<string,double> stats` por torre, lidos por `Stat(t, def, "chave", padrão)`; já existem `slowFactor`, `splashRadius/Factor`, `nodes`, `nodeHolds`, `nodeCooldownSeconds`, `nodeDamagePerSecond`, `focusBonusCap`, `focusKeepsBetweenTargets` e afins | Novas torres entram por **chaves novas** de `stats`, sem mudar o esquema do JSON. |
| Traços de inimigo | lista fixa `basic, fast, swarm, armored, durable, flying, regen, drain, boss`; `strong`/`weak` das torres referenciam esses traços | Inimigos novos (§7) exigem **traços novos** no validador. |
| Foco (`FocusTargetId`, `FocusHits`) | já no `TdTower` e no hash; usado pelo Raio em ramos legados (`Combat.cs`, bloco de foco) | **Pipeline** reaproveita o contador (RF-31.10). |
| Nós (`TdNode`, `NodeCdTicks`) | bloqueador do Balanceador com `Holds`, `Dps`, `LifeTicks`, `Flags` | **AKS** reaproveita o nó como **pod atacante que não bloqueia** (`Holds` 0, flag de "pod"; RF-31.9). |
| Debuffs do inimigo | `SlowMult/SlowTicks`, `ParalyzeTicks`, `VulnBonus/VulnTicks`, `CorrodeAmount/CorrodeTicks`, `InvulnTicks` | **Sentinel** reusa `VulnBonus` (DP-31.4, a); **Policy** é aura sem estado por inimigo. |
| Drenagem | `DisabledTicks` na torre (Parasita 3 s; Ransomware criptografa 4 s) | **Backup** zera `DisabledTicks` (RF-31.5). |
| Divisão de chefe | `SplitBoss` cria inimigos **sem consumir o `Rng`**, em ordem fixa | **Fork Bomb** copia ao morrer pelo mesmo padrão (§7). |
| Determinismo | simulação 30 Hz, `double` com ordem de iteração fixa, sem `Math.Pow`; `Rng` só em `BeginWave` (1 `NextUInt` por inimigo da onda, jitter de spawn) e no despertar de runa (`AwakenRune`, por comando); o estado do `Rng` que vale é o deixado pelas fichas TD.30a a TD.30c | Nenhuma torre nova pode chamar o `Rng` (§12.1). |
| Runas (TD.29a) | **teia de 32 nós** (4 anéis × 8 setores), 8 efeitos (`rangeBp`, `boltDamageBp`, `waveCredits`, `frostSlowTenths`, `integrity`, `cannonDamageBp`, `abilityCooldownBp`, `firewallTenths`) despertados por **fragmentos** com sorteio pelo `Rng`. **O SDD-29 §4 a §6 (grafo de 61 nós pago em créditos) descreve uma versão anterior; o código vigente é a teia de fragmentos** | As runas existentes só reforçam Raio, Gelo, Canhão e efeitos gerais; **não cobrem as torres novas** (DP-31.7: nenhuma runa nova agora). |
| Custo | `towerCostTable` (48 fatores inteiros, base × F[n]/100, por tipo), teto técnico 48 torres, venda `min(preço pago, taxa × custo(n−1))` | As torres novas **entram na mesma curva** sem código novo (só `cost` base no JSON). |
| Bots | `TdSanityBot` + `TdBotPlans` (DSL de roteiros por perfil) | Roteiros precisam saber construir os tipos novos (§9). |

## 4. Escopo e exclusões

**Dentro:** 10 torres (JSON, Core, UI, ícones), 3 inimigos (JSON, Core, UI), `unlockStage`, categorias do painel rápido, impacto em determinismo e testes, bloco de balanceamento.

**Fora:** novas fases ou chefes; reabrir runas, economia de custo crescente ou pausa modal (SDD-29); níveis/ramos de torre (removidos, não voltam); runas próprias das torres novas (DP-31.7, decidido: nenhuma agora); qualquer exibição de pergunta/quiz (RF-28.17); persistência nova no perfil além do que o `unlockStage` já usa.

## 5. Tabela-resumo das 10 torres

Alcance em **unidades lógicas** (caminho = 1.200; Raio 150, Gelo 100, Canhão 110). Custo = **base** da curva `base × F[n]/100` (SDD-29 §5.1). Referência: Raio 90 · 24 dano · 0,9 s ≈ 27 dps.

| # | id | Nome (Azure) | Categoria | `kind` proposto | Base | Alcance | Dano | Intervalo | Fase (`unlockStage`) | Ficha |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `functions` | Functions | Ataque | `attack` | 60 | 90 | 22 × 3 (rajada) | recarga 3,0 s, só no gatilho | 1 | TD.31a |
| 2 | `cosmos` | Cosmos | Ataque | `attack` | 130 | 100 | 20 + 3 saltos | 1,6 s | 3 | TD.31b |
| 3 | `sentinel` | Sentinel | Controle | `control` | 110 | 130 | 0 (marca +25%) | marca a cada 5 s | 5 | TD.31b |
| 4 | `vault` | Cofre (máx. 2) | Economia | `economy` | 160 | n/a | n/a | por onda | 6 | TD.31c |
| 5 | `backup` | Backup | Suporte | `support` | 170 | 140 | n/a | reativa a cada 6 s; cura 1 a cada 45 s | 5 | TD.31c |
| 6 | `cdn` | CDN | Ataque | `attack` | 140 | 260 (mín. 120, fixo) | 8 | 1,4 s | 4 | TD.31a |
| 7 | `queue` | Fila | Controle | `control` | 120 | 110 | 0 (empurra 60; 30 em chefe) | 6 s | 5 | TD.31b |
| 8 | `policy` | Policy | Controle | `control` | 120 | 110 | 0 (remove armadura) | contínuo | 2 | TD.31a |
| 9 | `aks` | AKS | Suporte | `summoner` | 180 | 80 | 3 pods × 8/s | recarga 20 s | 6 | TD.31c |
| 10 | `pipeline` | Pipeline | Ataque | `attack` | 150 | 130 | 14 + rampa | 0,8 s | 4 | TD.31b |

Nenhuma torre nova chama o `Rng`. Todos os empates (alvo, salto, torre mais próxima) resolvem por **menor X lógico e depois menor `Id`**.

## 6. Requisitos funcionais (uma subseção por torre)

Formato de cada RF: **Serviço e flavor** · **Função** · **Números iniciais** · **Forte/Fraco** · **Interações e bordas** · **Critério de aceite**. "Torre ativa" = `Placed` e `DisabledTicks = 0`; torre **SEM POSIÇÃO**, drenada ou criptografada **não** dispara nada (regra já vigente para as 5 torres).

### RF-31.1: Functions (`functions`): rajada só quando o inimigo entra no alcance

- **Serviço e flavor (pt-BR):** Azure Functions: "Como Azure Functions: só roda quando é chamada, e cobra barato por isso."
- **Função:** barata, **dispara uma rajada de 3 tiros** quando **um inimigo novo cruza a borda do alcance**; fora disso não atira. Alvo: solo e aéreo. Mira: o inimigo que disparou o gatilho.
- **Números:** `cost` 60 · `range` 90 · `damage` 22 por tiro · `stats`: `burstShots` 3, `burstGapTicks` 4 (≈ 0,13 s entre tiros), `rearmSeconds` 3,0 (recarga mínima entre rajadas), `interval` 3,0 (valor de validação) → rajada = 66 de dano em ≈ 0,4 s, ≈ 22 dps de média.
- **Forte:** `fast`, `flying`, `basic` (acerta antes de o rápido sair do alcance). **Fraco:** `durable`, `boss`, `armored` (22 por tiro some na armadura), `swarm` (uma rajada por borda, não por inimigo, com recarga).
- **Bordas:** o gatilho é "algum inimigo estava **fora** do alcance no tick anterior e está **dentro** agora". Dois inimigos que entram juntos geram **uma** rajada. Se a torre estiver em recarga quando o inimigo entra, o gatilho **não fica guardado** (não há fila). Sair e voltar ao alcance com a recarga pronta dispara de novo. **Decidido (DP-31.2, a):** o estado do gatilho fica **no `TdTower`** (lista ordenada por `Id` dos inimigos que estavam dentro no tick anterior, no hash), sem comparar X entre ticks; assim o empurrão da Fila e o `LayoutChanged` não geram falso gatilho.
- **CA-31.1:** (a) com um único inimigo parado fora e depois dentro do alcance, a torre dispara exatamente 3 tiros e 0 tiros enquanto ele permanece dentro; (b) dois inimigos que cruzam no mesmo tick produzem 1 rajada; (c) com `rearmSeconds` não cumprido, não dispara; (d) `runner` (traço `fast`) morre com menos tiros do que com Raio no mesmo cenário (matriz de contra-escolha, T-31.1).

### RF-31.2: Cosmos (`cosmos`): raio em cadeia

- **Serviço e flavor:** Azure Cosmos DB: "Como Cosmos DB: uma escrita, replicada para toda parte."
- **Função:** o tiro acerta o alvo e **salta para até 3 inimigos** próximos (sem runa que aumente os saltos, DP-31.7), cada salto com dano reduzido. Solo e aéreo. Mira padrão `first`.
- **Números:** `cost` 130 · `range` 100 · `damage` 20 · `interval` 1,6 · `stats`: `chainJumps` 3, `chainRadius` 60 (unidades entre o inimigo atingido e o próximo), `chainFalloffBp` 8000 (cada salto = 80% do anterior: 20 → 16 → 12,8 → 10,2; total ≈ 59 contra 4 alvos).
- **Forte:** `swarm`, `basic`. **Fraco:** `armored` (dano por salto some na armadura), `boss`, `durable` (um alvo só).
- **Bordas:** o próximo salto escolhe o inimigo **ainda não atingido nesta cadeia** mais próximo do atual dentro de `chainRadius`; empate por menor `Id`; sem alvo, a cadeia termina. Um inimigo nunca é atingido duas vezes na mesma cadeia. O salto **não** exige alcance da torre (só `chainRadius` entre os inimigos). Voador só é saltado se a torre mira `both` (sim).
- **CA-31.2:** contra 4 inimigos a ≤ 60 unidades entre si, causa dano nos 4 com a razão 1 : 0,8 : 0,64 : 0,51 (após armadura); contra 1 alvo, 1 acerto; nenhum inimigo recebe dois acertos da mesma cadeia; resultado idêntico em 5 execuções (T-31.2).

### RF-31.3: Sentinel (`sentinel`): marca de ameaça (debuff, não lentidão)

- **Serviço e flavor:** Microsoft Sentinel: "Como o Sentinel: marca a ameaça para todo o SOC agir sobre ela."
- **Função:** a cada 5 s **marca 1 inimigo** (o de maior HP restante no alcance, mira `strong`): **todas as torres** causam **+25% de dano** nele por 4 s. **Não aplica lentidão** nem dano. Mira configurável como as demais (`first/last/strong/weak/close`).
- **Números:** `cost` 110 · `range` 130 · `interval` 5,0 (recarga da marca) · `stats`: `markBonusBp` 2500, `markSeconds` 4, `markMax` 1 por torre.
- **Forte:** `boss`, `durable`, `armored` (o bônus soma depois da armadura: ver ordem do dano em SDD-28 RF-28.5, "vulnerável (marca)"). **Fraco:** `swarm` (uma marca só), `fast` (a marca expira ou o inimigo sai do alcance antes do foco).
- **Bordas:** marcas de **vários** Sentinels em inimigos diferentes valem; **no mesmo inimigo não empilham**, vale a maior (e a duração renova para a maior restante). A marca **compartilha o canal** do "vulnerável" de runas e do Gelo: **decidido (DP-31.4, a):** a marca **reusa `VulnBonus`/`VulnTicks`** ("o maior vale", sem soma com Corroer e sem campo novo). Consequência aceita: qualquer "vulnerável" ativo (marca, Gelo ou runa) conta como "marcado" para revelar o **Phishing** (§7).
- **CA-31.3:** com Sentinel ativo, um alvo marcado recebe dano ×1,25 de Raio, Canhão e Cosmos durante 4 s e ×1,0 depois; dois Sentinels no mesmo alvo não dão ×1,5625; sem lentidão aplicada em nenhum momento (T-31.3).

### RF-31.4: Cofre (`vault`): economia, não ataca

- **Serviço e flavor:** Azure Key Vault: "Como o Key Vault: guarda o que vale e rende confiança."
- **Função:** **não ataca**; ao **fechar cada onda** (evento `WaveCompleted`) paga créditos, com **bônus** se a onda foi **sem vazamento**.
- **Números:** `cost` 160 · `stats`: `creditsPerWave` 14, `noLeakBonus` 10, `maxPerPlayer` 2 (somado quando a onda não vazou). Retorno inicial: 24 por onda sem vazar → recupera o custo em ≈ 7 ondas sem vazar (ou ≈ 11 com vazamentos), **antes** do crescimento de custo por repetição (n = 2 sai por ≈ 200).
- **Forte:** corridas longas e estáveis (a corrida contínua do TD.28b é infinita; o teto de 2 Cofres, DP-31.3, limita a composição). **Fraco:** o Cofre não defende; **perde** a renda se estiver drenado/criptografado/SEM POSIÇÃO no instante do fechamento.
- **Bordas:** pagamento é crédito inteiro, somado **junto** do bônus de onda existente (`WaveBonus`) e emitido como evento próprio para o recibo ("+14 ¢ Cofre"), sem texto longo. Vender o Cofre devolve pela regra de venda normal (SDD-29 §5.2); comprar/vender em loop é neutro e não rende (teto pelo preço pago). **Teto: 2 Cofres por partida** (DP-31.3); o 3º `BuildTower` é rejeitado com motivo próprio ("limite do tipo"), validado no Core. A renda por exemplar **não decai**. Com o teto, a renda máxima base é 2 × 24 = 48 por onda sem vazar (o `waveCredits` de runa soma por cima: B-31.6).
- **CA-31.4:** ao fechar uma onda **sem vazar**, créditos sobem em `waveBonus + creditsPerWave + noLeakBonus`; com vazamento, sem o `noLeakBonus`; Cofre drenado não paga; nenhuma torre recebe cálculo de ataque do Cofre (T-31.4).

### RF-31.5: Backup (`backup`): reativa torres e cura o núcleo devagar

- **Serviço e flavor:** Azure Backup / Site Recovery: "Como o Site Recovery: derrubou? Volta ao ar sozinho."
- **Função:** suporte de área. A cada 6 s, **reativa 1 torre drenada** no alcance (zera `DisabledTicks`; ordem: a de menor `Id`); a cada 45 s de **onda em curso** **cura 1 de integridade do núcleo**, até o máximo.
- **Números:** `cost` 170 · `range` 140 (aura, como o Scale Set) · `stats`: `reviveEverySeconds` 6, `coreHealEverySeconds` 45, `coreHealCapPerStage` 3 (teto de cura total por fase).
- **Forte:** `drain` (Parasita; chefe Ransomware). **Fraco:** ondas sem drenagem (a torre fica "morta"); alcance curto para cobrir o trilho todo.
- **Bordas:** o Backup **não** reativa a si mesmo (o Parasita o desativa como qualquer torre; outro Backup pode); a reativação não concede imunidade (o Parasita pode drenar de novo). **Estrelas (DP-31.5, a):** estrelas e pontuação leem a **perda acumulada** de integridade (`twoStarMaxLoss` 6, `threeStarMaxLoss` 2), **não** a integridade atual; a cura do Backup não "perdoa" dano. Exige um contador de perda acumulada no `TdMatchState` (no hash; reusar se já existir).
- **CA-31.5:** um Parasita drena uma torre no alcance; em ≤ 6 s ela volta; a cada 45 s de onda a integridade sobe 1 (nunca acima do máximo; no máximo 3 vezes por fase); o teste de estrelas confirma que a cura **não** reduz a perda acumulada (DP-31.5, T-31.5).

### RF-31.6: CDN (`cdn`): alcance enorme, dano baixo, bate antes da zona

- **Serviço e flavor:** Azure CDN: "Como a CDN: já respondeu antes de o pedido chegar na origem."
- **Função:** alcance **260** (quase o dobro do Raio), dano **baixo** (8), com **alcance mínimo 120** (decidido, DP-31.6, a): só atinge inimigos **fora da zona das outras torres**, "antes da zona". Solo e aéreo. É a torre de **dano de desgaste** de longe, não a que fecha.
- **Números:** `cost` 140 · `range` 260 · `damage` 8 · `interval` 1,4 · `stats`: `minRange` 120. DPS ≈ 5,7 (por si só **menor** que o Raio).
- **Forte:** `basic`, `flying`, `swarm` (acerta cedo, soma em ondas grandes). **Fraco:** `armored`, `durable`, `boss` (8 de dano some na armadura); **não** atinge quem já está dentro do alcance mínimo.
- **Bordas:** "antes da zona" = **alcance mínimo**, independente do layout (`LayoutChanged`, ADR-026). O `rangeBp` das runas aumenta o alcance (260 já é o maior): **risco registrado, sem runa nova agora** (DP-31.7; B-31.6 e §16).
- **CA-31.6:** inimigo a 200 unidades é atingido; a 100, não; o alcance efetivo respeita `range` e `minRange`; dano 8 contra `armored` (armadura 38) cai para o piso (`max(1, floor(x))`) (T-31.6).

### RF-31.7: Fila (`queue`): empurra o inimigo para trás

- **Serviço e flavor:** Azure Service Bus: "Como o Service Bus: põe o excesso na fila e segura o resto."
- **Função:** a cada 6 s **empurra 1 inimigo para trás** no caminho (aumenta X lógico em 60 unidades; **30 em chefes**, ≈ 1,5 s de caminhada de um `grunt`, limitado a 1.200). **Não é lentidão** (funciona em chefes, com recuo reduzido a 50%; ver bordas). Mira configurável (`strong` recomendado).
- **Números:** `cost` 120 · `range` 110 · `interval` 6,0 · `stats`: `pushbackUnits` 60, `bossPushbackFactorBp` 5000 (**50% em chefes** = 30 unidades, DP-31.8), `pushGraceSeconds` 3 (o mesmo inimigo não é empurrado de novo antes disso).
- **Forte:** `durable`, `boss` (ganha tempo de exposição às outras torres; em chefe o recuo é de 30, para não deixar Ransomware/Zero-day reféns). **Fraco:** `swarm`, `fast` (um empurrão no meio da multidão); causa dano zero, então não mata sozinha.
- **Bordas:** o empurrão **não passa** do portal (X ≤ 1.200) nem "desfaz" gatilhos de chefe já disparados (`Triggers` é por bit, não repete; Zero-day não "reinvoca" invulnerabilidade ao voltar atrás). Inimigo preso em nó de Balanceador **não** é empurrado (o nó segura). O empurrão acontece no tick da torre, antes do movimento do inimigo (ordem fixa, §12.2).
- **CA-31.7:** o alvo tem X aumentado em exatamente `pushbackUnits` (60; 30 se for chefe) no tick do empurrão; não passa de 1.200; o mesmo inimigo não é empurrado antes de 3 s; Zero-day não repete invulnerabilidade; replay com mesmo hash (T-31.7).

### RF-31.8: Policy (`policy`): remove a armadura na área

- **Serviço e flavor:** Azure Policy: "Como o Azure Policy: impõe a regra e derruba a blindagem."
- **Função:** aura contínua: inimigos **dentro do alcance** têm **armadura = 0** para **todas** as torres (a armadura é ignorada no cálculo, não "perdida" para sempre).
- **Números:** `cost` 120 · `range` 110 · `stats`: `armorRemoveBp` 10000 (100%; ajustável para 50% se quebrar o balanceamento). Sem dano e sem intervalo.
- **Forte:** `armored` (armadura 38 → 0), o grupo de CDN/Functions/Cosmos (dano baixo por tiro). **Fraco:** `flying`, `swarm`, `fast` (sem armadura para remover).
- **Bordas:** múltiplas Policies **não empilham** (a armadura já é 0). O efeito **só vale dentro do alcance da Policy** (o inimigo "carrega" a regra enquanto está lá, e a perde ao sair: efeito de aura, sem estado por inimigo, vantagem para o hash). Convive com "Perfurar" e "Corroer" de runas (DP-31.7).
- **CA-31.8:** com Policy ativa, um `armored` no alcance recebe o dano do Raio sem redução de armadura (24 em vez de `max(1, 24−38)`); ao sair do alcance, volta a armadura 38; duas Policies = mesmo efeito de uma (T-31.8).

### RF-31.9: AKS (`aks`): invoca pods atacantes temporários (não bloqueiam)

- **Serviço e flavor:** Azure Kubernetes Service: "Como o AKS: sobe três pods quando a carga aperta."
- **Função:** quando **≥ 3 inimigos de solo** estão no alcance (gatilho "autoscale"), invoca **3 pods temporários** que **atacam** (8 de dano/s cada, solo) e **NÃO bloqueiam** (decidido, DP-31.9): nenhum inimigo fica retido e os pods não são alvo. Duram 12 s; recarga 20 s. Só solo. É o "burst de dano por densidade", **não** um retentor: esse papel é do Balanceador.
- **Números:** `cost` 180 · `range` 80 · `stats`: `pods` 3, `podDps` 8, `podLifeSeconds` 12, `triggerEnemies` 3, `interval` 20 (≈ 14 dps de média, 24 dps durante os 12 s).
- **Forte:** `swarm`, `basic`, `replicator` (a densidade dispara o gatilho e os pods somam por inimigo). **Fraco:** `flying`, `boss` (um alvo só, sem retenção), `fast` (sai do alcance), ondas esparsas (o gatilho de 3 não dispara).
- **Bordas:** reaproveita `TdNode` (`Holds` 0, `Flags` bit 4 = pod, `LifeTicks`); o nó **não** entra na lógica de retenção do Balanceador. Cada pod mira o inimigo de solo mais adiantado (menor distância ao portal) dentro do alcance do AKS, desempate por menor `Id`. Posição dos pods determinística a partir do X do primeiro inimigo (passo fixo de 12 unidades, sem `Rng`). Pod não atrai nem sofre dano.
- **CA-31.9:** com 3 inimigos de solo no alcance, 3 pods nascem no mesmo tick e cada um causa 8/s; **nenhum inimigo é retido** (`Holds` 0); somem em 12 s; a recarga é 20 s; com 2 inimigos, nada nasce; voador nunca é atingido (T-31.9).

### RF-31.10: Pipeline (`pipeline`): dano cresce enquanto mira o mesmo alvo

- **Serviço e flavor:** Azure DevOps (Pipelines): "Como o Pipeline: cada etapa aprovada deixa a próxima mais forte."
- **Função:** o dano **sobe a cada tiro seguido no mesmo alvo** e **zera ao trocar de alvo**. Anti-tanque e anti-chefe: lenta de começar, devastadora no que demora a morrer. Solo e aéreo, mira `strong`.
- **Números:** `cost` 150 · `range` 130 · `damage` 14 · `interval` 0,8 · `stats`: `rampPerHitBp` 800 (+8% por tiro seguido), `rampCapBp` 12000 (teto +120%, ≈ 15 tiros), `rampKeepsBetweenTargets` 0. Dano final: 14 → ≈ 31 no teto; DPS de 17,5 a ≈ 38,5.
- **Forte:** `durable`, `boss`, `armored` (somado a Policy, fecha o combo). **Fraco:** `swarm`, `fast` (troca de alvo zera a rampa), `regen` (a rampa se perde se o alvo morre/foge).
- **Bordas:** reaproveita `FocusTargetId`/`FocusHits` e as chaves `focus*` já lidas (`Combat.cs`); o que muda é o conteúdo (novas chaves `rampPerHitBp`/`rampCapBp` em pontos-base inteiros, sem `double` no estado). O contador **zera** ao ser drenada (`DisabledTicks`) e ao perder o alvo por alcance. Marca do Sentinel soma **multiplicativamente** depois da rampa (ordem do dano, §12.3).
- **CA-31.10:** contra um alvo parado, o n-ésimo tiro causa `floor(14 × (1 + min(12, 0,08·(n−1))))` antes de armadura; troca de alvo volta a 14; teto respeitado; contra `tank` (420 HP) mata em menos tiros que o Raio a partir do tiro N (medida registrada, T-31.10).

### 6.1 Matriz de contra-escolha (nova coluna dos traços)

Cada traço de inimigo (existente ou novo, §7) precisa de pelo menos uma torre **forte** e uma **fraca** (regra do CA-28.5, mantida). Resumo inicial:

| Traço | Fortes (existentes + novas) | Fracas |
|---|---|---|
| `fast` | Balanceador, Gelo, **Functions** | Canhão, Pipeline, Cosmos, **AKS** |
| `swarm` | Canhão, **Cosmos**, **AKS** (dano por densidade), **CDN** | Raio, Pipeline, Fila |
| `armored` | Canhão, **Policy**, **Pipeline** | **CDN**, **Cosmos**, **Functions** |
| `durable` | Raio, **Pipeline**, **Fila**, Scale Set | Canhão, **Functions** |
| `flying` | Raio, Gelo, **Functions**, **CDN** | Canhão, Balanceador, **AKS** |
| `boss` | **Pipeline**, **Fila**, **Sentinel** | Gelo, **CDN**, **AKS** |
| `regen` | Raio, Gelo, **Sentinel** (marca = burst) | Pipeline |
| `drain` | **Backup**, Raio | Scale Set |
| `replicator` (novo) | **Cosmos**, Canhão, **AKS** | Raio |
| `shielded` (novo) | **Pipeline**, Canhão | **Functions**, **CDN** |
| `stealth` (novo) | **Sentinel** (revela), CDN (alcance) | Raio, Canhão |

## 7. Inimigos novos (3 aprovados; o `exfil` foi cortado, DP-31.10)

Introduzidos **com** a torre que lhes dá função, para a lição vir antes ou junto do problema. HP, velocidade e demais números seguem o formato de `td-enemies.json` e entram sempre **depois** do balanceamento (TD.31e). Todo `strong`/`weak` usa os traços novos, que o `TdContentValidator.Traits` precisa aceitar (ampliação na TD.31d).

| id | Nome | Traço novo | Fase | Mecânica | Torres que o contra-atacam |
|---|---|---|---|---|---|
| `fork` | **Fork Bomb** | `replicator` | 3 (com Cosmos) | **Ao morrer** cria **2 cópias** com 40% do HP máximo (**uma geração**: as cópias não se copiam). Sem `Rng`: nascem em X e X+2, nesta ordem, como `SplitBoss`. | Cosmos (cadeia atinge todas), Canhão (área), AKS (pods por densidade) |
| `trojan` | **Trojan** | `shielded` | 4 (com Pipeline/Policy) | **Escudo** que absorve os primeiros `shieldHp` de dano (ex.: 120) antes do HP; o escudo **não regenera**. Imune a lentidão enquanto o escudo existir. (Não confundir com o chefe **Ransomware**, que já existe com outra mecânica.) | Pipeline (rampa derruba o escudo), Canhão |
| `phishing` | **Phishing** | `stealth` | 5 (com Sentinel) | **Camuflado**: torres só o enxergam a menos de `revealRange` (ex.: 40 unidades) do **portal de saída da própria zona**, a menos que esteja **marcado** pelo Sentinel; isso o torna alvo inválido para mira a distância. | Sentinel (marca revela), CDN (alcance), Gelo (retarda) |

**A fase 6 não traz inimigo novo:** a prova final reaproveita `fork`, `trojan`, `phishing` e os 8 inimigos e 3 chefes existentes (só `td-waves.json`). Regras comuns: cada inimigo novo entra nas **ondas autorais** (`td-waves.json`) das fases indicadas, **em grupos pequenos** (2 a 6 por onda) e **nunca** na onda 1 da fase em que é introduzido (o jogador já viu o conceito na onda anterior, no estilo SDD-28 RF-28.12). Cópias do `fork` contam no `Alive` da onda de origem; o teto `maxEnemiesOnField` (44) segura copias em cascata. `phishing` camuflado não aparece como alvo na mira, mas **continua sendo visível na UI** (ícone com contorno tracejado, SDD-20).

## 8. Determinismo, `StateHash` e ordem do `Rng`

### 8.1 Regra

**As 10 torres e os 3 inimigos não chamam o `Rng`.** Toda escolha (alvo, salto, torre mais próxima, posição de pod) é por critério numérico com desempate por menor X e menor `Id`. Assim a **ordem de consumo do `Rng` não muda**:

1. `BeginWave`: para cada grupo, para cada inimigo, 1 `NextUInt` (jitter de spawn);
2. despertar de runa por comando: 1 `NextUInt` por `AwakenRune`.

O conjunto exato de consumidores do `Rng` é o que as fichas TD.30a a TD.30c deixarem; a TD.31 **não acrescenta** nenhum.

**O que muda mesmo assim:** como `BeginWave` gasta 1 sorteio **por inimigo da onda**, adicionar `fork`/`trojan`/`phishing` às `td-waves.json` **altera a quantidade de sorteios** nas ondas em que entram (e portanto o `Rng` seguinte). Isso é inevitável e esperado: o `ContentHash` muda. **As cópias do `fork` não consomem sorteio** (como `SplitBoss`).

### 8.2 `StateHash`

- O hash já inclui `_content.ContentHash` (todo JSON de conteúdo): **mudar `td-towers.json`, `td-enemies.json` ou `td-waves.json` invalida todos os goldens/replays TD** (justificativa única por ficha de conteúdo; goldens **idle intactos**, que não dependem de `td-*.json`).
- **Estado novo que precisa entrar no hash** (campos acrescentados ao fim do bloco da torre/inimigo/nó, ordem estável):

| Estado | Onde | Quem usa |
|---|---|---|
| `RangeSeenIds` (ids ordenados dos inimigos dentro no tick anterior), `BurstLeft`, `BurstGapTicks` | `TdTower` | Functions (DP-31.2, a; nome/forma finais na TD.31a) |
| recarga do salto/marca/empurrão/cura/reativação: `CooldownTicks` e `NodeCdTicks` já existentes (reuso) | `TdTower` | Cosmos, Sentinel, Fila, Backup, Pipeline |
| `FocusTargetId`/`FocusHits` (já no hash) | `TdTower` | Pipeline |
| `VulnBonus`/`VulnTicks` (já no hash; **sem campo novo**, DP-31.4, a) | `TdEnemy` | Sentinel |
| `PushGraceTicks` | `TdEnemy` | Fila |
| `ShieldHp` | `TdEnemy` | Trojan |
| `SplitDone` (bit em `Triggers`) | `TdEnemy` | Fork Bomb (cópias nascem com o bit ligado) |
| `Revealed` (derivável de `VulnTicks > 0`; sem campo) | `TdEnemy` | Phishing |
| `Flags` bit 4 = pod, `LifeTicks` (existente) | `TdNode` | AKS |
| `_backupHealsThisStage` | `TdMatchState` | Backup |
| contagem de Cofres por partida (teto 2, DP-31.3; derivável das torres colocadas, sem campo se possível) e **perda acumulada de integridade** (DP-31.5) | `TdMatchState` | Cofre, Backup/estrelas |

- Valores em **ints/ticks** (pontos-base), sem `double` novo no estado onde der; `double` onde já há (X, Hp) com ordem de iteração fixa (lista por `Id`).
- Todo novo evento (`TdEventType`) é **anexado ao fim** do enum (valores existentes não mudam): `VaultPaid`, `CoreHealed`, `TowerRevived`, `EnemyPushed`, `PodSpawned`, `ShieldBroken`, `EnemyCopied`. `Events` fica fora do hash.

### 8.3 Ordem de processamento do tick (fixar para não divergir entre SOs)

Dentro de cada tick de mundo, estágio a estágio (proposta, a TD.31a confirma contra `TdMatchState.Tick.cs`/`Combat.cs`): (1) Policy/aura de armadura e Backup (reativação) → (2) Sentinel (marcação via `VulnBonus`) → (3) Fila (empurrão) → (4) ataques (Functions, Cosmos, CDN, Pipeline, existentes) → (5) nós e pods → (6) movimento dos inimigos → (7) mortes e cópias (Fork Bomb) → (8) vazamentos → (9) fechamento de onda (Cofre) → (10) integridade (cura do Backup). A ordem é **parte do contrato** e entra em teste de replay (T-31.14).

### 8.3.1 Ordem do dano (reaproveita ADR-031)

dano base → rampa do Pipeline → bônus de foco existente → marca do Sentinel/vulnerável → multiplicadores de habilidade → **armadura** (zerada se na aura da Policy, após perfuração) → escudo do Trojan → `max(1, floor(x))`.

## 9. Impacto no balanceamento dos bots (bloco separado, TD.31e)

Estado **conhecido hoje** (HEAD `a6d2298`):

- **Os bots não vencem mais as fases 4 e 6** (informação do usuário nesta ficha). Os `Skip` dos testes citam "as fases 3 a 6"; o que vale é a **medição** (T-31.20).
- **Três testes `Skip` no Core**, todos com a mesma razão ("TD.29a: sem runas de compra, o bot não vence mais as fases 3 a 6; recalibração do bot/conteúdo fica para ficha própria"):
  1. `tests/SideWar.Core.Tests/TdSanityBotTests.cs`: "TD.10 Médio vence as fases 1 a 5 com média de ★ >= 2";
  2. `tests/SideWar.Core.Tests/TdSanityBotTests.cs`: "TD.22 Otimizado vence com três estrelas nas seis fases";
  3. `tests/SideWar.Core.Tests/TdScoreTests.cs`: "RF-28.21 Calibration: optimized bot reaches MVP, medium stays Pleno/Senior, no-counter stays Estagiario".
- As metas do SDD-29 §12 (Otimizado 100%/3★, Médio ≥ 85%, SemContra 0%, mediana de 12 a 16 torres) **não estão sendo verificadas** enquanto os `Skip` existirem.

Impacto esperado das torres novas (hipótese, **não medida**): ampliam muito o espaço de estratégia (de 5 para 15 tipos); **mais opções ≠ mais poder** (o objetivo é contra-escolha), mas **Cofre** e **Pipeline + Policy** podem **inflar** a vitória (renda e anti-chefe), e **Functions** barata pode dominar o início. Por isso **nenhuma ficha de torre (31a a 31c) mexe em `TdSanityBot`/`TdBotPlans`**; todo o tratamento de bots fica na **TD.31e**:

- **B-31.1:** reativar os 3 `Skip` **primeiro** com o conteúdo de 5 torres (linha de base), registrando o que o bot vence hoje, por fase e perfil, em `specs/evidencias/TD.31e.md`.
- **B-31.2:** estender a DSL `TdBotPlans` com construção dos tipos novos e adicionar roteiros por fase que **usem a contra-escolha** (Policy contra blindados, Sentinel/Pipeline contra chefe, Backup contra drenagem, Cosmos/AKS contra enxame).
- **B-31.3:** recalibrar `base`/`range`/`damage` das 10 torres, HP e quantidade dos 3 inimigos e a renda do Cofre. **Decidido (DP-31.11): as metas do SDD-29 §12 são revisadas para 15 torres.** A TD.31e parte da linha de base (B-31.1), propõe as metas revistas (taxa de vitória por perfil, ★ médias, mediana de torres proporcional a 15 tipos) e as registra em `specs/evidencias/TD.31e.md` **e** numa atualização deste SDD. **Não é permitido** voltar a passar os testes apenas por mudar o limiar sem essa evidência.
- **B-31.4:** garantir que **nenhuma** estratégia "só 1 tipo novo" vença a fase 6 sem risco (anti-dominância): varrer 15 tipos × perfis × 20 seeds.
- **B-31.5:** os 3 testes deixam de ter `Skip` **ou** cada um ganha motivo atualizado e data (nenhum `Skip` fica sem dono).
- **B-31.6 (risco registrado, DP-31.7):** medir o efeito de `rangeBp` sobre a CDN (260 + runa) e de `waveCredits` somado ao Cofre (teto 2). Se qualquer um dominar, limitar **só por `stats`/JSON** (ex.: teto de alcance, `maxRangeBp` por torre) ou voltar ao usuário; **sem tipo de runa novo** nesta fase.
- **Patente:** os limiares 5.000/12.000/22.000/29.000/34.000 (SDD-29 §10, TD.22) podem precisar de nova calibração se as notas dos perfis mudarem.

## 10. Liberação gradual por fase (`unlockStage`)

Curva aprovada na SDD-39 (DP-39.2 e DP-39.6), substituindo a tabela anterior. A tabela abaixo mostra os tipos existentes; os novos são entregues em TD.39c/d.

| Fase | Torres existentes liberadas nessa fase | Total existente acumulado |
|---|---|---|
| 1 | Raio, Gelo, Canhão, Functions | 4 |
| 2 | Balanceador, Policy | 6 |
| 3 | Cosmos, Scale Set | 8 |
| 4 | CDN, Pipeline | 10 |
| 5 | Sentinel, Backup, Fila | 13 |
| 6 | Cofre, AKS | 15 |

- **DP-31.12 preservada:** `unlockStage` compara com o maior estágio concluído do perfil mais um; reiniciar em fase anterior mantém as torres liberadas.

- Cada torre nova aparece com **uma dica de 1ª vez** curta (≤ 6 palavras de função + 1 de contra, padrão `td.tipsSeen`), nunca bloqueante. Texto na §11.4.
- Torres não liberadas aparecem **apagadas com cadeado e "Fase N"** (padrão `TdIcon.Lock`), sem ocupar espaço extra: ver §11.2.

## 11. UI: painel rápido, categorias e miniaturas

Contexto: o painel de torres (TD.28c, `td.towerPanelPinned`, painel fixo discreto no cantinho, `TdUiPresenter`) hoje mostra 3 a 5 cards. Com 15 isso estoura a faixa e a tela.

### 11.1 Categorias (Ataque · Controle · Suporte · Economia)

| Categoria | Torres (nova em **negrito**) | Cor de borda (paleta SDD-20) |
|---|---|---|
| **Ataque** | Raio, Canhão, **Functions**, **Cosmos**, **CDN**, **Pipeline** | âmbar |
| **Controle** | Gelo, Balanceador, **Sentinel**, **Fila**, **Policy** | ciano |
| **Suporte** | Scale Set, **Backup**, **AKS** | verde |
| **Economia** | **Cofre** | dourado |

Cada torre declara `category` no JSON (campo novo, DP-31.1: derivar de `kind` não basta, p.ex. Balanceador é `blocker` e vai em Controle). O painel mostra **apenas os cards liberados** da categoria ativa, **no máximo 6** por categoria (Ataque lota com 6 na fase 6).

### 11.2 Forma do painel (discreto, sem rolagem pesada)

- **Faixa de 4 abas minúsculas** (ícone + inicial: ⚔ A · ❄ C · ⚙ S · ¢ E) em cima do painel; **toque troca a categoria**, sem animação longa, sem pausa. Categoria **sem nada liberado** não mostra aba (Economia só aparece na fase 6).
- **Cards em 1 linha horizontal** de ≤ 6 slots de 44 DIP (o snap de torre, RF-28.20), **sem rolagem** (6 cabem); o card mostra **só o miniatura + custo** (`⚡ 90`) e **o próximo preço** (SDD-29 §5.1). Detalhe (nome, "bom contra/ruim contra") em **tooltip** ao passar o mouse.
- **Lembra a última categoria** (setting opcional `td.towerPanelCategory`; falha de leitura = "Ataque"). **Atalhos** `1..4` para a categoria só quando o painel está em foco.
- **Painel fixo** ocupa a mesma altura de hoje (uma linha de cards + a linha de abas de ~14 DIP); o painel modal "Construir" (SDD-29 §14) usa as **mesmas categorias** como cabeçalho.
- Em tela pequena (≤ 560 × 380 DIP) as abas viram ícones sem inicial (layout compacto, §14.3 do SDD-29).

### 11.3 Miniaturas no `TdIconPainter` (10 novos ícones)

`TdIconPainter` (`src/SideWar.Rendering/Scene/TdIconPainter.cs`) desenha ícones **vetoriais por código**, num quadrado de 16 unidades (`u = size/16`), com `TdIcon` em `src/SideWar.Runtime/Td/TdUiModel.cs`. Acrescentar ao **fim** do enum `TdIcon` (valores existentes não mudam):

| `TdIcon` | Desenho proposto (16×16, traço) | Semântica |
|---|---|---|
| `Functions` | raio dentro de chaves `{ ⚡ }` | "só quando chamada" |
| `Cosmos` | 3 nós ligados por linhas em zigue-zague | cadeia |
| `Sentinel` | olho/alvo com retículo | marca |
| `Vault` | cofre: quadrado com disco e dois pés | economia |
| `Backup` | seta circular em volta de um ponto | restaurar |
| `Cdn` | globo (círculo + meridiano) | alcance |
| `Queue` | 3 blocos empilhados + seta para trás | fila/recuo |
| `Policy` | escudo com traço (check) | regra/armadura |
| `Aks` | hexágono de leme com 3 pontos | pods |
| `Pipeline` | 3 degraus crescentes com seta | rampa |

- Mesmas regras dos ícones atuais: **uma cor + acento**, **legível em 16 DIP**, sem texto, tema escuro/claro; mais **ícone de traço** para os 3 inimigos novos (SDD-20/09).
- **Golden:** `tests/SideWar.Rendering.Tests/Golden/` ganha um golden `td-icons-azure` (Linux para texto, mas ícones são vetoriais e valem nos dois SOs); a TD.31d gera e justifica.
- **Sem sprites nem assets binários** (consistente com TD.14).

### 11.4 Textos (≤ 6 palavras de função)

| Torre | Linha do card/dica |
|---|---|
| Functions | "Rajada ao entrar no alcance." |
| Cosmos | "Raio em cadeia: 4 alvos." |
| Sentinel | "Marca: todos causam +25%." |
| Cofre | "Rende créditos por onda." |
| Backup | "Reativa torres e cura núcleo." |
| CDN | "Longo alcance, dano baixo." |
| Fila | "Empurra inimigo para trás." |
| Policy | "Remove armadura na área." |
| AKS | "Invoca 3 pods bloqueadores." |
| Pipeline | "Dano cresce no mesmo alvo." |

Inspeção de `Strings.cs` e do JSON garante ≤ 6 palavras (CA-31.14).

## 12. Divisão em fichas

### 12.1 Princípios

- **Cada ficha deixa o jogo jogável** e os testes do Core verdes (exceto os 3 `Skip`, que só a TD.31e trata).
- **Conteúdo e Core andam juntos** em cada ficha de torre (uma torre sem Core é inútil); **UI e inimigos novos** ficam na TD.31d, que roda **depois** das anteriores (sem paralelismo, §12.4).
- Nenhuma ficha de torre usa o `Rng`; **cada ficha de conteúdo (31a, 31b, 31c, 31d) regenera os goldens TD** (DP-31.13, a; justificado no REPORT da ficha) e **mantém os idle intactos**.
- Ambiente: **🟢 WSL puro** nas fichas 31a a 31c e 31e; **🟡** na 31d (render/golden; testes interativos só com autorização do usuário, memória `sem-testes-interativos-sem-aviso`).

### 12.2 Resumo e dependências

| Ficha | Escopo | Depende de | Ambiente |
|---|---|---|---|
| ~~DEC-31~~ | **Fechada em 2026-11-17** (§2, decisões g a s) | este SDD | feito |
| **TD.31a** | Variações simples: **Functions, CDN, Policy** (RF-31.1, 31.6, 31.8) + ampliação do validador (kinds `control`/`economy`/`summoner`, `category`, estado do gatilho no `TdTower`) | TD.30c | 🟢 |
| **TD.31b** | **Cosmos, Sentinel, Fila, Pipeline** (RF-31.2, 31.3, 31.7, 31.10): cadeia, marca, empurrão, rampa; estado novo no hash | TD.31a (validador, `category`, ordem do tick §8.3) | 🟢 |
| **TD.31c** | **Cofre, Backup, AKS** (RF-31.4, 31.5, 31.9): economia, reativação/cura, pods | TD.31b (validador, tick) e, para os testes de Backup, o Parasita (já existe) | 🟢 |
| **TD.31d** | **UI, sprites e inimigos novos**: 10 ícones no `TdIconPainter`, categorias no painel rápido e no modal Construir, dicas de 1ª vez, inimigos `fork`/`trojan`/`phishing` no Core e nos ícones de traço; `unlockStage` (comparando com o maior estágio concluído do perfil) e `td-waves.json` das fases 3 a 6 | TD.31c | 🟡 |
| **TD.31e** | **Balanceamento e bots** (§9): `TdBotPlans`, reativar os 3 `Skip`, recalibração, anti-dominância, evidência | TD.31d | 🟢 |

Ordem: ver §12.4 (sequencial, sem paralelismo).

### 12.3 Arquivos de cada ficha

**TD.31a (Functions, CDN, Policy)**
- `src/SideWar.Core/Content/td-towers.json` (3 entradas: `functions`, `cdn`, `policy`, com `stats` novas e `category`).
- `src/SideWar.Core/Content/TdContentModels.cs`, `TdContentLoader.cs`, `TdContentJson.cs` (campo `category`, `minRange` se virar campo).
- `src/SideWar.Core/Content/TdContentValidator.cs` (`TowerKinds` ampliado só no necessário para Policy; validação de `stats` novas).
- `src/SideWar.Core/Td/TdMatchState.Combat.cs`, `TdMatchState.Tick.cs` (gatilho de borda, alcance mínimo, aura de armadura), `TdTypes.cs` (`TdTower`: estado do gatilho), `TdMatchState.Hash.cs`.
- `tests/SideWar.Core.Tests/TdAzureTowersTests.cs` (novo; T-31.1, T-31.6, T-31.8) e atualização dos goldens TD.

**TD.31b (Cosmos, Sentinel, Fila, Pipeline)**
- `src/SideWar.Core/Content/td-towers.json` (4 entradas).
- `src/SideWar.Core/Content/TdContentValidator.cs` (kind `control` já criado na TD.31a).
- `src/SideWar.Core/Td/TdTypes.cs` (`TdEnemy`: `PushGraceTicks`; eventos novos anexados ao fim; a marca usa `VulnBonus`), `TdMatchState.Combat.cs` (cadeia, marca, empurrão, rampa), `TdMatchState.Tick.cs` (ordem §8.3), `TdMatchState.Hash.cs`.
- `tests/SideWar.Core.Tests/TdAzureTowersTests.cs` (T-31.2, T-31.3, T-31.7, T-31.10) e goldens TD.

**TD.31c (Cofre, Backup, AKS)**
- `src/SideWar.Core/Content/td-towers.json` (3 entradas).
- `src/SideWar.Core/Content/TdContentValidator.cs` (regras de `economy`/`summoner`, `maxPerPlayer`).
- `src/SideWar.Core/Td/TdMatchState.cs` e `TdMatchState.Result.cs` (pagamento do Cofre ao fechar onda, teto de 2 Cofres, cura do núcleo, estrelas/pontuação pela perda acumulada, DP-31.5), `TdMatchState.Combat.cs` (reativação, pods atacantes como `TdNode` com flag, sem retenção), `TdTypes.cs`, `TdMatchState.Hash.cs`.
- `tests/SideWar.Core.Tests/TdAzureTowersTests.cs` (T-31.4, T-31.5, T-31.9), `TdScoreTests.cs` (regra de estrelas) e goldens TD.

**TD.31d (UI, sprites, inimigos novos)**
- `src/SideWar.Core/Content/td-enemies.json`, `td-waves.json` (fases 3 a 6), `td-stages.json` (`concept` se mudar), `TdContentValidator.cs` (`Traits` ampliado + `TraitParams`), `src/SideWar.Core/Td/TdMatchState.Combat.cs` (escudo, cópia ao morrer, camuflagem), `TdTypes.cs`.
- `src/SideWar.Runtime/Td/TdUiModel.cs` (`TdIcon` ampliado), `TdUiController.cs`/`TdUiLayout` (categorias, aba ativa), `Strings.cs` (10 + 4 textos e dicas).
- `src/SideWar.Rendering/Scene/TdIconPainter.cs` (10 miniaturas) e `TdUiRenderer.cs` (faixa de abas, ícones de traço dos inimigos novos).
- `src/SideWar.App/Composition/TdUiPresenter.cs`, `src/SideWar.Persistence/Settings/AppSettings.cs` e `SettingsSerializer.cs` (`td.towerPanelCategory`, opcional).
- `tests/SideWar.Runtime.Tests/Td/TdUiControllerTests.cs`, `tests/SideWar.Rendering.Tests/Golden/` (`td-icons-azure`, painel), `tests/SideWar.Core.Tests` (T-31.11 a T-31.13 dos inimigos).

**TD.31e (balanceamento)**
- `src/SideWar.Core/Td/TdBotPlans.cs`, `TdSanityBot.cs`, `TdRank.cs` (limiares se recalibrados).
- `src/SideWar.Core/Content/td-towers.json`, `td-enemies.json`, `td-waves.json`, `td-stages.json` (só números).
- `tests/SideWar.Core.Tests/TdSanityBotTests.cs`, `TdScoreTests.cs` (remover os 3 `Skip` conforme B-31.5), novo teste de anti-dominância.
- `specs/evidencias/TD.31e.md` (tabela antes/depois por fase e perfil, 20 seeds).

### 12.4 Ordem de execução (obrigatória)

**TD.30a → TD.30b → TD.30c → TD.31a → TD.31b → TD.31c → TD.31d → TD.31e**, **uma por vez**: as fichas mexem nos mesmos arquivos (`TdMatchState*.cs`, `TdTypes.cs`, `TdContentValidator.cs`, `td-*.json`, `TdUiController.cs`, `TdIconPainter.cs`), então **não há paralelismo**. Cada ficha só abre depois de a anterior estar commitada com testes verdes. As fichas TD.30a a TD.30c são anteriores e fora do escopo deste SDD; a TD.31a parte do estado em que elas deixarem o Core (inclusive do `Rng`, §8.1). O Cofre e o Backup não dependem de nenhuma delas.

## 13. Ideias adicionais (propostas, não aprovadas)

Nada aqui entra em ficha sem decisão do usuário. Custo de implementação: **P** (≤ 1 ficha pequena), **M** (1 ficha), **G** (várias fichas).

| # | Ideia | Custo | Dependências |
|---|---|---|---|
| 1 | **Monitor** (Azure Monitor): torre de suporte que **revela camuflados** (Phishing) no alcance e mostra o alcance/HP dos inimigos. | M | TD.31d (traço `stealth`), aura de revelação sem estado, UI de alcance |
| 2 | **Event Grid**: ao **morrer** um inimigo, a torre solta um **pulso** de dano pequeno em área (um por morte, com recarga). Boa contra `swarm`/`fork`. | M | evento de morte (ordem §8.3, etapa 7), teto de cascata (`maxEnemiesOnField`) |
| 3 | **Modo desafio por região Azure**: regras fixas por fase (ex.: sem Canhão em West Europe, orçamento de torres menor). | G | TD.31e (balanceamento), perfil (recorde por desafio), `td-stages.json` |
| 4 | **Melhorias por patente**: uma **melhoria passiva única** por torre, liberada pela patente (sem níveis nem ramos). **Conflito:** níveis de torre foram removidos e "não voltam" (§4); precisa de aprovação explícita. | G | `TdRank`, perfil, UI de painel |
| 5 | **Eventos de status do Azure** como **mutadores de onda** ("Degradação": −10% de alcance numa onda). Agenda **por número de onda**, sem `Rng`, para manter o determinismo. | M | `td-waves.json`, aviso discreto na UI, TD.31e |
| 6 | **Azure Advisor**: no cartão de derrota (TD.17) sugere **a torre que contra-ataca** o traço que mais vazou. Só texto. | P | cartão de derrota existente, contagem de vazamentos por traço |

## 14. Critérios de aceitação

| CA | Critério |
|---|---|
| **CA-31.1 a CA-31.10** | Um por torre, nas subseções RF-31.1 a RF-31.10. |
| **CA-31.11** | Os 3 inimigos do §7 existem, passam no validador (traços novos) e cada traço tem ≥ 1 torre forte e ≥ 1 fraca (matriz do §6.1, T-31.11). |
| **CA-31.12** | **Determinismo:** 5 execuções (Linux e Windows) de uma partida scriptada com **todas as 15 torres** e os 3 inimigos têm o mesmo `StateHash`; nenhuma mecânica nova avança o `Rng` (teste compara `RngState` com e sem as torres novas em uma onda sem inimigo novo, T-31.12). |
| **CA-31.13** | **Replay:** log com `BuildTower` das 10 novas, `SellTower`, `SetTargeting`, `BuyRune`/`Awaken` e `Pause`/`Resume` reproduz o hash (T-31.13). |
| **CA-31.14** | **Ordem do tick** do §8.3 fixada por teste de replay; textos ≤ 6 palavras (inspeção de `Strings.cs` e JSON, T-31.14). |
| **CA-31.15** | **Liberação (DP-31.12):** com o maior estágio concluído do perfil em 0 a 5 (fases 1 a 6 jogáveis) o painel mostra exatamente 3, 5, 7, 10, 13, 15 torres liberadas, **independente da fase da partida**; torres bloqueadas não são compráveis por comando (`BuildTower` rejeitado com o motivo existente de "não liberada") (T-31.15). |
| **CA-31.16** | **UI:** painel com 4 abas (aba sem nada liberado não aparece), ≤ 6 cards por aba, **sem rolagem**; goldens `td-icons-azure` dos 10 ícones e do painel em 3 categorias; DPI 100/150/200% (T-31.16). |
| **CA-31.17** | **Desempenho:** simulação com 44 inimigos, 15 torres e cadeia de Cosmos mede ≤ orçamento do tick (a definir com a medição SDD-16; rótulo "não medido" até medir, T-31.17). |
| **CA-31.18** | **Bots (TD.31e):** B-31.1 a B-31.5 cumpridos e documentados; nenhum `Skip` sem dono (T-31.18 a T-31.20). |

## 15. Cenários de teste

**T-31.1** a **T-31.10:** um por torre, no estilo dos testes do Core (`TdAzureTowersTests`), com conteúdo mínimo em memória (sem depender do JSON de produção) e seeds fixas. **T-31.11** matriz de contra-escolha e validador de traços. **T-31.12** determinismo e `RngState`. **T-31.13** replay com as 15 torres. **T-31.14** ordem do tick e textos. **T-31.15** liberação por fase e rejeição de torre não liberada. **T-31.16** UI e goldens. **T-31.17** desempenho. **T-31.18 a T-31.20** bots (linha de base, recalibração, anti-dominância).

## 16. Riscos

| Risco | Efeito | Mitigação |
|---|---|---|
| Cofre vira estratégia dominante (economia composta) | partida "ganha sozinha" | teto de 2 (DP-31.3); B-31.4 (anti-dominância); renda baixa no início |
| Functions barata domina início | fases 2/3 triviais | `cost` 60 + recarga 3 s; medir na TD.31e |
| Sentinel + Pipeline + Policy empilham | chefe cai rápido demais | ordem do dano §8.3.1; limites em `stats`; TD.31e |
| AKS duplica o Balanceador | torre redundante | **resolvido no desenho (DP-31.9):** pods atacantes que não bloqueiam; B-31.4 vigia dominância |
| `rangeBp` infla a CDN (260) e `waveCredits` soma com o Cofre | CDN fora de escala; renda acima do previsto | DP-31.7 (a): sem runa nova agora; **medir na TD.31e (B-31.6)** e limitar por `stats` se dominar |
| Painel com 15 cards fica poluído | quebra a visão "discreto" | categorias + liberação gradual + 6 cards por aba (§11) |
| Muitos goldens regenerados | ruído nos PRs | DP-31.13 (a): regenerar a cada ficha de conteúdo, justificado no REPORT |
| Replays/goldens TD antigos | invalidados | `ContentHash` muda; goldens idle intactos; nada legado a manter |
| Bots já falham nas fases 4 e 6 | impossível medir se as torres ajudam | bloco TD.31e começa pela linha de base (B-31.1) |

## 17. Referências internas

SDD-28 (RF-28.5 catálogo, RF-28.7 inimigos, RF-28.12 curva, RF-28.17 sem ensino, RF-28.20 snap de 44 DIP), SDD-29 (§5.1 custo crescente, §10 patente, §12 bots, §14 modal), `src/SideWar.Core/Content/td-towers.json`, `td-enemies.json`, `td-stages.json`, `td-waves.json`, `td-runes.json`, `src/SideWar.Core/Td/` (`TdTypes.cs`, `TdMatchState*.cs`, `TdBotPlans.cs`, `TdSanityBot.cs`), `src/SideWar.Rendering/Scene/TdIconPainter.cs`, `src/SideWar.Runtime/Td/TdUiModel.cs`, memórias `visao-jogar-no-trabalho` e `sem-testes-interativos-sem-aviso`.
