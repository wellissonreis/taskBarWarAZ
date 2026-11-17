# SDD-29: Página de runas do TD (grafo radial) no lugar da melhoria de torre

**Status:** PROPOSTA (ficha TD.18, 2026-11-17), aguardando as DPs da §13 · **Etapa:** ciclo "TD" · **Depende de:** SDD-28 (partida TD, RF-28.4, 28.5, 28.13, 28.21, 28.22), SDD-10/11 (núcleo determinístico), SDD-22 (hotspots), SDD-04 (cliques) · **Substitui (se aprovada):** em SDD-28, os níveis 1 a 3, os ramos A/B, os finais (capstones), o botão "Melhorar", `UpgradeTower`, `ChooseBranch`, `maxTowers` como teto de design e as partes da árvore de selos que viram obsoletas (§9). **Este documento não edita o SDD-28, `DECISOES.md` nem `PLANO.md`**: a revisão deles é a ficha TD.26.

> Todo número aqui é **valor inicial ajustável por JSON** e **não medido** (README §2.3). Entram em playtest e no `TdSanityBot` (§12) antes de valer.

## 1. Objetivo

Trocar "clicar na torre e melhorar" por duas decisões mais legíveis e mais táticas:

1. **Quantas torres de cada tipo construir** (sem limite de quantidade; o limite é o espaço na faixa e os créditos; cada torre extra do mesmo tipo custa **+15%**).
2. **Em que tipo especializar** numa **página de runas global**: um **grafo radial** (estilo árvore passiva) em que cada setor fortalece **todas** as torres de um tipo. Quem especializa 2 tipos e deixa os outros fracos joga diferente de quem espalha.

Diretriz do usuário: **geração Z, fácil de entender, taticamente complexo, pouca leitura.** Regra de ouro do documento: **cada runa = ícone + nome de 1 a 2 palavras + efeito de até 6 palavras com número.** O nó pequeno nem tem nome: só ícone e número.

## 2. Decisões do usuário (2026-11-17, fixas, não reabrir)

| # | Decisão |
|---|---|
| a | Sem upar torres. Não existe mais clicar na torre para melhorar. |
| b | **Uma página de runas global** com galhos por tipo de torre (mais um setor geral). Investir num tipo fortalece **todas** as torres daquele tipo em campo. |
| c | **Tokens** acumulam; o jogador monta a página. Fontes: fechar onda, onda sem vazamento, caixa-bomba, pergunta-bônus V/F do Preparo, chefe. |
| d | Torres **sem limite de quantidade**; limites = espaço na faixa e créditos; **custo +15% por torre adicional do mesmo tipo** (tema "cota de vCPU"); vender devolve conforme regra a definir (§5) e reduz o custo da próxima. |
| e | Cada runa: ícone + nome de 1-2 palavras + efeito em ≤ 6 palavras com número. |
| f | **Forma da página (imagem do usuário):** grafo radial como Path of Exile / árvore de runas do Ragnarok: nó central dourado, nós circulares pequenos ligados por linhas em padrão hexagonal, 2 linhas curtas por nó (ex.: "Atk +1.3"), **só se compra nó adjacente a um já comprado**, comprados acesos/prateados, disponíveis destacados, bloqueados apagados, alguns nós maiores ("notáveis") nos cruzamentos. |

## 3. Referências de mercado (o que copiar)

> Fonte: conhecimento prévio do autor da ficha, **não reverificado na web nesta ficha** (pesquisa rápida pedida, sem coleta nova). Tratar como direção de design, não como dado citável.

| Jogo | O que vale copiar | O que evitar |
|---|---|---|
| **Bloons TD 6, Monkey Knowledge** | Árvore **global** com um ramo por classe de torre (Primária, Militar, Mágica, Suporte); bônus **pequenos e numéricos**; pontos que acumulam jogando. Dá a sensação de "minha estratégia de macacos". | Poder permanente empilhado que torna fases antigas triviais; nós de 1 linha longa de texto. |
| **Kingdom Rush (melhorias por estrelas)** | Melhorias **por tipo de torre**, compradas com moeda de meta, **reembolso livre** (re-spec sem punição). Estimula experimentar. | Muitas camadas de nível em cada torre (é o que estamos removendo). |
| **Rogue Tower** | Escolhas **por partida** que redefinem a rodada (roguelite); sinergia entre tipos. | Aleatoriedade que quebra o determinismo auditável do nosso Core. |
| **Vampire Survivors** | Ícone + 1 número por upgrade; compra **instantânea, 1 toque**; power-ups que dizem o efeito sem texto; reatividade imediata ao comprar. | Lista plana sem estrutura (aqui queremos o grafo). |
| **LoL, páginas de runas** | **Keystone** (1 runa grande que muda o estilo, com troca) + menores; **páginas salvas** (presets); caminhos temáticos. | Tela de leitura densa. |
| **Path of Exile / Ragnarok (imagem do usuário)** | **Adjacência obrigatória** (o jogador "escala caminhos"); **notáveis** nos cruzamentos; **keystones** com custo e contrapartida; pan/zoom; destacar o caminho até um alvo; pontos de respec. | Grafo gigante (PoE tem ~1.300 nós; aqui são **67**). |

Síntese: **grafo pequeno** (67 nós), **bônus numéricos pequenos**, **notáveis com sinergia entre tipos**, **keystones com contrapartida**, **reembolso barato**, **compra com 1 clique**.

## 4. Estrutura da página: o grafo radial

### 4.1 Forma

Nó central **"Núcleo"** (dourado, já comprado, não custa) com **6 setores** em padrão hexagonal, um por tipo de torre + um setor **Geral**, na ordem em anel (a ordem define quem tem sinergia com quem):

```
                 Raio
        Geral  .--o--.  Gelo
              /  / \  \
             o--o  ◉  o--o        ◉ = Núcleo (dourado)
              \  \ /  /           o = nó pequeno   O = notável   ★ = keystone
     Scale Set '--o--'  Canhão    ◇ = notável de fronteira (sinergia)
               Balanceador
```

Cada setor tem **10 nós**; entre setores vizinhos há **1 notável de fronteira** (sinergia). Total: 6 × 10 + 6 + 1 (núcleo) = **67 nós**.

Estrutura de um setor (da borda do núcleo para fora; as arestas formam uma árvore com 2 ramos curtos, para existir escolha de caminho):

```
Núcleo ─ E ─ S1 ─ S2 ─ N1 ─ S3 ─ S4 ─ N2 ─ S5 ─ ★K
             └────────────── ◇ (liga ao setor vizinho a partir de N1 e N2)
```

(O desenho final do grafo, com 2 ramos paralelos por setor, fica para a ficha TD.19; o contrato é: **conexo, 6 simetrias, cada setor alcançável só pelo Núcleo ou por um ◇ de fronteira**.)

### 4.2 Regras de compra

- Só se compra um nó **adjacente a um já possuído** (o Núcleo conta). Um ◇ de fronteira é comprável se **qualquer** dos dois lados vizinhos for possuído.
- **Custos (tokens):** nó pequeno **1** · notável **3** · notável de fronteira **3** · keystone **5**. Um setor completo custa 6×1 + 2×3 + 5 = **17**; a página inteira, **~120** (ninguém a completa numa partida: é de propósito, ver §6).
- Estados visuais: **possuído** (aceso, prata), **comprável agora** (anel destacado, tokens suficientes e adjacente), **sem token** (adjacente mas faltam tokens: anel fraco com o custo), **bloqueado** (apagado). Linhas entre nós possuídos ficam acesas.
- Efeitos **somam** dentro do mesmo tipo e são **ints em pontos-base** (sem `double` no estado, mesma regra de arredondamento do dano atual).

### 4.3 Os nós (texto final dos nós: ícone + nome + ≤ 6 palavras)

Convenção: o **nó pequeno** mostra só o ícone do atributo e o número (ex.: "⚡ +6%"); o nome só aparece no tooltip. Notável e keystone mostram **nome + efeito**.

**Pequenos (1 token), por setor:** E, S1..S5 repetem 3 atributos do tipo (valores iniciais):

| Setor | Atributos dos nós pequenos (cada nó) |
|---|---|
| **Raio** (alvo único) | Dano +6% · Cadência +5% · Alcance +6% |
| **Gelo** (controle) | Lentidão +5% · Alcance +6% · Cadência +5% · Dano +10% (o dano base é muito baixo) |
| **Canhão** (área, só solo) | Dano +6% · Área +8% · Cadência +5% · Alcance +6% |
| **Balanceador** (retém) | Duração do nó +10% · Dano do nó +10% · Recarga do nó −10% · Alcance +6% |
| **Scale Set** (suporte) | Bônus da aura +5% · Alcance +8% |
| **Geral** | Crédito por morte +4% · Bônus de onda +8% · Recarga de habilidade −5% · Venda +5 p.p. (70 → 75%) · Integridade +1 (1 nó só) |

**Notáveis (3) e keystone (5):**

| Setor | Notável 1 | Notável 2 | ★ Keystone (com contrapartida) |
|---|---|---|---|
| **Raio** | **Perfurar** · Ignora 30% de armadura | **Rajada** · 4º tiro causa dano ×2 | **Foco total** · Dano +60%, cadência −25% |
| **Gelo** | **Congelar** · Paralisa 1 s a cada 5 s | **Corroer** · Alvo recebe +15% de dano | **Inverno** · Lentidão em área; alcance −20% |
| **Canhão** | **Estilhaço** · Área causa 80% do dano | **Penetrador** · +40% dano em blindado e chefe | **Saturação** · Deixa fogo por 3 s |
| **Balanceador** | **Pool** · +1 nó simultâneo | **Failover** · Nó explode ao fim | **Anycast** · Nós nascem na frente; retém +1 |
| **Scale Set** | **Autoscale** · Vizinhas: cadência +10% | **Réplica** · Vizinhas: dano +10% | **Elástico** · Escalar dura +4 s |
| **Geral** | **Cota folgada** · Custo +15% vira +12% | **Chamada ágil** · Chamar onda paga +25% | **Diversidade** · +5% dano por tipo distinto em campo |

(Os efeitos de Raio/Gelo/Canhão/Balanceador/Scale Set reaproveitam as chaves de `stats` dos ramos A/B e capstones atuais do `td-towers.json`, hoje lidas pelo combate: `armorPenetration`, `superHitEvery`, `paralyzeEverySeconds`, `vulnerableBonus`, `splashFactor`, `burnZoneSeconds`, `nodes`, `nodeHolds`, `explodeOnEnd`, `spawnAtFront`, `neighborCadenceBonus`, `neighborDamageBonus`. A ficha TD.21 muda **a origem** dessas chaves de "nível/ramo da torre" para "runas do tipo".)

**Notáveis de fronteira ◇ (3 tokens), as sinergias entre tipos:**

| Entre | Nome | Efeito (≤ 6 palavras) |
|---|---|---|
| Raio · Gelo | **Alvo marcado** | Raio +25% dano em lentos |
| Gelo · Canhão | **Estilhaçar** | Canhão +30% dano em lentos |
| Canhão · Balanceador | **Martelo e bigorna** | Canhão +40% dano em retidos |
| Balanceador · Scale Set | **Pool elástico** | Nós duram +20% com Scale Set |
| Scale Set · Geral | **Auto-orçamento** | Torres custam −5% |
| Geral · Raio | **Pico de uso** | Raio +10% dano sem vazamento |

### 4.4 Tensão estratégica (por que especializar)

- **Cada setor completo ≈ ×1,8 de força do tipo** (17 tokens); com ~20 a 35 tokens por partida (§6) o jogador **completa 1 setor e meio**, não 6.
- Os **keystones** têm contrapartida (Foco total, Inverno) e **Diversidade** puxa para o lado oposto (muitos tipos): é a decisão "especialista × generalista".
- O custo crescente (+15%) pune "10 torres do mesmo tipo" **e** a página recompensa "tudo no mesmo tipo": a mistura certa depende da fase (voador pede Raio/Gelo; blindado pede Canhão/Penetrador).
- Os ◇ obrigam a **escolher uma direção** (Raio → Gelo → Canhão…): sinergia custa caminho.

## 5. Economia de torres sem nível

### 5.1 Custo crescente (decisão d)

`custo(tipo, n) = round(base × 1,15^n)`, com `n` = quantas torres **desse tipo** existem agora (inclui as SEM POSIÇÃO, que ainda contam como quota). Em **inteiros**: tabela de fatores em pontos-base pré-computada (`1,15^n`, n 0..24) no Core, sem `Math.Pow` (determinismo). Exemplo do Raio (base 90): **90 · 104 · 119 · 137 · 157 · 181 · 208 · 239 · 275 · 317** (10 torres ≈ 1.727 créditos). O Popover Construir mostra o **próximo preço** e o contador ("⚡ ×3 · 137").

### 5.2 Venda (a definir; recomendação marcada como DP-29.5)

**Recomendado:** vender devolve **70% do preço da última torre daquele tipo** (`0,7 × custo(tipo, n−1)`, onde `n` é a contagem antes de vender). Efeitos: (1) torres do mesmo tipo são **fungíveis** (vender a "mais velha" ou a "mais nova" rende o mesmo), sem exploit de comprar barato e revender; (2) vender **reduz a contagem** e, portanto, o custo da próxima; (3) comprar e vender logo em seguida perde sempre 30%. Alternativa: devolver 70% do que **aquela** torre custou (`Invested`, como hoje).

### 5.3 Limite de torres

- Sai o teto de design (`maxTowers` 6 a 8 do `td-stages.json`). **Sobra o espaço da faixa** (snap de 44 DIP, RF-28.20) e os créditos. O desafio **Frugal** continua com seu teto (`maxTowers` do desafio).
- **Teto técnico de segurança** (perf, não design): 24 torres no total (valor do ADR-034). Ver DP-29.7 e o risco R-29.1 (barras largas facilitam; ver §14).

## 6. Tokens: fontes, valores iniciais e ritmo

Tokens são **inteiros**, entram no `StateHash` e **não** têm decimais. Fontes (valores iniciais, não medidos):

| Fonte | Tokens | Regra |
|---|---|---|
| **Início da partida** | **+2** (+ selos de meta, §9) | permite comprar 2 nós já no Preparo da onda 1 |
| **Fechar onda** | **+1** | onda concluída (todos mortos ou vazados) |
| **Onda sem vazamento** | **+1** | nenhum inimigo chegou ao núcleo naquela onda |
| **Caixa-bomba coletada** | **+1** | `CollectCrate` aceito (RF-28.19); explodir não dá |
| **Pergunta-bônus V/F acertada** | **+1** | `AnswerBonus` com acerto (RF-28.23), 1 por Preparo; erro = 0 |
| **Chefe derrotado** | **+3** | chefe morto (não vazado); o selo/estrela de chefe segue o SDD-28 |

Ritmo esperado (fase 1, 10 ondas): 2 + 10 + ~8 + ~2 + ~4 + chefe ≈ **26** tokens; fase 6 (15 ondas) ≈ **40**. Com custos 1/3/5 isso é **1 a 2 setores** por partida. O **recibo do token aparece ao fechar a onda** ("+2 ◈"), no HUD, sem texto longo.

Tokens **não** têm teto e **não** ganham juros. **Sobra de token** não entra na pontuação (a pontuação do RF-28.21 já tem "créditos sobrando"; evitar poluir a fórmula). Pergunta: derrota dá tokens? Não: a partida acabou, a página reseta (§7).

## 7. A página é por partida ou persistente? (DP-29.1)

**Recomendação (DP-29.1): página POR PARTIDA (reseta), com a árvore de selos mantida como meta enxuta que dá tokens iniciais.**

- **Por quê:** cada partida vira uma **decisão de build** ("nesta fase vou de Gelo + Canhão"), como Rogue Tower/Vampire Survivors; o bot e os testes ficam simples; não há poder permanente que trivialize fases antigas (o problema do Monkey Knowledge); o determinismo é limpo: a página vive **dentro do estado da partida**, e o log basta.
- **Como convive com os selos:** os selos (estrelas, coroa, medalhas) **continuam** e compram melhorias **permanentes pequenas** (`td-meta.json`); os nós obsoletos de infraestrutura (§9) viram **"Tokens iniciais"** (+1 token inicial por nó, até +3) e o resto da árvore fica. Selos = **ponto de partida**; runas = **estratégia da partida**.
- **Alternativa (persistente):** a página é do perfil; tokens ganhos são permanentes; os selos viram tokens (a árvore de selos desaparece). Dá a sensação PoE de "ir escalando para sempre", mas (1) fases iniciais viram triviais com o tempo; (2) o perfil precisa guardar a página (e entrar no cabeçalho do replay); (3) o balanceamento passa a depender do progresso do jogador. Se escolhida, a página guarda só o bitset de nós e permite **respec grátis entre partidas**.

## 8. Respec e reembolso (DP-29.3)

- **Comprar** vale em **Preparo e Onda** (decisão sob pressão é o ponto do jogo).
- **Reembolso:** só em **Preparo**, só de **nó folha** (nenhum possuído depende dele; mantém o grafo conexo), **100% dos tokens**, sem custo, um clique ("clique de novo no nó aceso", ou clique direito). Em Onda o reembolso é **bloqueado** (evita trocar de build a cada inimigo).
- **Reiniciar tudo** ("↺ Zerar"): reembolsa todos os nós folha em cadeia, só em Preparo.
- Comandos: `BuyRune(RuneId)`, `RefundRune(RuneId)` (§11).
- **Alternativa:** reembolsar a qualquer momento (mais permissivo, mais fácil de abusar entre ondas) ou reembolsar 75% (custa um pouco, mais coerente com "decisões importam").

## 9. Convivência com a árvore de selos e conteúdo existente

| Conteúdo atual | Destino |
|---|---|
| `td-towers.json` `levels[]`, `branches[]` (A/B), `capstone` | **Removidos.** Ficam só stats base e `kind`/`targets`. Os valores de `stats` dos ramos migram para `td-runes.json`. |
| `td-towers.json` `cost` | **Fica** como base de `custo(tipo, n)`. |
| `td-stages.json` `maxTowers` (6 a 8) | Torna-se **opcional/nulo** nas fases; só o desafio Frugal mantém teto. |
| `rules.sellRefund` 0.7 | **Fica**, mas aplicado a `custo(tipo, n−1)` (§5.2). |
| `td-meta.json` nós `infrastructure.sell75`, `infrastructure.slots1`, `infrastructure.slots2` | **Obsoletos** (venda agora é regra; `maxTowers` some). Viram "Tokens iniciais" (§7). |
| `td-meta.json` nós de Defesa/Operação (dano, alcance, integridade, recarga, snapshot, auto-início) | **Ficam.** Mas "Dano +3% global" e "Alcance +5%" **somam** com as runas (cuidado no balanceamento, §12). |
| `td-abilities.json` | Inalterado. Runas Gerais mexem só na recarga. |
| `td-questions.json` / `AnswerBonus` | Inalterado; acerto agora também dá **+1 token** (§6). |
| Selos de coroa/medalha/estrela | Inalterados (§10). |

## 10. Impacto em coroa, medalhas, pontuação e patente

- **Coroa e medalhas** (RF-28.21): inalteradas em regra. **"Sem vender"** continua (nenhum `SellTower`); com custo crescente, vender passa a ser uma decisão real, então a medalha ganha sentido. **Medalha nova** opcional (pós-MVP, DP-29.8): **"Especialista"** = vencer com ≥ 70% dos tokens gastos num só setor.
- **Pontuação:** a fórmula do RF-28.21 **não muda** (integridade, tempo, chamadas, créditos sobrando). Melhorias nunca entraram nela.
- **Patente:** os limiares (5.000 · 12.000 · 20.000 · 25.000 · 31.000) foram calibrados com o bot **antes** das runas. **A TD.22 recalibra** e reconfirma: Otimizado = MVP, Médio = Pleno/Sênior, SemContra = Estagiário (CA-28.17).
- **Perfil v2 → v3:** ver §15 (migração).

## 11. Determinismo e comandos

Tudo continua determinístico e no log do replay.

- **Comandos novos:** `BuyRune(string RuneId)`, `RefundRune(string RuneId)`. Rejeições com `CommandReason` novos (`RuneNotAdjacent`, `RuneOwned`, `RuneNoTokens`, `RuneNotLeaf`, `RuneLockedInWave`, `UnknownRune`).
- **Comandos removidos:** `UpgradeTower`, `ChooseBranch` (um log antigo que os contenha é **rejeitado** na leitura: não há replay legado a manter, o `ContentHash` muda).
- **Estado novo** (entra no `StateHash`): `Tokens` (long), `OwnedRunes` (bitset por ordem do JSON). Modificadores do tipo são **derivados** na compra/reembolso (cache fora do hash).
- **Tokens** são creditados por eventos do Core (fim de onda, `CollectCrate`, `AnswerBonus`, chefe morto): sem relógio, sem aleatório.
- **Goldens:** diferente da TD.13/15, aqui o `StateHash` **muda** (estado novo e regras de custo), então os goldens TD **são regenerados** (justificado, uma vez, na TD.21). Goldens **idle intactos**.

## 12. Balanceamento e o `TdSanityBot`

Mudança estrutural: o poder que vinha de "nível 3 = ×2,2 de dano" passa a vir de **mais torres** (limitado pelo custo +15%) e **runas** (~26 a 40 tokens). Ações da TD.22:

- **Roteiros do bot (DSL de `TdBotPlans`)** ganham passos `rune <id>` e `runeSector <setor> <n>`; a ordem de compra é resolvida por `TdRuneGraph.PathTo(alvo, possuídos)` (ajudante do Core, também usado pela UI para destacar o caminho até um alvo).
- **Perfis:** **Novato** compra o setor mais barato e adjacente ao acaso do roteiro (Geral, depois Raio); **Médio** especializa **1 setor** pelo contra da fase (usa o `TdBotPlans` atual para o tipo); **Otimizado** vai ao keystone/notáveis do setor ideal e a 1 ◇ de sinergia; **SemContra** **não compra** setor do contra.
- **Metas (20 seeds, ordem de grandeza da TD.10; a confirmar):** fases 1 a 5 Otimizado 100% e ★ médio ≈ 3; Médio ≥ 85% e ★ 1,1 a 2,9; Novato ≤ 100% mas com perda de integridade; SemContra 0% de vitória. Fase 6 (prova final): Otimizado 100% e Novato 0%.
- **Pontos a recalibrar:** créditos iniciais e bônus de onda (antes pagavam níveis; agora pagam quantidade), HP dos inimigos, o preço base das torres, o ganho por setor (×1,8) e a soma com os selos "Dano +3%/Alcance +5%".
- **Evidência:** `specs/evidencias/TD.22.md` com a tabela antes/depois como a da TD.10.

## 13. Decisões pendentes (tabela final)

| ID | Pergunta | Recomendação | Alternativa |
|---|---|---|---|
| **DP-29.1** | Página por partida (reseta) ou persistente? Como convive com os selos? | **Por partida**; selos dão tokens iniciais e as melhorias pequenas da árvore atual ficam (§7) | Persistente no perfil, selos viram tokens, respec grátis entre partidas |
| **DP-29.2** | Tamanho/forma do grafo e custos (67 nós, 1/3/3/5) | Como na §4 | Grafo menor (36 nós, só 6 notáveis) ou maior com 2 anéis de keystones |
| **DP-29.3** | Reembolso: só Preparo e só folha, 100%? | **Sim** (§8) | A qualquer momento; ou 75% |
| **DP-29.4** | Comprar runa **durante a Onda**? | **Sim** (decisão sob pressão) | Só em Preparo (mais legível, menos tático) |
| **DP-29.5** | Venda: 70% do preço da última torre do tipo? | **Sim** (§5.2): fungível e sem exploit | 70% do investido na torre específica |
| **DP-29.6** | Fontes e valores de tokens (§6) | Como na §6; recalibrar na TD.22 | Menos tokens (só fechar onda e chefe) |
| **DP-29.7** | Teto técnico de 24 torres e `maxTowers` das fases | Teto técnico 24; `maxTowers` nulo nas fases; só Frugal limita | Manter `maxTowers` alto por fase (12 a 16) para normalizar entre monitores |
| **DP-29.8** | Medalha nova "Especialista" e desafio "Sem runas" | Pós-MVP (TD.27) | Incluir já |
| **DP-29.9** | Crescimento de custo `+15%` fixo ou por tipo? | **Fixo 15%** (Cota folgada reduz p/ 12%) | Por tipo (Raio 15%, Gelo 12%) |
| **DP-29.10** | Presets/"páginas salvas" (estilo LoL) | Pós-MVP (TD.27) | Incluir já (3 páginas) |

## 14. UI na faixa de 64 DIP: a página abre FORA da faixa

A faixa de 64 DIP é pequena demais para um grafo. A página é um **painel (janela) acima da barra**, no padrão dos popovers do SDD-28 (RF-28.14 e TD.16), com **tamanho fixo pequeno** e pan/zoom.

- **Abrir:** botão **◈ Runas** no HUD (badge com o saldo de tokens; **pulsa** quando há token e nó comprável) e atalho de teclado. Fechar: ESC, clique fora ou o botão de novo.
- **Tamanho:** painel de **560 × 420 DIP** na escala do HUD (`TdBand.HudScale`), ancorado acima do HUD; cabe os 67 nós visíveis sem zoom (nós pequenos de 18 DIP, notáveis de 28, keystones de 34).
- **Pan e zoom:** arrastar para mover; roda do mouse para zoom **0,6× a 1,6×**; **duplo clique** no fundo centraliza no **último nó comprado** (ou nos compráveis agora). Ao abrir, a página **já centraliza** no setor em que o jogador mais investiu.
- **Pouca leitura:** o grafo mostra só **ícones e números**; o **nome e o efeito (≤ 6 palavras)** aparecem em **tooltip no hover** e **fixos** para notáveis/keystones com zoom ≥ 1×. Cores por setor (cores de torre do SDD-20); estados possuído/disponível/sem token/bloqueado (§4.2).
- **Interação:** **1 clique = comprar** (nó comprável); **clique de novo no nó aceso** (folha, em Preparo) = reembolsar; clique no nó bloqueado destaca o **caminho mais curto** até ele (`PathTo`) e mostra o custo total em tokens.
- **Durante a Onda:** o painel **não pausa** o jogo. Em Onda ele fica **translúcido** e **compacto** (só o setor focado), para a pessoa continuar vendo a faixa; compra permitida, reembolso não (§8).
- **Cliques:** o painel é uma região de captura de cliques (SDD-04) que **só existe enquanto aberto**; a faixa e as torres continuam clicáveis fora dele.
- **Torre (popover) sem melhoria:** o popover da torre fica com **Mira** e **Vender (+valor)**; some "Melhorar" e "Ramo". O popover **Construir** mostra o próximo preço e a contagem.
- **Esmaecer e foco:** o painel segue as regras de foco/escala da TD.14 e **nunca** esmaece sozinho.

## 15. Migração de perfil e conteúdo

- **Perfil TD v2 → v3** (`sidewar.td-profile`): (1) nós obsoletos da árvore de selos (`infrastructure.sell75`, `slots1`, `slots2`) são **removidos e reembolsados em selos**, sem perda; (2) IDs desconhecidos saneados; (3) se a DP-29.1 for "por partida", **nada de runa vai ao perfil**; se "persistente", o perfil ganha `runes` (lista de IDs) e `tokens`.
- **Conteúdo:** novo `td-runes.json` (setores, nós, arestas, efeitos) entra no `ContentHash`; `td-towers.json` perde `levels/branches`; `td-stages.json`/`td-meta.json` ajustados.
- Settings (`td.tutorialDone`, `td.tipsSeen`) **não mudam**.

## 16. Tutorial e "Como jogar" (TD.17)

A TD.17 **não** cria passos de "melhorar torre/ramo A/B". Depois que as runas entrarem (TD.25):

- **Passo condicional (≤ 8 palavras):** ao fechar a 1ª onda com token: balão "◈ Token novo! Abra Runas" com **seta no botão ◈ Runas**; abrindo: "Compre um nó aceso" apontando o nó comprável; depois: "Foque num tipo de torre". Segue as regras do RF-28.22 (avança por ação real, "Pular tutorial" sempre visível, sem comando novo só pela dica).
- **Dica de 1ª vez:** o setor de cada tipo, uma linha curta ("⚡ Raio: foco em 1 alvo. Compre Dano.").
- **Como jogar:** o cartão ganha uma linha de runas e outra de custo crescente (ex.: "Cada torre igual custa +15%").

## 17. Critérios de aceitação e testes

| CA | Critério |
|---|---|
| **CA-29.1** | Não existem `UpgradeTower`/`ChooseBranch`; `td-towers.json` sem `levels/branches`; nenhum botão "Melhorar" na UI (T-29.1) |
| **CA-29.2** | Custo da n-ésima torre do mesmo tipo = `round(base×1,15ⁿ)` pela tabela inteira; vender devolve 70% de `custo(n−1)` e reduz o próximo custo (T-29.2, T-29.3) |
| **CA-29.3** | Sem limite de quantidade além do espaço e dos créditos (e do teto técnico); desafio Frugal respeita seu teto (T-29.4) |
| **CA-29.4** | `td-runes.json` válido: grafo **conexo**, cada nó alcançável do Núcleo, custos 1/3/3/5, sem ciclo de dependência de efeito (T-29.5) |
| **CA-29.5** | Só compra adjacente a possuído; ◇ por qualquer lado; sem token rejeita; reembolso só de folha e só em Preparo (T-29.6, T-29.7) |
| **CA-29.6** | Tokens: +2 no início, +1 onda, +1 sem vazamento, +1 caixa, +1 pergunta acertada, +3 chefe (T-29.8) |
| **CA-29.7** | Runas de um tipo afetam **todas** as torres daquele tipo em campo, inclusive as construídas depois; sinergias ◇ condicionais (T-29.9) |
| **CA-29.8** | Replay com `BuyRune`/`RefundRune` reproduz o `StateHash`; goldens TD regenerados; goldens idle intactos (T-29.10) |
| **CA-29.9** | O bot: Otimizado 100% em 1 a 5, Médio ≥ 85%, SemContra 0%; patente recalibrada (T-29.11) |
| **CA-29.10** | UI: painel abre/fecha, 1 clique compra, pan/zoom, caminho até alvo, **sem texto > 6 palavras por runa** (inspeção de `Strings.cs` + T-29.12) |
| **CA-29.11** | Migração v2 → v3 sem perder selos (T-29.13) |

Testes: **T-29.1** conteúdo sem `levels/branches`; **T-29.2** tabela de custo inteira; **T-29.3** venda/fungibilidade; **T-29.4** teto técnico e Frugal; **T-29.5** validador do grafo; **T-29.6** adjacência e ◇; **T-29.7** reembolso; **T-29.8** fontes de token; **T-29.9** efeito por tipo e sinergias; **T-29.10** replay/hash; **T-29.11** bot e patente; **T-29.12** UI (hit-test, pan/zoom, goldens `td-ui-runes`); **T-29.13** migração de perfil.

## 18. Fichas de implementação (TD.19+)

| Ficha | Descrição | Depende de | Ambiente |
|---|---|---|---|
| **TD.19** | Conteúdo: `td-runes.json` + modelos/validador (grafo conexo, custos, efeitos); remover `levels/branches` de `td-towers.json`; `maxTowers` opcional; `rules` de custo (`towerCostGrowth`) e fontes de token | TD.18 aprovada | 🟢 |
| **TD.20** | Core economia: custo crescente por tipo (tabela inteira), venda (§5.2), remover `UpgradeTower`/`ChooseBranch`, teto técnico, espaço | TD.19 | 🟢 |
| **TD.21** | Core runas: tokens, `BuyRune`/`RefundRune`, modificadores por tipo, mover efeitos de ramos/capstones para runas, sinergias ◇, `StateHash`, replay; regenerar goldens TD | TD.19, TD.20 | 🟢 |
| **TD.22** | Bot e balanceamento: `rune`/`runeSector`, `TdRuneGraph.PathTo`, recalibrar curva, créditos e patente; evidência `TD.22.md` | TD.21 | 🟢 |
| **TD.23** | Perfil v2 → v3 e árvore de selos (tokens iniciais, migrar nós obsoletos); persistência conforme DP-29.1 | TD.19, TD.21 | 🟢 |
| **TD.24** | UI: painel de runas (grafo radial, estados, pan/zoom, tooltips, caminho até alvo), botão ◈ no HUD, Construir com preço crescente, popover de torre sem "Melhorar", entrada de cliques | TD.21, TD.14 | 🟡 |
| **TD.25** | Tutorial/"Como jogar" e dicas de runas (§16) | TD.17, TD.24 | 🟡 |
| **TD.26** | Documentação: revisar SDD-28 (RF-28.4/28.5/28.13/28.22, CA/T), ADR, `PLANO.md`, MTC | TD.19 a TD.25 | 🟢 |
| **TD.27** (pós-MVP) | Medalha "Especialista", desafio "Sem runas", presets de página (DP-29.8, DP-29.10) | TD.24 | 🟡 |

Ordem: TD.19 → TD.20 → TD.21 → (TD.22 ‖ TD.23) → TD.24 → TD.25 → TD.26; TD.27 por último.

## 19. Riscos

| ID | Risco | Mitigação |
|---|---|---|
| R-29.1 | Barras largas dão mais espaço e facilitam (perde a normalização do `maxTowers`) | Custo +15% já limita; teto técnico 24; DP-29.7 |
| R-29.2 | Grafo difícil de ler em 560 × 420 | Só ícones/números; hover para texto; caminho destacado; zoom |
| R-29.3 | Comprar durante a Onda atrapalha quem joga ao lado do trabalho | Painel translúcido e compacto; compra opcional; nunca pausa |
| R-29.4 | Setores com um tipo dominante (Raio ×1,8 resolve tudo) | Bot por perfil; keystones com contrapartida; Diversidade; recalibrar |
| R-29.5 | O reaproveitamento dos efeitos dos ramos no combate quebra o determinismo | Ficha TD.21 migra só a **origem** dos `stats`; testes de hash e golden regenerado |
| R-29.6 | Selos "Dano +3%/Alcance +5%" somam com runas e inflam | Rever na TD.22 (ou cortar da árvore de selos) |
