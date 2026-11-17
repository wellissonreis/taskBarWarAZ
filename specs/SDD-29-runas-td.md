# SDD-29: Página de runas do TD (grafo radial, moeda única) no lugar da melhoria de torre

**Status:** PROPOSTA REVISADA (fichas TD.18 e TD.18b, 2026-11-17); decisões do usuário registradas em §2; DPs restantes na §13 · **Etapa:** ciclo "TD" · **Depende de:** SDD-28 (partida TD, RF-28.4, 28.5, 28.13, 28.21, 28.22), SDD-10/11 (núcleo determinístico), SDD-22 (hotspots), SDD-04 (cliques), SDD-05 (DPI/monitores) · **Substitui (se aprovada):** em SDD-28, os níveis 1 a 3, os ramos A/B, os finais (capstones), o botão "Melhorar", `UpgradeTower`, `ChooseBranch`, `maxTowers` como teto de design, o HUD/popovers "abertos sobre a faixa" (viram a modal central, §14) e as partes da árvore de selos que ficam obsoletas (§9). **Este documento não edita o SDD-28, `DECISOES.md` nem `PLANO.md`**: a revisão deles é a ficha TD.26.

> Todo número aqui é **valor inicial ajustável por JSON** e **não medido** (README §2.3). Entram em playtest e no `TdSanityBot` (§12) antes de valer.

## 1. Objetivo

Trocar "clicar na torre e melhorar" por duas decisões mais legíveis e mais táticas, pagas com **uma só moeda, os créditos**:

1. **Quantas torres de cada tipo construir** (sem limite por fase; o custo cresce **muito** a cada torre do mesmo tipo).
2. **Em que tipo especializar** numa **página de runas por partida**: um **grafo radial** (estilo árvore passiva) em que cada setor fortalece **todas** as torres de um tipo.

Como é a mesma moeda e tudo é reversível, o jogador decide o balanço a qualquer momento: de "5 torres e nenhuma runa" a "vendi tudo e só tenho runas do Raio".

Diretriz do usuário: **geração Z, fácil de entender, taticamente complexo, pouca leitura.** Regra de ouro: **cada runa = ícone + nome de 1 a 2 palavras + efeito de até 6 palavras com número.** O nó pequeno nem tem nome: só ícone e número.

## 2. Decisões do usuário (fixas, não reabrir)

### 2.1 De 2026-11-17 (ficha TD.18)

| # | Decisão |
|---|---|
| a | Sem upar torres. Não existe mais clicar na torre para melhorar. |
| b | **Uma página de runas global** com galhos por tipo de torre (mais um setor geral). Investir num tipo fortalece **todas** as torres daquele tipo em campo. |
| e | Cada runa: ícone + nome de 1-2 palavras + efeito em ≤ 6 palavras com número. |
| f | **Forma da página (imagem do usuário):** grafo radial como Path of Exile / árvore de runas do Ragnarok: nó central dourado, nós circulares pequenos ligados por linhas em padrão hexagonal, 2 linhas curtas por nó, **só se compra nó adjacente a um já comprado**, comprados acesos/prateados, disponíveis destacados, bloqueados apagados, alguns nós maiores ("notáveis") nos cruzamentos. |

### 2.2 De 2026-11-17 (ficha TD.18b, **substituem** o que for conflitante da TD.18)

| # | Decisão | Onde está |
|---|---|---|
| 1 | **Moeda única circular.** Não existem tokens. Runas são compradas com **créditos**, a mesma moeda das torres. Vender torre devolve créditos que podem virar runa e vice-versa (devolver runa devolve créditos). Dá para vender todas as torres e focar nas runas do Raio. **Substitui** a decisão (c) "tokens" da TD.18. | §5, §6, §8 |
| 2 | **Tudo a qualquer momento**, inclusive durante a onda: comprar e devolver runas, **100%**, sem taxa. | §8 |
| 3 | **Liberdade total:** o jogador escolhe quando e o que montar; nada de direcionar ou forçar caminho, além da adjacência do grafo radial (do centro para fora). | §4.2, §14.5 |
| 4 | **A página é por partida** (consequência da moeda única de partida). Árvore de selos permanente fica como está; os 3 nós obsoletos viram **créditos iniciais** (recomendado). | §7 |
| 5 | **Tudo que o usuário abre é uma modal no centro da tela que PAUSA o jogo**: HUD completo/painel de construção, página de runas, inspetor de torre, "Como jogar", configurações do jogo, cartões de vitória/derrota. Na faixa fica só o chip mínimo e cliques diretos no trilho. Pausa é **comando no log**. **Substitui** o painel de runas "acima da barra, sem pausa" da TD.18. | §14, §11 |
| 6 | **Torres ilimitadas** (sem teto por fase; só teto técnico de segurança), **custo exorbitantemente crescente** por torre do mesmo tipo, calibrado para média de **12 a 16 torres por partida**. **Substitui** o "+15%" da decisão (d) da TD.18. | §5.1 |
| 7 | Extras pós-MVP (páginas salvas, medalha "Especialista", desafio "Sem runas") continuam **pós-MVP**. | §13 |

## 3. Referências de mercado (o que copiar)

> Fonte: conhecimento prévio do autor da ficha, **não reverificado na web**. Direção de design, não dado citável.

| Jogo | O que vale copiar | O que evitar |
|---|---|---|
| **Bloons TD 6, Monkey Knowledge** | Árvore **global** com um ramo por classe de torre; bônus **pequenos e numéricos**. | Poder permanente empilhado que trivializa fases antigas (por isso a página é por partida). |
| **Kingdom Rush** | Melhorias **por tipo de torre**, **reembolso livre** (re-spec sem punição). | Camadas de nível em cada torre (é o que estamos removendo). |
| **Rogue Tower** | Escolhas **por partida** que redefinem a rodada; sinergia entre tipos. | Aleatoriedade que quebra o determinismo. |
| **Vampire Survivors** | Ícone + 1 número por upgrade; compra **instantânea, 1 toque**. | Lista plana sem estrutura. |
| **LoL, páginas de runas** | **Keystone** que muda o estilo; **páginas salvas** (pós-MVP). | Tela de leitura densa. |
| **Path of Exile / Ragnarok (imagem do usuário)** | **Adjacência obrigatória**; **notáveis** nos cruzamentos; keystones com contrapartida; pan/zoom; destacar o caminho até um alvo. | Grafo gigante (PoE ~1.300 nós; aqui **61**). |
| **Jogos de pausa tática (ex.: Bastion/XCOM-like menus, TD "pause and build")** | Menus de construção que **pausam** dão decisão calma sem perder o ritmo. | Menu que não pausa e pune quem lê. |

## 4. Estrutura da página: o grafo radial

### 4.1 Forma

Nó central **"Núcleo"** (dourado, já possuído, não custa) com **6 setores** em padrão hexagonal, um por tipo de torre + um setor **Geral**, em anel (a ordem define quem tem sinergia com quem: Raio, Gelo, Canhão, Balanceador, Scale Set, Geral, de volta ao Raio):

```
                 Raio
        Geral  .--o--.  Gelo
              /  / \  \
             o--o  ◉  o--o        ◉ = Núcleo (dourado)
              \  \ /  /           o = nó pequeno   O = notável   ★ = keystone
      Scale Set '--o--'  Canhão   ◇ = notável de fronteira (sinergia)
               Balanceador
```

Cada setor tem **9 nós** (6 pequenos, 2 notáveis, 1 keystone); entre setores vizinhos há **1 notável de fronteira** (sinergia). Total: 6 × 9 + 6 + 1 (Núcleo) = **61 nós**. (A TD.18 dizia "10 nós por setor / 67", mas a tabela e os custos já descreviam 9; **corrigido para 61**.)

Estrutura de um setor (do Núcleo para fora; as arestas formam árvore com 2 ramos curtos, para existir escolha de caminho):

```
Núcleo ─ E ─ S1 ─ S2 ─ N1 ─ S3 ─ S4 ─ N2 ─ S5 ─ ★K
             └────────────── ◇ (liga ao setor vizinho a partir de N1 e N2)
```

O desenho final (2 ramos paralelos por setor) fica para a ficha TD.19. **Contrato:** grafo **conexo**, 6 simetrias, cada setor alcançável pelo Núcleo ou por um ◇ de fronteira.

### 4.2 Regras de compra (decisão 3: liberdade)

- Só se compra nó **adjacente a um já possuído** (o Núcleo conta). Um ◇ é comprável se **qualquer** dos dois lados vizinhos for possuído. Essa é a **única** restrição de caminho: não há ordem obrigatória, nem setor travado por fase, nem "recomendação forçada". O destaque do caminho até um alvo (`PathTo`) é **opcional** e só informa.
- **Custo em créditos** (fixo, não cresce com a compra: **sem arbitragem**): nó pequeno **40** · notável **120** · notável de fronteira **120** · keystone **200**. Setor completo = 6×40 + 2×120 + 200 = **680**; página inteira = 6×680 + 6×120 = **4.800** (ninguém a completa numa partida, de propósito; ver §6).
- Estados visuais: **possuído** (aceso, prata), **comprável agora** (adjacente e com créditos: anel destacado), **sem créditos** (adjacente, faltam créditos: anel fraco com o custo), **bloqueado** (não adjacente: apagado). Linhas entre possuídos ficam acesas.
- Efeitos **somam** dentro do mesmo tipo e são **ints em pontos-base** (sem `double` no estado; mesma regra de arredondamento do dano atual).

### 4.3 Os nós (ícone + nome + ≤ 6 palavras)

O **nó pequeno** mostra só o ícone do atributo e o número (ex.: "⚡ +6%"); o nome só aparece no tooltip. Notável e keystone mostram **nome + efeito**.

**Pequenos (40 créditos), por setor:**

| Setor | Atributos dos nós pequenos |
|---|---|
| **Raio** (alvo único) | Dano +6% · Cadência +5% · Alcance +6% |
| **Gelo** (controle) | Lentidão +5% · Alcance +6% · Cadência +5% · Dano +10% (dano base muito baixo) |
| **Canhão** (área, só solo) | Dano +6% · Área +8% · Cadência +5% · Alcance +6% |
| **Balanceador** (retém) | Duração do nó +10% · Dano do nó +10% · Recarga do nó −10% · Alcance +6% |
| **Scale Set** (suporte) | Bônus da aura +5% · Alcance +8% |
| **Geral** | Crédito por morte +4% · Bônus de onda +8% · Recarga de habilidade −5% · Venda +5 p.p. (70 → 75%, **teto 85%**, ver §5.2) · Integridade +1 (1 nó só) |

**Notáveis (120) e keystone (200):**

| Setor | Notável 1 | Notável 2 | ★ Keystone (com contrapartida) |
|---|---|---|---|
| **Raio** | **Perfurar** · Ignora 30% de armadura | **Rajada** · 4º tiro causa dano ×2 | **Foco total** · Dano +60%, cadência −25% |
| **Gelo** | **Congelar** · Paralisa 1 s a cada 5 s | **Corroer** · Alvo recebe +15% de dano | **Inverno** · Lentidão em área; alcance −20% |
| **Canhão** | **Estilhaço** · Área causa 80% do dano | **Penetrador** · +40% dano em blindado e chefe | **Saturação** · Deixa fogo por 3 s |
| **Balanceador** | **Pool** · +1 nó simultâneo | **Failover** · Nó explode ao fim | **Anycast** · Nós nascem na frente; retém +1 |
| **Scale Set** | **Autoscale** · Vizinhas: cadência +10% | **Réplica** · Vizinhas: dano +10% | **Elástico** · Escalar dura +4 s |
| **Geral** | **Cota folgada** · Crescimento do custo −20% | **Chamada ágil** · Chamar onda paga +25% | **Diversidade** · +5% dano por tipo distinto em campo |

(**Cota folgada** passa a reduzir o **expoente de crescimento** da §5.1 de 1,25 para 1,20 por torre: continua sendo o "alívio de vCPU". Os efeitos de Raio/Gelo/Canhão/Balanceador/Scale Set reaproveitam as chaves de `stats` dos ramos A/B e capstones atuais do `td-towers.json`: `armorPenetration`, `superHitEvery`, `paralyzeEverySeconds`, `vulnerableBonus`, `splashFactor`, `burnZoneSeconds`, `nodes`, `nodeHolds`, `explodeOnEnd`, `spawnAtFront`, `neighborCadenceBonus`, `neighborDamageBonus`. A TD.21 muda a **origem** dessas chaves de "nível/ramo da torre" para "runas do tipo".)

**Notáveis de fronteira ◇ (120), sinergias entre tipos:**

| Entre | Nome | Efeito (≤ 6 palavras) |
|---|---|---|
| Raio · Gelo | **Alvo marcado** | Raio +25% dano em lentos |
| Gelo · Canhão | **Estilhaçar** | Canhão +30% dano em lentos |
| Canhão · Balanceador | **Martelo e bigorna** | Canhão +40% dano em retidos |
| Balanceador · Scale Set | **Pool elástico** | Nós duram +20% com Scale Set |
| Scale Set · Geral | **Auto-orçamento** | Torres custam −5% |
| Geral · Raio | **Pico de uso** | Raio +10% dano sem vazamento |

### 4.4 Tensão estratégica (por que especializar)

- **Cada setor completo ≈ ×1,8 de força do tipo** por 680 créditos (valor inicial, §12). Com a renda por partida (§6) o jogador **completa 1 setor e meio**, não 6, **e ainda** compra torres: a moeda única cria o dilema "mais uma torre cara ou uma runa que reforça as que tenho".
- O custo crescente das torres (§5.1) torna a 6ª torre do mesmo tipo **mais cara que um setor inteiro de runas**: a pessoa é empurrada para **poucas torres bem runadas** ou **um pouco de cada**, e a escolha depende da fase (voador pede Raio/Gelo; blindado pede Canhão/Penetrador).
- Keystones têm contrapartida (Foco total, Inverno) e **Diversidade** puxa para o lado oposto: "especialista × generalista".
- Os ◇ obrigam a escolher uma direção (Raio → Gelo → Canhão…): sinergia custa caminho (só adjacência, nada além).

## 5. Economia unificada de créditos

### 5.1 Custo crescente exorbitante por torre (decisão 6)

**Calibração TD.22 (20 seeds por perfil/fase):** custo = `round(base × F[n] / 100)`, metade-para-o-par em inteiros. `n` conta as torres do tipo, inclusive SEM POSIÇÃO. Tabelas inteiras de 48 entradas no JSON, incluídas no ContentHash; nenhum `Math.Pow` em simulação.

Até `n = 3`, mantêm-se os fatores iniciais ×1,25 (normal) e ×1,20 (Cota folgada). A partir de `n = 4`, cada fator antigo recebe `1,10^(n−3)`, arredondado ao inteiro mais próximo: `F[n] = round(Fantigo[n] × 1,10^max(0,n−3))`. Assim a cauda cresce aproximadamente ×1,375/×1,32 por repetição; comprar muitos exemplares iguais fica exorbitante sem teto de design. O validador aceita tabelas configuráveis estritamente crescentes, início 100, 48 entradas, Low ≤ normal.

Fatores normais n = 0..12: **100 · 125 · 156 · 195 · 268 · 369 · 507 · 698 · 960 · 1320 · 1814 · 2495 · 3431**.

| Tipo / n já em campo | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Raio/Gelo | 90 | 112 | 140 | 176 | 241 | 332 | 456 | 628 | 864 | 1188 | 1633 |
| Canhão | 120 | 150 | 187 | 234 | 322 | 443 | 608 | 838 | 1152 | 1584 | 2177 |
| Balanceador | 100 | 125 | 156 | 195 | 268 | 369 | 507 | 698 | 960 | 1320 | 1814 |
| Scale Set | 150 | 188 | 234 | 292 | 402 | 554 | 760 | 1047 | 1440 | 1980 | 2721 |

O Médio constrói em média 13, 14, 14, 14, 15, 15 torres nas fases 1..6 (mediana global 14); Otimizado 13..16. São roteiros de jogador, sem teto interno de torres no bot. A decisão de gastar a sobra em runas ou guardá-la para score continua livre. Valores medidos, renda, resultados e limites dessa amostra em `specs/evidencias/TD.22.md`.

O Popover/aba **Construir** mostra o **próximo preço** e o contador ("⚡ ×3 · 176").

### 5.2 Venda e arbitragem (regra simples)

- **Vender devolve `min(preço efetivamente pago pela torre, floor(taxa × custo vigente(tipo,n−1)))`**. Taxa 70%, runas até 85%. O teto individual corrige a arbitragem de Cota folgada: comprar pela curva Low, devolver a runa e vender pela normal nunca rende mais que o desembolso. A devolução de runa continua livre, 100%, em qualquer momento. A venda reduz a contagem e o próximo preço.
- `TdTower.PurchasePrice` registra o preço já com todos os descontos e entra no StateHash, pois influencia comandos futuros. `SellValueAt` é o valor teórico da curva; `SellValue(id)` aplica o teto individual e é usado pelo comando e pela UI. Torres deixam de ser estritamente fungíveis quando o teto individual atua. Sem trocar modificadores/contagem, a recompra imediata ainda perde pelo menos 15%; ao trocar runas o piso é zero lucro, não necessariamente 15% de perda.
- **Compra/devolução de runa em loop:** preço fixo e reembolso de 100% tornam o loop neutro. Nenhuma trava de devolução ou taxa foi introduzida.
- O validador preserva a restrição conservadora `descontoMáximoDeCompra + tetoDeVenda < 100%`, mas ela sozinha não cobria a troca de curva. A garantia geral agora vem do teto pelo preço pago, verificado em varredura dos cinco tipos × 48 contagens × desconto de fronteira ligado/desligado × bônus de venda ligado/desligado.
- **Efeito que vira estado:** o bônus de uma runa vale **no instante da compra** e some **no instante da devolução** (modificadores recalculados; **temporizadores, recargas e tiros em curso não reiniciam**). Assim, "trocar de runa para zerar recarga" não existe.
- **Venda de todas as torres é permitida** (decisão 1): a defesa pode ficar só com runas e uma torre; a partida segue sendo derrota por vazamento, não por regra.
- A medalha "Sem vender" continua e passa a ter peso real.

### 5.3 Limite de torres (decisão 6)

- Sai o teto de design (`maxTowers` 6 a 8 do `td-stages.json`): **torres ilimitadas por fase**. O que limita é o **espaço** na faixa (snap de 44 DIP, RF-28.20) e o **custo exorbitante** da §5.1.
- **Teto técnico de segurança (performance, não design): 48 torres no total** (barra larga de 7.680 px / 44 DIP ≈ 174 slots, mas 48 já é > 3× a meta de 12 a 16 e fica abaixo do orçamento de simulação; a TD.20 valida com benchmark). Com o preço da §5.1, **n = 12 já custa ×14,5**, então ninguém chega perto: o teto só existe para impedir abuso de conteúdo/replay. Aparece como mensagem "Limite técnico" só ali.
- O desafio **Frugal** continua com seu teto próprio (`maxTowers` do desafio = 4).

## 6. Fontes de crédito (moeda única) e ritmo

Não há tokens. As fontes que antes davam tokens agora pagam **créditos**, no mesmo "relógio" de eventos do Core (sem relógio de parede, sem aleatório novo). Conversão inicial: **1 token ≈ 40 créditos** (o preço do nó pequeno). Valores iniciais, **não medidos**, a calibrar na TD.22:

| Fonte | Créditos | Regra |
|---|---|---|
| **Início da partida** | créditos iniciais da fase **+80** (totais TD.22: 300/380/380/420/460/640) | sobe `startingCredits`; selos "Créditos iniciais" somam (§7) |
| **Fechar onda** | `waveBonusBase` 20 → **60**, `waveBonusPerWave` 4 → **5** (onda 1 = 65; onda 10 = 110) | onda concluída (mortos ou vazados) |
| **Onda sem vazamento** | **+40** (+2 por onda) | nenhum inimigo chegou ao núcleo |
| **Caixa-bomba coletada** | **+40** (+2 por onda) | `CollectCrate` aceito (RF-28.19); explodir não dá. Hoje a caixa só reduz recarga (20 s): **mantém a redução e passa a pagar créditos** |
| **Pergunta-bônus V/F acertada** | `creditsBase` 15 → **40**, `creditsPerWave` 3 → **3** | `AnswerBonus` com acerto (RF-28.23), 1 por Preparo; erro = 0 |
| **Chefe derrotado** | **+120** | chefe morto (não vazado); o selo/estrela de chefe segue o SDD-28 |
| **Venda de torre** | 70 a 85% de `custo(n−1)`, limitado ao preço pago | §5.2 |
| **Devolução de runa** | **100%** do preço | §8 |
| **Abate de inimigo** | como hoje (`td-enemies.json`); runa Geral **+4%** por nó | inalterado |

Ritmo esperado (fase 1, 10 ondas), **só as fontes novas/alteradas** além do que já existe: +80 (início) + 10 ondas de bônus maior (~ +400 sobre o atual) + ~8 sem vazamento (~ +400) + ~2 caixas (~ +90) + ~4 perguntas (~ +100) + chefe (+120) ≈ **+1.200 créditos** sobre a renda de hoje, que é o equivalente ao que antes eram ~26 tokens (≈ 1.040). O resultado esperado é o **orçamento-alvo** de ≈ 2.900 a 3.100 da §5.1. **O recibo aparece ao fechar a onda** ("+110 ¢ · sem vazamento +50 ¢"), no chip, sem texto longo.

Créditos **não têm juros** e **sobra de crédito** continua na pontuação do RF-28.21 ("créditos sobrando"); com a moeda única isso passa a ser **uma decisão**: gastar tudo em runas (fortalece) ou guardar (pontua). Derrota não dá nada: a partida acabou, a página reseta (§7).

## 7. A página é por partida (decisão 4)

- **Por partida (reseta).** Cada partida é uma **decisão de build** ("nesta fase vou de Gelo + Canhão"); a página vive **dentro do estado da partida**, então o log do replay basta (compra/devolução de runa entram como comandos) e o determinismo fica limpo; nada de poder permanente que trivialize fases antigas.
- **Árvore de selos (meta permanente): fica como está**, com este ajuste **recomendado**: os 3 nós obsoletos `infrastructure.sell75`, `infrastructure.slots1`, `infrastructure.slots2` viram **"Créditos iniciais I, II, III"** (+40 créditos iniciais cada, até +120), mantendo o **custo em selos** que já tinham, para o perfil v2 migrar sem perda (§15). Selos = **ponto de partida em créditos**; runas = **estratégia da partida**.
- **Alternativa descartada:** transformar a árvore de selos inteira em créditos iniciais (a árvore desapareceria). Perde a progressão visível e o valor das estrelas.
- **Sem persistência de runa no perfil.** O perfil não guarda página.

## 8. Comprar e devolver: a qualquer momento, 100% (decisões 1 e 2)

- **Comprar e devolver runa e torre** vale em **Preparo, Onda e modal aberta** (a modal pausa, §14). **Sem taxa** na devolução de runa (100%, ver a análise de exploit na §5.2). **Reavaliação:** a TD.18 bloqueava o reembolso durante a onda e só de nó folha por medo de "trocar de build a cada inimigo"; com a modal central **pausando**, trocar de build durante a onda custa **atenção e tempo de parede**, não vantagem de simulação, então **não é preciso taxa nem restrição**. O usuário quer liberdade total, e a conta fecha: runa devolvida = créditos de volta, nada some.
- **Devolver um nó no meio do grafo** (não folha): o Core **devolve em cascata** todos os nós que deixariam de estar conectados ao Núcleo (100% de cada um), e a UI **avisa antes** ("Devolver 4 nós · +360 ¢"). Assim o grafo continua conexo sem proibir nada.
- **Zerar tudo** ("↺ Zerar"): devolve a página inteira de uma vez.
- Comandos: `BuyRune(RuneId)`, `RefundRune(RuneId)` (§11).
- **Compra sem créditos suficientes** é rejeitada (`NotEnoughCredits`, o mesmo motivo das torres).

## 9. Convivência com a árvore de selos e conteúdo existente

| Conteúdo atual | Destino |
|---|---|
| `td-towers.json` `levels[]`, `branches[]` (A/B), `capstone` | **Removidos.** Ficam só stats base e `kind`/`targets`. Os valores de `stats` dos ramos migram para `td-runes.json`. |
| `td-towers.json` `cost` | **Fica** como `base` de `custo(tipo, n)`. |
| `td-stages.json` `maxTowers` (6 a 8) | **Removido das fases** (nulo); só o desafio Frugal mantém teto. |
| `rules.sellRefund` 0.7 | **Fica**, aplicado a `custo(tipo, n−1)` (§5.2). |
| `rules.waveBonusBase/PerWave`, `bonusQuestion`, `startingCredits` | **Sobem** (§6). |
| `td-meta.json` nós `infrastructure.sell75`, `slots1`, `slots2` | **Viram "Créditos iniciais I a III"** (§7). |
| `td-meta.json` nós de Defesa/Operação (dano, alcance, integridade, recarga, snapshot, auto-início) | **Ficam.** "Dano +3% global" e "Alcance +5%" **somam** com as runas (cuidado no balanceamento, R-29.6). |
| `td-abilities.json` | Inalterado. Runas Gerais mexem só na recarga. |
| `td-questions.json` / `AnswerBonus` | Inalterado; o acerto paga créditos maiores (§6). |
| Selos de coroa/medalha/estrela | Inalterados (§10). |

## 10. Impacto em coroa, medalhas, pontuação e patente

- **Coroa e medalhas** (RF-28.21): inalteradas em regra. "Sem vender" ganha sentido. Medalha "Especialista" e desafio "Sem runas": **pós-MVP** (decisão 7).
- **Pontuação:** a fórmula do RF-28.21 **não muda** (integridade, tempo, chamadas, créditos sobrando); com moeda única, "créditos sobrando" vira trade-off real (§6).
- **Patente:** os limiares (5.000 · 12.000 · 20.000 · 25.000 · 31.000) foram calibrados **antes** das runas; a **TD.22 recalibrou para 5.000 / 12.000 / 22.000 / 29.000 / 34.000**: Otimizado = MVP, Médio = Pleno/Sênior, SemContra = Estagiário (CA-28.17).
- **Pausa e tempo:** o tempo de pausa **não conta** no tempo da partida (a simulação não avança, §11); a pontuação por tempo não é afetada.
- **Perfil v2 → v3:** §15.

## 11. Determinismo e comandos

Tudo continua determinístico e no log do replay.

- **Comandos novos:** `BuyRune(string RuneId)`, `RefundRune(string RuneId)`, `Pause`, `Resume`. Rejeições novas (`CommandReason`): `RuneNotAdjacent`, `RuneOwned`, `RuneNotOwned`, `UnknownRune`, `NotEnoughCredits` (existente, reaproveitado), `AlreadyPaused`, `NotPaused`, `PausedCommand` (comando de tempo recusado em pausa).
- **Comandos removidos:** `UpgradeTower`, `ChooseBranch`. Um log antigo que os contenha é **rejeitado** na leitura (o `ContentHash` muda; não há replay legado a manter).
- **Pausa no Core (decisão 5):** `Paused` (bool) **entra no `StateHash`**. Com `Paused = true`: o `Tick` **não avança** nada (inimigos, projéteis, temporizadores, recargas, **caixa-bomba congelada**, contagem de início automático e tempo da partida); só o relógio de parede da UI corre. **Comandos aceitos em pausa:** `BuildTower`, `SellTower`, `SetTargeting`, `BuyRune`, `RefundRune`, `AnswerBonus` (resultado da pergunta, §14.4), `Resume` (a modal é justamente para decidir em calma). **Recusados em pausa** (`PausedCommand`): `StartWave`, `CallNext`, `UseAbility`, `CollectCrate`, `SetSpeed`/qualquer comando de tempo. **"Nada de comando de tempo enquanto aberta."** (A pergunta-bônus V/F abre numa modal própria que **pausa**; o cronômetro de 10 s dela é de parede, §14.4.)
- **Ordem no log:** como vários comandos podem ocorrer no **mesmo tick** (a simulação parada), o log passa a ser `(tick, seq, comando)` com `seq` crescente por tick. O replay aplica na ordem e reproduz o `StateHash`.
- **Estado novo no hash:** `Paused`, `OwnedRunes` (bitset por ordem do JSON), contadores de torres por tipo (já derivados). Modificadores por tipo são **derivados** na compra/devolução (cache fora do hash).
- **Créditos** são creditados por eventos do Core (fim de onda, `CollectCrate`, `AnswerBonus`, chefe morto): sem relógio de parede, sem aleatório.
- **Goldens:** o `StateHash` **muda** (estado novo e regras de custo e de renda); os goldens TD **são regenerados** (justificado, uma vez, na TD.21). Goldens **idle intactos**.

## 12. Balanceamento e o `TdSanityBot`

O poder que vinha de "nível 3 = ×2,2 de dano" passa a vir de **mais torres** (curva calibrada na §5.1) e **runas** (~680 a 1.020 créditos por partida). Ações da TD.22:

- **Roteiros do bot (DSL `TdBotPlans`)** ganham passos `rune <id>`, `runeSector <setor> <n>` e `sell <tipo>`; a ordem de compra usa `TdRuneGraph.PathTo(alvo, possuídos)` (Core, também usado pela UI para destacar caminho).
- **Perfis:** **Novato** compra o setor mais barato adjacente; **Médio** especializa **1 setor** pelo contra da fase; **Otimizado** vai ao keystone/notáveis do setor ideal e a 1 ◇ de sinergia, e **vende** torres fracas para pagar runas quando faz sentido; **SemContra** **não compra** setor do contra.
- **Metas TD.22 (20 seeds):** Otimizado 100% e 3★ em todas as fases; Médio ≥ 85% e ★ médio ≥ 2 em 1..5; Novato ≤ 100% mas com perda de integridade; SemContra 0%. Fase 6: Otimizado 100% e Novato 0%. **Novo:** mediana do Médio em **12 a 16 torres** e **nenhuma** estratégia dominante "vender tudo e só runas do Raio" que vença a fase 6 sem risco (se existir, é falha de balanceamento, não de regra: ajustar custo do keystone e a curva).
- **Pontos a recalibrar:** `base × F[n]`, créditos iniciais e bônus de onda (§6), HP dos inimigos, ganho por setor (×1,8), soma com os selos "Dano +3%/Alcance +5%".
- **Evidência:** `specs/evidencias/TD.22.md` com tabela antes/depois como a da TD.10.

## 13. Decisões pendentes (tabela final)

**Fechadas (usuário, 2026-11-17):**

| ID | Pergunta | Decisão |
|---|---|---|
| **DP-29.1** | Página por partida ou persistente? | **Por partida** (decisão 4); selos ficam como estão, 3 nós viram "Créditos iniciais" (§7) |
| **DP-29.3** | Reembolso: regras? | **A qualquer momento, 100%**, em cascata para manter conexão (decisões 1 e 2; §8) |
| **DP-29.4** | Comprar runa durante a Onda? | **Sim**, e devolver também (decisão 2) |
| **DP-29.6** | Fontes e valores de tokens | **Sem tokens**: fontes viram créditos (decisão 1; §6) |
| **DP-29.7** | Teto de torres e `maxTowers` | **Ilimitado por fase**, teto técnico **48**, só o Frugal limita (decisão 6; §5.3) |
| **DP-29.8** | Medalha "Especialista" e desafio "Sem runas" | **Pós-MVP** (decisão 7) |
| **DP-29.9** | Crescimento de custo | Tabela inteira com cauda acelerada na TD.22; **Cota folgada** usa Low (§5.1) |
| **DP-29.10** | Presets/"páginas salvas" | **Pós-MVP** (decisão 7) |

**Fechadas por consequência (recomendação adotada, sem ação do usuário):**

| ID | Pergunta | Resolução |
|---|---|---|
| **DP-29.2** | Tamanho/forma do grafo e custos | 61 nós, custos 40/120/120/200 créditos (§4); só a calibração numérica fica com a TD.22 |
| **DP-29.5** | Regra de venda | `min(preço pago, taxa × custo(tipo,n−1))`, sem lucro (§5.2, TD.22) (§5.2) |

**Abertas:** **nenhuma decisão de produto.** Ficam só os itens de **medição** (não são DPs): renda real por fase (§5.1/§6), expoente 1,25 e base de cada torre (TD.22), valores de `Créditos iniciais` e comportamento exato da modal ao ocultar a faixa por tela cheia (§14.7, verificado pela TD.19b).

## 14. UI: modal central que pausa (decisão 5)

### 14.1 Regra geral

**Tudo que o usuário abre é uma modal no centro da tela, e ela pausa o jogo.** Na faixa de 64 DIP sobra **só o chip/mínimo** (núcleo, onda, créditos) e os **cliques diretos no trilho** (coletar caixa-bomba, tocar uma torre para abrir o inspetor, tocar um slot vazio para abrir "Construir já nesse slot"). A faixa **não tem popover nem painel** além disso.

Modais (todas a mesma janela, mesmas regras de pausa, tamanho e foco):

| Modal | Como abre | Observação |
|---|---|---|
| **Painel com abas** | Clicar no **chip** | Abas: **Construir · Runas · Jogo · Como jogar** (atalhos 1 a 4) |
| **Inspetor de torre** | Clicar numa torre no trilho | Mira e **Vender (+valor)**; o contador de torres do tipo; sem "Melhorar" |
| **Pergunta-bônus V/F** | Evento do Preparo | Pausa a simulação; é a única com cronômetro próprio, de parede (§14.4) |
| **Cartão de vitória/derrota** | Fim da partida | Pausa implícita (a partida acabou); "Jogar de novo" / "Fechar" |
| **Como jogar** | Aba ou 1ª vez (TD.17) | Aba do painel; no 1º uso abre sozinho no tutorial |
| **Configurações do jogo** | Aba **Jogo** | Tamanho da faixa (TD.14), modo de início automático, volume, etc. |

### 14.2 Pausa (comandos no log)

- **Abrir** a modal envia `Pause` ao Core (tick atual); **fechar** (X, Esc ou clique fora) envia `Resume`. Se a modal já estava aberta e o usuário abre outra (ex.: do inspetor para "Runas"), **não** há `Resume`/`Pause` extra: a pausa é **uma só**, com contador de modais abertas no `UiController` (a pausa do Core é booleana; só o último fechar envia `Resume`).
- **Em pausa:** simulação congelada, **caixa-bomba congelada**, **nada de comando de tempo** (`StartWave`, `CallNext`, `UseAbility`, `SetSpeed`); o botão ▶ Iniciar onda **fica desabilitado** dentro da modal e o chip mostra "⏸". Comprar/vender/mirar/runas **funcionam**.
- **Colocar torre (modo posicionar):** escolher o tipo em "Construir" **fecha a modal visualmente, mantém a pausa** e arma o modo posicionar na faixa; **colocar (clique no trilho) ou Esc** encerram o modo e enviam `Resume`. Evita tempo correndo durante a colocação sem inventar um estado novo no Core (a pausa continua sendo o mesmo `Paused`).
- **Determinismo:** a UI só envia comandos; um log `Pause … comandos … Resume` reproduz o mesmo `StateHash`. Cliques fora da modal enquanto aberta **não** viram comandos de jogo (só fecham a modal).

### 14.3 Tamanho, posição e multimonitor

- **Tamanho:** **720 × 480 DIP**, escalado pelo **DPI do monitor da barra** (SDD-05: 100% = 720 × 480 px; 150% = 1.080 × 720 px). Limita-se a **90% da área de trabalho** do monitor (mínimo viável 560 × 380 DIP; abaixo disso as abas mostram um layout compacto de 1 coluna). **Não** é afetado por `TdBand.HudScale` (a modal é maior que o HUD antigo; sua escala é a do DPI).
- **Posição:** **centro da área de trabalho do monitor onde está a barra** (o mesmo monitor da faixa, não o do mouse nem o primário). Se o usuário trocar a barra de monitor com a modal aberta, a modal **reposiciona** no novo.
- **Fundo (scrim):** uma camada semitransparente cobre **só o monitor da barra** para capturar o clique fora; ela **não** cobre outros monitores. A faixa continua visível (a modal fica centralizada, longe de uma barra de 64 DIP no rodapé).
- **Conteúdo da aba Runas:** o grafo de 61 nós (nó pequeno 18 DIP, notável 28, keystone 34) cabe **sem zoom** no corpo de ~660 × 380 DIP; **pan** (arrastar) e **zoom 0,6× a 1,6×** (roda); duplo clique centraliza no último nó comprado; ao abrir, centraliza no setor mais investido. O cabeçalho da modal mostra **créditos ¢** sempre visíveis.

### 14.4 A pergunta-bônus V/F

Único caso com **tempo próprio**: a pergunta (RF-28.23) tem cronômetro de 10 s **de parede**, não de simulação. A modal da pergunta **pausa** a simulação (como as demais) e roda o cronômetro com o relógio da UI; **responder, expirar ou X** envia `AnswerBonus`/`Skip` e depois `Resume`. O determinismo se mantém porque o **resultado** (acerto/erro/expirou) vira comando no log, não o tempo de parede. (Ver §6: acerto paga 40 + 3·onda créditos.)

### 14.5 Liberdade e pouca leitura

- A modal **não direciona**: nenhum "próximo passo recomendado" fixo, nenhuma aba travada. O tutorial (§16) é uma **dica ligada ao chip e a poucos controles**, sempre ignorável.
- Cada runa: ícone + número (nó pequeno), nome + ≤ 6 palavras (notável/keystone) em tooltip e fixo com zoom ≥ 1×; cores por setor (SDD-20). **1 clique = comprar** (nó comprável); **clique no nó aceso = devolver** (aviso de cascata se houver dependentes); clicar num nó bloqueado **destaca o caminho** mais curto (`PathTo`) e mostra o custo total em créditos.

### 14.6 Foco, teclado e janela

- A modal é uma janela própria, **topmost**, **ativável** (diferente da faixa *no-activate*): **toma o foco** ao abrir (necessário para teclado) e **devolve o foco** à janela que estava em primeiro plano ao fechar. **Esc** fecha; **Tab/setas** navegam nós/abas; **Enter** compra/confirma; **1 a 4** trocam de aba; **Ctrl+Z** não existe (devolver é um clique).
- Enquanto aberta, a modal **recebe os cliques** (região de captura SDD-04, só existe enquanto aberta); a faixa e as torres **não** respondem (o clique fora fecha, ver 14.1).

### 14.7 Ausência, esmaecer por foco e tela cheia

- **Esmaecer por foco (TD.14) e ausência por clique-fora (TD.16):** a modal **nunca esmaece sozinha**; enquanto está aberta, o **esmaecer da faixa fica suspenso** (faixa em opacidade normal) e o **clique fora** é tratado como "fechar a modal", **nunca** como "ausência" nem "ir para o app em segundo plano". O jogo já está pausado, então a **ausência** (que pausava/avisava quando o usuário saía) **não se aplica** durante a modal.
- **Tela cheia (SDD-06):** se um app em tela cheia assume, a faixa oculta; a modal **fecha** junto e a pausa segue a regra atual de ocultar (a TD.19b confirma o comportamento exato com o `TdGameHost`; **não** retomar sozinha em silêncio se havia uma pergunta ativa).
- **Cartão de vitória/derrota:** mesmo padrão (Esc = "Fechar"; não há "retomar", a partida acabou).

## 15. Migração de perfil e conteúdo

- **Perfil TD v2 → v3** (`sidewar.td-profile`): (1) nós `infrastructure.sell75`, `slots1`, `slots2` **renomeados** para "Créditos iniciais I a III" (**mesmo custo em selos**, sem perda; IDs migram por tabela); (2) IDs desconhecidos saneados; (3) **nada de runa vai ao perfil** (a página é por partida).
- **Conteúdo:** novo `td-runes.json` (setores, nós, arestas, efeitos, custos em créditos); `td-towers.json` perde `levels/branches`; `td-stages.json` ganha `towerCostTable` e `towerCostTableLow`, perde `maxTowers`; `td-meta.json` ajustado. Todos no `ContentHash`.
- Settings (`td.tutorialDone`, `td.tipsSeen`) **não mudam**; a modal acrescenta `td.modalLastTab` (aba da última abertura, opcional).

## 16. Tutorial e "Como jogar" (TD.17)

A TD.17 **não** cria passos de "melhorar torre/ramo A/B" nem de "painel acima da barra". **A TD.17 passa a ensinar "chip → abrir modal"** (decisão 5), com as regras do RF-28.22 (avança por ação real, "Pular tutorial" sempre visível, sem comando novo só pela dica):

- **Passo 1 (≤ 8 palavras):** balão "Toque no chip para construir" com **seta no chip**; ao clicar, abre a modal na aba **Construir** (pausada).
- **Passo 2 (na modal):** "Escolha uma torre e toque no trilho"; depois da colocação a modal retoma sozinha (14.2).
- **Passo condicional de runas (TD.25):** ao ter créditos para o primeiro nó: "Runas: reforce um tipo" apontando a aba **Runas**; abrindo: "Compre um nó aceso"; depois: "Foque num tipo de torre" (dica, nunca obrigatória).
- **Dica de 1ª vez:** o setor de cada tipo, uma linha curta ("⚡ Raio: foco em 1 alvo. Compre Dano.").
- **"Como jogar" (aba 4):** o cartão ganha uma linha de runas ("Runas reforçam todas as torres do tipo"), uma de custo crescente ("Cada torre igual fica mais cara") e uma de moeda única ("Vender devolve ¢ para runas").
- **Tutorial × pausa:** enquanto o tutorial mostra dica sobre a modal, a pausa **não** muda; fechar a modal sem completar o passo **volta** o passo (ex.: "Toque no chip").

## 17. Critérios de aceitação e testes

| CA | Critério |
|---|---|
| **CA-29.1** | Não existem `UpgradeTower`/`ChooseBranch`; `td-towers.json` sem `levels/branches`; nenhum botão "Melhorar" (T-29.1) |
| **CA-29.2** | Custo da n-ésima torre do tipo = `round(base×F[n]/100)` pela tabela inteira; vender devolve `min(preço pago, taxa × custo(n−1))` (taxa 70 a 85%) e reduz o próximo custo; trocar runas nunca gera lucro na venda (T-29.2, T-29.3) |
| **CA-29.3** | Sem `maxTowers` por fase; teto técnico 48 respeitado; Frugal respeita seu teto (T-29.4) |
| **CA-29.4** | `td-runes.json` válido: grafo **conexo**, cada nó alcançável do Núcleo, custos 40/120/120/200, **`descontoMáximoDeCompra + tetoDeVenda < 100%`** (T-29.5) |
| **CA-29.5** | Só compra adjacente a possuído; ◇ por qualquer lado; sem créditos rejeita; devolver a qualquer momento (inclusive em Onda) devolve 100% em cascata e mantém o grafo conexo (T-29.6, T-29.7) |
| **CA-29.6** | Fontes de crédito da §6; loop compra/devolução de runa é neutro (T-29.8) |
| **CA-29.7** | Runas de um tipo afetam **todas** as torres daquele tipo em campo, inclusive as construídas depois; sinergias ◇ condicionais; devolver remove o efeito sem reiniciar temporizadores (T-29.9) |
| **CA-29.8** | Replay com `BuyRune`/`RefundRune`/`Pause`/`Resume` reproduz o `StateHash`; em `Paused` o `Tick` não avança nada (inclusive caixa-bomba) e `StartWave`/`CallNext`/`UseAbility`/`SetSpeed` são recusados; goldens TD regenerados, idle intactos (T-29.10) |
| **CA-29.9** | Bot: Otimizado 100% em 1 a 5, Médio ≥ 85%, SemContra 0%, mediana de torres do Médio em 12 a 16; patente recalibrada (T-29.11) |
| **CA-29.10** | UI: chip abre modal central 720 × 480 DIP no monitor da barra e pausa; fechar (X, Esc, fora) retoma; 1 clique compra; pan/zoom; caminho até alvo; **sem texto > 6 palavras por runa** (inspeção de `Strings.cs` + T-29.12) |
| **CA-29.11** | Migração v2 → v3 sem perder selos (T-29.13) |
| **CA-29.12** | Modal: toma foco ao abrir e devolve ao fechar; Esc/Tab/Enter/1-4; esmaecer da faixa suspenso; clique fora não conta como ausência (T-29.14) |

Testes: **T-29.1** conteúdo sem `levels/branches`; **T-29.2** tabela de custo inteira; **T-29.3** venda/teto pelo preço pago/sem lucro; **T-29.4** teto técnico e Frugal; **T-29.5** validador do grafo e da regra anti-exploit; **T-29.6** adjacência e ◇; **T-29.7** devolução em cascata e a qualquer momento; **T-29.8** fontes de crédito e loop neutro; **T-29.9** efeito por tipo e sinergias; **T-29.10** replay/hash/pausa; **T-29.11** bot e patente; **T-29.12** UI (hit-test, pan/zoom, goldens `td-ui-runes`); **T-29.13** migração de perfil; **T-29.14** modal (foco, teclado, monitor, DPI, esmaecer, tutorial).

## 18. Fichas de implementação (TD.19+)

Ficha TD.19 anterior (conteúdo) fica; a **modal central e a pausa viram fichas próprias** (TD.19b e TD.24b). Removidas por obsolescência: "painel de runas acima da barra", "tokens", "reembolso só de folha em Preparo", "teto técnico 24" (virou 48) e "+15%" (virou ×1,25).

| Ficha | Descrição | Depende de | Ambiente |
|---|---|---|---|
| **TD.19** ✅ | Conteúdo: `td-runes.json` + modelos/validador (grafo conexo, custos em créditos, regra anti-exploit); remover `levels/branches` e `maxTowers` das fases; `towerCostTable`/`towerCostTableLow`; fontes de crédito da §6 | TD.18b aprovada | 🟢 |

**TD.19b concluída (2026-11-17):** pausa no Core entregue (`Pause`/`Resume`, `Paused` no hash só quando verdadeiro, recusa `PausedCommand`, `Step` congelado, log `(Frame, Seq)`); evidência em `specs/evidencias/TD.19b.md`.

**TD.19 concluída (2026-11-17):** conteúdo e validador entregues (`td-runes.json`, `towerCostTable`/`towerCostTableLow`); evidência em `specs/evidencias/TD.19.md`. Remoção de `levels/branches/maxTowers` e ligação das fontes de crédito §6 ficam para a TD.20/TD.21 (consumidores ainda usam o legado).

**TD.20 concluída (2026-11-17):** economia no Core entregue. Custo da n-ésima torre do tipo = `round(base × F[n] / 100)` pela `rules.towerCostTable` (**arredondamento metade-para-o-par, em inteiros**, o da tabela da §5.1: 90×1,25 = 112 e 90×1,95 = 176); venda = `floor(custo(tipo, n−1) × taxa)` com a taxa em pontos-base limitada por `sellRefundCapBp` (85%), fungível entre torres do tipo (piso: comprar e vender perde sempre ≥ 15%); `UpgradeTower`/`ChooseBranch` e `levels/branches/capstone/maxTowers` removidos do Core, do conteúdo, do Runtime e do Rendering; teto técnico `TdMatchState.TechnicalTowerLimit = 48` (conta as SEM POSIÇÃO; o espaço continua limitado pelo snap de 44 DIP). Torres no stat base (efeitos de ramos/capstones voltam como runas na TD.21). O bot ficou sem passos de melhoria e a curva está desequilibrada até a TD.22 (testes de balanceamento marcados `Skip: recalibra na TD.22`). Os modificadores de runa que mexem em custo/venda (`towerCostTableLow`, `sellRefundBp`) entram na TD.21. Evidência em `specs/evidencias/TD.20.md`.

**TD.21 concluída (2026-11-17):** `BuyRune`/`RefundRune` com créditos, adjacência bidirecional, cascata por conexão ao Núcleo e comandos aceitos em pausa/Onda; bitset das runas no hash; efeitos por tipo, notáveis/keystones, seis sinergias, custo/venda e fontes de crédito da §6 ligados. Temporizadores em curso preservados; replay e goldens TD atualizados, idle intactos. Evidência e ressalva matemática sobre Cota folgada × venda em `specs/evidencias/TD.21.md`. Bot/balanceamento continuam na TD.22; nenhuma UI implementada.

| **TD.19b** ✅ | **Pausa no Core:** `Paused` no hash, comandos `Pause`/`Resume`, recusa de comandos de tempo, log `(tick, seq)`, caixa-bomba e temporizadores congelados; testes de determinismo e replay | TD.19 | 🟢 |
| **TD.20** ✅ | Core economia: custo crescente por tipo (tabela inteira), venda (§5.2), remover `UpgradeTower`/`ChooseBranch`, teto técnico 48, espaço | TD.19 | 🟢 |
| **TD.21** ✅ | Core runas: `BuyRune`/`RefundRune` (créditos, cascata), modificadores por tipo, mover efeitos de ramos/capstones para runas, sinergias ◇, `StateHash`, replay; regenerar goldens TD | TD.19, TD.19b, TD.20 | 🟢 |
| **TD.22** ✅ | Bot e balanceamento: `rune`/`runeSector`/`sell`, `TdRuneGraph.PathTo`, recalibrar curva `F[n]`, créditos/renda e patente; evidência `TD.22.md` | TD.21 | 🟢 |
| **TD.23** ✅ | Perfil v2 → v3 e árvore de selos ("Créditos iniciais I a III") | TD.19, TD.21 | 🟢 |
| **TD.24a** ✅ | **Modal central + pausa na UI:** janela própria (720 × 480 DIP, DPI, monitor da barra, scrim, foco/Esc/teclado), chip mínimo na faixa, contador de modais, modo posicionar, inspetor de torre, pergunta-bônus, cartões de vitória/derrota, aba Jogo (config) e aba Como jogar migrados para a modal, esmaecer/ausência (14.7) | TD.19b, TD.14, TD.16 | 🟡 |
| **TD.24b** | **Aba Runas:** grafo radial (estados, pan/zoom, tooltips, caminho até alvo, devolução com aviso de cascata) e aba Construir com preço crescente | TD.21, TD.24a | 🟡 |
| **TD.25** | Tutorial/"Como jogar" e dicas: chip → abrir modal, runas (§16) (atualiza TD.17) | TD.17, TD.24a, TD.24b | 🟡 |
| **TD.26** | Documentação: revisar SDD-28 (RF-28.4/28.5/28.13/28.22, CA/T), ADR, `PLANO.md`, MTC | TD.19 a TD.25 | 🟢 |
| **TD.28a** ✅ | **Corrida contínua no Core:** `TdMatchOptions` (ondas contínuas, corrida persistente), `StageCleared`, infinito depois da fase 6, `TdSanityBot.RunContinuous`; ver §20 | TD.22, TD.24c | 🟢 |
| **TD.28b** ✅ | **Host da corrida contínua:** `TdMatchOptions.Default` no host (`td.continuousWaves`, padrão ligado; desligado = Classic), `StageCleared` → perfil por `StageId` + aviso discreto no cantinho, derrota = fim da corrida com "Reiniciar", chip "Fase N · Onda n/Total", Iniciar só no Preparo inicial, pergunta-bônus durante a onda, atalho global de minimizar (boss key), painel de torres fixo (`td.towerPanelPinned`), núcleo/portal ~1,5× a torre; ver §20 | TD.28a, TD.24d | 🟢 |
| **TD.28c** ✅ | **Foto das torres, faixa ao vivo, fps fixo, velocidade:** sprite real da torre nos cards (painel rápido, Construir, Torres); `td.bandHeightDip` aplica na hora; sem queda para 8 fps sem foco; `td.gameSpeed` 1x/2x/3x (botão no chip e aba Jogo; host faz N Steps por tick, replay igual); torres de volta a 28 e portal/núcleo 42 (janela da cena com folga de 14 DIP×k acima da faixa, alfa 0) | TD.28b | 🟢 |
| **TD.27** (pós-MVP) | Medalha "Especialista", desafio "Sem runas", páginas salvas | TD.24b | 🟡 |

Ordem: TD.19 → TD.19b → TD.20 → TD.21 → (TD.22 ‖ TD.23 ‖ TD.24a) → TD.24b → TD.25 → TD.26; TD.27 por último.

## 19. Riscos

| ID | Risco | Mitigação |
|---|---|---|
| R-29.1 | Barras muito largas dão espaço de sobra e "mais torres" ficam baratas | Curva com cauda acelerada (§5.1); teto técnico 48; varredura TD.22 até n=47 |
| R-29.2 | Grafo difícil de ler em 720 × 480 | Só ícones/números; hover para texto; caminho destacado; zoom |
| R-29.3 | "Vender tudo e só runas do Raio" vira estratégia dominante | Bot a mede (§12); keystones com contrapartida; ajustar custo do keystone e a curva |
| R-29.4 | Setor dominante (Raio ×1,8 resolve tudo) | Bot por perfil; Diversidade; recalibrar |
| R-29.5 | Reaproveitar efeitos de ramos no combate quebra determinismo | TD.21 migra só a **origem** dos `stats`; testes de hash e golden regenerado |
| R-29.6 | Selos "Dano +3%/Alcance +5%" somam com runas e inflam | Rever na TD.22 (ou cortar da árvore de selos) |
| R-29.7 | Modal que toma foco atrapalha quem joga ao lado do trabalho | Pausa evita perda; devolve o foco ao fechar; só abre por ação do usuário (chip, torre, evento do Preparo) |
| R-29.8 | Pergunta-bônus com tempo de parede "vazando" para o Core | Só o resultado entra no log (§14.4); modal pausa a simulação |
| R-29.9 | Pausa abusada para "pensar a onda inteira" sem custo | Pausa não dá comando de tempo nem muda a simulação; sem benefício além de tempo de parede; pontuação por tempo usa ticks de simulação |

**TD.22 concluída (2026-11-17):** bot com `rune`, `runeSector`, `sell` e `TdRuneGraph.PathTo`; cauda F[n] acelerada; orçamento inicial das fases 2/6 +40; fontes de evento da §6 mantidas após medição. HP, estrelas, selos e fórmula de score mantidos: a dificuldade foi recalibrada pelo orçamento e pelas decisões de compra. Patentes 5k/12k/22k/29k/34k. Regra antiarbitragem por preço pago, varredura e cinco goldens TD atualizados; idle intactos. Matriz de 480 partidas e comparação antes/depois em `specs/evidencias/TD.22.md`.

**TD.23 concluída (2026-11-17):** perfil v3, cadeia v1 → v2 → v3, saneamento dos nós desconhecidos e reembolso integral dos três nós obsoletos. Conforme o briefing de implementação, as compras antigas são removidas e os selos ficam disponíveis (total ganho preservado), em vez da conversão automática de IDs originalmente descrita no §15. Novos nós `infrastructure.startingCredits1/2/3`: “Créditos iniciais I/II/III”, custos 2/3/4 selos e +40 créditos cada. O Core aplica os efeitos `startingCredits` ao criar a partida, inclusive o “Orçamento inicial” existente (+10). Runas continuam exclusivamente por partida. Evidência e encaminhamento da UI para TD.24b em `specs/evidencias/TD.23.md`.

### TD.24c — retorno após fim de partida

Ao terminar, a modal oferece reinício e mapa; na vitória, oferece também a próxima fase desbloqueada. Fechar o resultado mantém o chip como caminho para reabri-lo. A aba Jogo permite reiniciar mediante confirmação Sim/Não. Uma nova partida usa a criação e seed já existentes, após fechar a modal e liberar sua pausa.

### TD.24d — nota de integração visual

O canvas de Runas apresenta apenas um estado “em breve”; não especifica uma teia. O HUD mapeia “Fim da fase” à ação existente de iniciar ou chamar a próxima onda, conforme o estado da partida. A ilustração de três faixas não altera o mapa jogável. O painel rápido de torres usa a faixa existente para posicionamento e não pausa a partida.

## 20. Corrida contínua (2026-11-17, TD.28a)

Visão do usuário: **jogo para jogar enquanto trabalha** — sensação de jogo infinito rodando ao fundo, discreto. O Core ganha duas regras de partida, agrupadas em `TdMatchOptions` (passadas a `TdMatchState.Create(..., options)` e a `TdReplayScript.Options`):

| Opção | Padrão | Efeito |
|---|---|---|
| `ContinuousWaves` | **ligado** | Delay 0: assim que a onda termina (nada vivo, nada por nascer), a próxima começa **no mesmo passo** (`WaveCompleted` e `WaveStarted` no mesmo `Step`). A fase nunca volta a Preparo entre ondas. Só o Preparo **inicial** (antes da onda 1 da corrida, para montar a defesa e dar `StartWave`) continua existindo. `CallNextWave` segue valendo. |
| `PersistentRun` | **ligado** | Vencer a fase **não** termina a partida: o estado não é recriado. Torres (ids, posições, recargas), runas, créditos, habilidades e relógio continuam; muda o conteúdo (fase seguinte). Depois da fase 6, o ciclo infinito. Só a **derrota** (`Phase = Derrota`) encerra a corrida inteira. `Victory` não é mais emitido. |
| `StageClearHeal` | 5 | Integridade do núcleo recuperada ao concluir cada fase, limitada ao máximo. Escolha: **+5** (25% de 20) — alivia, sem apagar o aperto; "cheia" tiraria a tensão de vazar. |

`TdMatchOptions.Classic` (ambos desligados) mantém exatamente o comportamento anterior (Preparo manual entre ondas, Vitória terminal). **`TdMatchState.Create` sem opções e `TdReplayScript` sem `Options` usam Classic**, então testes/logs/hashes anteriores e o host atual continuam iguais até a TD.28b trocar para `TdMatchOptions.Default`.

**Fim de fase (`StageCleared`).** Evento anexado ao enum (`A` = número absoluto da fase, `B` = pontuação); o resumo completo fica em `state.StageResults[^1]`: `TdStageSummary(StageNumber, StageId, Cycle, Stars, IntegrityLost, Crown, Medals, Score, CombatSeconds)`. Estrelas, coroa, medalhas e pontuação usam contadores **da fase** (perda de integridade desde o início da fase, vendas, chamadas antecipadas, torres de pico, habilidades usadas, segundos de combate); `Score` usa a mesma fórmula (`TdScoring`). O host registra recorde/coroa/medalhas por fase com `StageId`. Reiniciam por fase: `UsesPerStage` das habilidades, `_hasLeaked` (runas "sem vazar"), contadores de feitos. Continuam: recargas, créditos, runas, torres.

**Infinito.** `StageNumber` é absoluto (1…6, 7…); `StageId` é o conteúdo (1…6, volta a 1) e `Cycle` a volta (0 até concluir a fase 6). O HP dos inimigos comuns escala por `(1 + hpStageStep × (StageNumber − 1)) × 1,25^Cycle` (`TdMatchState.CycleHpGrowth = 1,25`, multiplicação iterativa, sem `Math.Pow`); torres/habilidades destravam por `StageNumber`, nunca re-travam. Chefes não escalam (como antes). Ondas/inimigos vêm do conteúdo da fase `StageId`, e o `WaveNumber`/`TotalWaves` do HUD são **da fase** (volta a 1 a cada fase).

**Pergunta-bônus.** Sem Preparo, `AnswerBonus` vale **durante a onda em andamento**, uma vez por onda iniciada (`BonusAnswered` volta a falso em cada `WaveStarted`); paga `BonusCreditsFor(WaveNumber)`. O host a mostra como card no cantinho. Em Classic continua só em Preparo. A caixa-bomba não mudou (já era só em Onda).

**Determinismo.** Fora de Classic o `StateHash` inclui as duas opções, `StageNumber` e `StagesCleared`; em Classic o hash é idêntico ao anterior (goldens TD **não** mudaram: old = new; idle intactos). Log/replay sem comandos novos.

**Layout.** O layout da barra é do host (`LayoutChanged`); o Core não o recria na troca de fase, então as torres continuam onde estão e só ficam SEM POSIÇÃO se o host mandar um layout em que a zona sumiu (regra existente, ADR-034).

**SanityBot da corrida (`TdSanityBot.RunContinuous`).** Uma única partida com `Default`, mesmo layout típico, a cada fase carrega o roteiro da fase de conteúdo (só construir/runas; mira e venda do roteiro de fase única são descartadas porque os ids de torre já mudaram), até derrota ou `maxStages`. Critérios (20 seeds, fase inicial 1, limite 40 fases; as 6 por perfil confirmam): **SemContra** perde na fase 1 (0 fases concluídas, ≈2 torres); **Novato** conclui a campanha acumulando torres e cai em ≈16–17 fases (derrota em 6/6); **Médio** cai em ≈33 fases (6/6); **Otimizado** nunca cai até 40 (limitado pelo teto técnico de 48 torres / compra de runas do roteiro). Os critérios por fase única da TD.22 (matriz 20 seeds) permanecem como estão: `Run` não mudou.

**O que o host faz na TD.28b:** criar com `TdMatchOptions.Default`; não esperar `Phase.Preparo` entre ondas nem `Victory` (ler `StageCleared` e `StageResults[^1]` para o card/recorde por fase, e seguir sem fechar a partida); gravar recorde/coroas/medalhas por `StageId` a cada `StageCleared`; tratar `Derrota` como fim da corrida; mostrar a pergunta-bônus como card durante a onda (usar `BonusAnswered`/`AnswerBonus`); o HUD "Onda n/Total" é da fase, e `StageNumber`/`Cycle` mostram o progresso da corrida; "Iniciar" só existe no Preparo inicial; mandar `LayoutChanged` como hoje. Persistir `TdReplayScript.Options` se gravar replays.

### TD.28b (host da corrida contínua, 2026-11-17)
Decisões do usuário: jogo para **jogar enquanto trabalha** (discreto, infinito, sem roubar foco, fácil de minimizar).
- **Opções:** `TdComposition` cria toda corrida com `TdMatchOptions.Default` (ondas contínuas, delay 0, corrida persistente, ciclo infinito depois da fase 6) quando `td.continuousWaves` (Configurações e aba Jogo, padrão **ligado**); desligado volta ao **Classic**. Vale a partir da **próxima corrida** (a atual não muda). `TdGameHost.BuildReplay`/`TdComposition.BuildReplay` persistem `Options` no `TdReplayScript`.
- **Perfil:** `TdProfileRecorder.OnEvents` grava, a cada `StageCleared`, score/coroa/medalhas/recorde/estrelas/desbloqueio por **`StageId`** (1 a 6; o ciclo reaproveita o registro). Derrota continua em `OnMatchEnded`.
- **Aviso de fase:** card pequeno no cantinho (janela no-activate, sem Pause), "Fase N" + `+coroa`/`+recorde`, some sozinho em **4 s** (`StageNoticeSeconds`); sem cartão bloqueante.
- **Derrota = fim da corrida:** card do cantinho com **Reiniciar**, que começa uma corrida nova na **fase em que a corrida começou** (`RunStartStage`: a sugerida ao abrir o App, ou a escolhida no Mapa); Mapa continua disponível.
- **Chip/HUD:** `Fase N` absoluta (`·C2`, `·C3`… depois da fase 6) e `Onda n/Total` da fase; o botão **Iniciar onda** só existe no Preparo inicial (HUD estreita pela direita, chip não pula). Pergunta-bônus V/F durante a onda no card do cantinho (1 por onda, não pausa).
- **Boss key:** atalho global **Ctrl+Alt+H** (= `hotkeys.ToggleVisibility`, configurável em Configurações > Atalhos, com a checagem de conflito existente) esconde faixa, chip, painel, cartões, dica e modal de uma vez. A partida **continua rodando** (a `AppStateMachine` segue visível), conta como **ausente**, o render fica parado e a pergunta-bônus espera. O mesmo atalho ou **"Mostrar SideWar"** da bandeja (e a 2ª instância) trazem de volta. **Não é lembrado**: o App sempre abre visível. Lógica em `TdBossKey`. Atalho no tooltip do chip e em Como jogar.
- **Painel de torres fixo:** `td.towerPanelPinned` (padrão **desligado**; Configurações e aba Jogo): o painel rápido (≡) fica aberto junto ao chip o tempo todo, sem registrar o ESC global em repouso; o ≡ não o fecha; abrir o Menu/Mapa o substitui e fechar o devolve; só fecha ao desligar ou ao minimizar pelo atalho.
- **Núcleo e portal:** 32 DIP (a faixa inteira, limite do espaço de desenho) contra torres de **21** (eram 28): ~1,5× (42 vs 64 DIP reais na faixa de 64; escala com 40/88). Simulação intocada.
- Fora do escopo/pendente: validação ao vivo (foco real, boss key sobre apps, z-order ao reaparecer).

### TD.24d (nota)
Pergunta-bônus e cartão de fim de fase são cartões no cantinho ancorados ao chip (janela sem ativação, sem Pause, sem modal); a modal central só abre por clique no chip/menu. Painel rápido de torres (botão ≡ do chip) não pausa. Portal e núcleo passam a 32 DIP de altura (torres 28), sem mudar a simulação.
