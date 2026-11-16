# SDD-28: Gameplay tower defense (redesenho: partida por ondas, torres na barra, sem idle e sem quiz)

**Status:** Aprovada pelo usuário em 2026-11-16 (DPs da §11 decididas; ADR-035 em `DECISOES.md`), sujeita ao gate TA.1 (DP-28.11) · proposta original da ficha TD-DESIGN, 2026-11-16 · **Etapa:** a definir (novo ciclo "TD", ver §12) · **Depende de:** SDD-10, SDD-11 (núcleo determinístico), SDD-21 (BuildZoneMap, UIA), SDD-22 (cena e hotspots), SDD-26/ADR-026 (layout como estado) · **Bloqueia:** fichas TD.1 a TD.12 (§12) · **Substitui (se aprovada):** SDD-27, SDD-25, SDD-26, SDD-13 e partes de SDD-11, 12, 22, 23, 24 (lista em §12.3)

> **Decisões da §11 tomadas pelo usuário em 2026-11-16 e registradas como ADR-035** em `DECISOES.md` (ficha TD.0). A ficha vale como especificação, com o gate TA.1 para a execução visual (DP-28.11). Nenhum número aqui é medido: todos são **valores iniciais ajustáveis** por JSON (RNF-12.1) e entram em playtest (rótulos "não medido", README §2.3).

## 1. Identificador e título

SDD-28: Redesenho do gameplay do SideWar como **tower defense de partida** (preparar, iniciar a onda, construir e melhorar durante a onda, chefes, habilidades, estrelas e progressão entre fases), jogado **somente dentro da barra de tarefas**, com tema Azure como ambientação (sem caráter de jogo de ensino).

## 2. Objetivo

Hoje o SideWar é um **idle**: as ondas se encadeiam sozinhas (SDD-27 RF-27.7), a progressão é "1,09ⁿ até o infinito" com prestígio, o jogador mexe pouco, e perguntas Azure são o motivo de existir dos chefes (SDD-26). No primeiro teste real (2026-11-16) isso se mostrou ocupando espaço demais (faixa de ~100 DIP acima da barra) e sem as decisões que tornam um tower defense bom. Esta ficha propõe o gameplay de um TD de qualidade de mercado, **adaptado a um mundo 1D de poucas dezenas de pixels** e a um jogo que roda ao lado do trabalho da pessoa.

Decisões do usuário (2026-11-16), tratadas como fixas aqui:

1. redesenho do gameplay, não só ajustes;
2. torres **somente dentro da barra de tarefas**, nas áreas livres entre ícones e bandeja, **sem faixa acima**;
3. a fase/onda começa **por ação do jogador** (botão "Iniciar onda"); auto-início é **opcional**;
4. construir, vender e melhorar torres **durante** as ondas;
5. o jogo **nunca esmaece sozinho** por padrão.

## 3. Contexto: o que torna um TD bom (pesquisa) e como adaptar

### 3.1 Fontes e o que foi (e não foi) verificado

| Jogo | Recepção (como encontrada) | Fonte |
|---|---|---|
| Kingdom Rush (série) | Frontiers e Origins: "Overwhelmingly Positive" (9/10) na Steam; Frontiers com 10.359 avaliações; o Kingdom Rush original tem Metascore 74 (Xbox One, 5 críticas). Críticas citam polimento, torres e heróis variados, e "balance issues" pontuais | [Metacritic (original)](https://legacy.prod.metacritic.com/game/xbox-one/kingdom-rush/critic-reviews), [VaporLens Frontiers](https://vaporlens.app/app/458710/kingdom_rush_frontiers_tower_defense/stats/details) |
| Bloons TD 6 | "Overwhelmingly Positive" na Steam (a contagem varia por fonte: de ~289 mil a ~390 mil avaliações; ~97% positivas, **dado instável**) | [Steam](https://store.steampowered.com/app/960090/), [VaporLens](https://vaporlens.app/app/960090/bloons_td_6/stats/details) |
| Defense Grid: The Awakening | Metascore 81 (PC) e 82 (Xbox 360); Steam ~96% positivas (~5,2 mil avaliações) | [Metacritic](https://legacy.prod.metacritic.com/game/pc/defense-grid-the-awakening/critic-reviews), [Steam](https://store.steampowered.com/app/18500/) |
| Thronefall | ~96% positivas, ~23 mil avaliações (agregadores; instável) | [Steam](https://store.steampowered.com/app/2239150), [VaporLens](https://vaporlens.app/app/2239150/thronefall/stats/details) |
| Rogue Tower | "Very Positive", ~83% de ~5,9 mil avaliações; mediana de ~10,7 h | [Discord/Steam listing](https://discord.com/game/rogue-tower-1124352532475564212) |
| Dungeon Warfare | "Overwhelmingly Positive" (VaporLens); queixas de picos de dificuldade, grind e apresentação | [VaporLens](https://vaporlens.app/app/355980/dungeon_warfare/stats/details) |

Lacunas honestas: não pesquisei **Mindustry** (fora do escopo útil: é de fábrica/logística); notas de App Store não foram coletadas; notas Steam variam por data e fonte e devem ser lidas como ordem de grandeza. Os mecanismos de design abaixo vêm de guias, wikis e de uma análise de level design, **não** de números de venda:

- Análise de level design do Kingdom Rush ([Game Developer](https://www.gamedeveloper.com/design/kingdom-rush---the-wonderful-campaign-level-design)): cada fase foca **um conceito**; fases de respiro entre picos; **chamar a onda antes é opcional e paga ouro e recarga de magia equivalentes ao tempo pulado** (recompensa risco); torres básicas com forças e fraquezas claras, e o nível 4 ganha poder **e** uma fraqueza própria; armadura é contra-atacada por magos/canhões perfurantes, rápidos por soldados que retêm; inimigos novos chegam **telegrafados**; os melhores contra-ataques e torres únicas ficam para depois; **críticas a stats ocultos** (esquiva não informada) como falha de legibilidade.
- Guias de Kingdom Rush ([Alliance/Frontiers/Origins](https://www.pocketgamer.com/kingdom-rush-frontiers/how-to-survive-in-kingdom-rush-frontiers-hints-tips-and-tricks/), [wiki](https://kingdomrushtd.fandom.com/wiki/Encyclopedia)): chamada antecipada dá ouro e reduz recargas (variável por título); **estrelas por inimigos que passaram**; Iron Challenge (1 vida, 1 onda, regras próprias) como rejogabilidade; heróis com 2 habilidades que evoluem; árvore de melhorias comprada com estrelas.
- Bloons TD 6 ([Bloons Wiki: Targeting](https://www.bloonswiki.com/Targeting_option), [TouchArcade](https://toucharcade.com/2018/06/15/bloons-td-6-review-the-game-where-everything-happens-so-much/)): prioridades **First / Last / Close / Strong** em toda torre, com desempate documentado; **3 caminhos de melhoria, só um até o nível 5, um segundo até o 3**; esse "orçamento de especialização" é o coração da profundidade.
- Defense Grid / Thronefall / Rogue Tower / Dungeon Warfare: **não coletei fontes primárias de mecânica**; o que se usa deles aqui é conhecimento geral e está marcado como **inferência** (decisões a cada onda, escolha rápida entre poucas opções, modificadores de partida roguelite, rejogabilidade por desafios). Nenhum requisito abaixo depende disso sozinho.

### 3.2 Padrões extraídos e adaptação ao espaço de uma barra de tarefas

| Padrão de mercado | Por que funciona | Adaptação ao SideWar (RF) |
|---|---|---|
| Início da onda pelo jogador + **chamada antecipada com bônus** | Dá controle de ritmo e um botão de risco/recompensa | Botão "Iniciar onda" e "Chamar próxima" (RF-28.2, RF-28.3) |
| Pré-visualização do que vem | Legibilidade e planejamento | Ícones de composição e traços da próxima onda no portal/HUD (RF-28.2) |
| Economia por onda (ouro por morte + bônus de onda) e **venda com perda** | Cada centavo é uma decisão; vender permite replanejar | Créditos por morte, bônus de onda, venda a 70% (RF-28.4) |
| Árvore de melhorias com ramos e um **capstone com fraqueza** | Profundidade e rejogabilidade | Níveis 1 a 3 lineares, ramo A/B no 4, capstone no 5 (RF-28.5) |
| Mira configurável (First/Last/Strong/Close) | Skill expression barata de implementar | Já existe no Core; entra na UI (RF-28.6) |
| Contra-escolha (blindado, voador, rápido, regenerador, chefe) | Obriga a **montar** defesa, não só empilhar | 8 tipos de inimigo, cada um com contra (RF-28.7) |
| Chefes com mecânica própria | Picos de tensão memoráveis | 3 chefes autorais (RF-28.8) |
| Habilidades ativas com recarga | Engajamento durante a onda sem microgerência constante | 3 habilidades (RF-28.9) |
| Estrelas, desafios e modo infinito | Rejogabilidade e metas de curto prazo | RF-28.10, RF-28.11 |
| Curva: conceito novo telegrafado, respiro, pico | Evita fadiga e picos injustos (a queixa do Dungeon Warfare) | Fases autorais com um conceito cada (RF-28.12) |

Restrições específicas do SideWar: (a) **uma dimensão** (sem caminhos que se bifurcam: a variedade vem de composição, mira, ramos, habilidades e modificadores, não de mapa); (b) **poucas dezenas de pixels de altura** (48 DIP) e legibilidade precisa de ícones e cores, não de barras de vida em tudo; (c) **o jogador está trabalhando**: sessões curtas, nada acontece sem ele (por isso o início manual resolve o problema da atenção), e o jogo deve ser seguro de ignorar e de pausar.

## 4. Escopo e exclusões

**Escopo:** loop de partida (Preparo → Onda → Resultado), ondas autorais, economia da partida, catálogo inicial de torres com ramos, inimigos e chefes, habilidades, estrelas e desafios, progressão meta entre partidas, o que fica e o que sai do idle atual, e as restrições técnicas (determinismo, render e z-order sobre a barra).

**Exclusões:** layout exato de pixels de cada sprite e tokens de cor (SDD-09/20, a rever em TD.9), formato final do save (SDD-14, a rever em TD.5), detalhes de UIA (SDD-21, mantidos), tradução fina de textos (`Strings.cs`). Esta ficha **não altera código nem outros SDDs**: a lista de fichas a revisar está em §12.

## 5. Requisitos funcionais

### RF-28.1: Loop de partida (máquina de estados `MatchPhase`)

```
Preparo ──StartWave──▶ Onda(n) ──todos mortos/vazados──▶ Preparo(n+1)
   ▲                      │  └─CallNextWave (opcional, com bônus)──▶ Onda(n+1) sobreposta
   └──────────────────────┴──integridade = 0──▶ Derrota ──(Tentar de novo | Sair)
Onda(última) concluída ──▶ Vitória ──▶ Resultado (estrelas, selos) ──▶ Mapa de fases
```

- **Preparo:** o relógio da simulação **não avança** (nenhum inimigo, nenhuma recarga de torre). O jogador constrói, melhora, vende, ajusta mira e vê a pré-visualização. Termina só por `StartWave` (ou pelo auto-início, RF-28.3).
- **Onda:** a simulação roda a 30 Hz (SDD-10). Construir, vender, melhorar, mudar mira e usar habilidades valem **durante** a onda (decisão 4); o único bloqueio é o limite de torres e o dinheiro.
- **Integridade:** o núcleo tem **20 pontos** (`economy.json`), **restaurados só entre fases** (não entre ondas). Vazamento tira o "dano ao núcleo" do inimigo (RF-28.7). Integridade 0 = **Derrota** da partida; não há "recuo de 5 ondas". O jogador pode tentar a fase de novo (créditos e torres reiniciam; selos e estrelas já ganhos ficam).
- **Pausa:** o jogador pode pausar a qualquer momento (hotkey e menu da bandeja). A simulação também **pausa sozinha** quando o jogo fica oculto (fullscreen, sessão bloqueada; SDD-06/07). **Não existe estado "Ausente"** nem multiplicador por presença.
- **Fora de partida:** a tela do Mapa de fases (RF-28.12) é a única que existe sem partida em andamento; sem partida, o overlay não desenha cena nenhuma (0 fps, 0 CPU).
- **Sem progresso passivo:** nada avança com o jogador ausente. Auto-início (RF-28.3) é o único modo de "jogar sozinho" e, por padrão, está desligado.

### RF-28.2: Iniciar onda e pré-visualização

- Comando `StartWave` (só em Preparo). Atalhos: botão "▶" no HUD (RF-28.14), hotkey global configurável (proposta **Ctrl+Alt+Espaço**) e item "Iniciar onda" no menu da bandeja.
- **Pré-visualização da próxima onda** (legibilidade; crítica do Kingdom Rush a stats ocultos): lista de ícones por tipo com quantidade (`×12`), **etiquetas de traço** (VOADOR, BLINDADO, REGENERA, CHEFE) e, em onda de chefe, o nome e a mecânica em uma linha. Aparece no popover do HUD e como ícones sobre o portal durante o Preparo. Nenhuma onda tem composição escondida (a variação aleatória é só de **temporização** dentro de ±10% do intervalo, com o `Rng` semeado).
- Tempo de Preparo é **ilimitado**. O HUD mostra "Onda 4/12".

### RF-28.3: Chamada antecipada e auto-início

- **Chamar próxima** (`CallNextWave`): só durante uma Onda cujos spawns já terminaram (`schedule` esgotado). Inicia a onda seguinte **sobreposta** à atual, sem esperar o Preparo. Não há limite de ondas sobrepostas além do limite de 44 inimigos em campo (SDD-12).
- **Bônus:** `bônus = ceil(ETA_s × (0,6 + 0,05 × w))` créditos, em que `ETA_s` é o tempo estimado (determinístico, `max((X − LeakX)/Speed)` sobre os vivos) que o jogador **economizou** ao não esperar o fim da onda atual. Teto proposto: `2 × bônusDeOnda` da onda chamada (DP-28.6). Também **reduz recargas de habilidades** em `ETA_s × 0,25` (inspiração: Kingdom Rush; decisão pendente se mantém, DP-28.6).
- **Auto-início (opcional, padrão desligado):** configuração `match.autoStart` (`off` | `countdown`). Com `countdown`, depois da onda concluída corre uma contagem (padrão 15 s, configurável de 5 a 60); o jogador pode iniciar antes e ganha `bônus = ceil(segundosRestantes × (0,6 + 0,05 × w))`. Em Derrota/Vitória o auto-início nunca reinicia a fase.
- Nada disso depende do relógio de parede: a contagem é em ticks de simulação (e **só corre com a simulação rodando**; pausada ou oculta, congela).

### RF-28.4: Economia da partida

- **Créditos** (`long`, sem carry fracionário: valores inteiros de tabela, sem multiplicadores de presença nem de Reservas). Fonte: morte de inimigo (`ouro` do tipo, RF-28.7), **bônus de onda** (`20 + 4 × w`, pago quando a onda termina), bônus de chamada antecipada (RF-28.3). Sem juros (DP-28.7).
- **Créditos iniciais** por fase: `rules` da fase (padrão 150; fases posteriores 220 a 300, ver RF-28.12).
- **Construir:** custo fixo por tipo (sem `1,5^n` por torre existente: o custo crescente por quantidade do idle punia expansão e não é padrão de TD). Tabela em RF-28.5.
- **Vender:** devolve **70%** do total investido (compra + melhorias) em qualquer momento, inclusive durante a onda (DP-28.7). Vender libera a posição na hora.
- **Limite de torres** por fase (`maxTowers`, padrão **8**, até 12 pelo meta, RF-28.13), no lugar do teto de 24 de ADR-034: o limite cria a decisão "o que fica na barra" e **normaliza a dificuldade entre monitores** (a barra de um usuário pode ter 12 posições livres e a de outro, 30).
- **Formatação:** `CreditFormatter` (SDD-27 RF-27.2) é reaproveitado como está (valores raramente passam de 10⁴; o sufixo K ainda pode aparecer).

### RF-28.5: Catálogo inicial de torres e árvore de melhoria

Estrutura comum (`towers.json` v2): níveis **1 → 2 → 3 lineares**; no **nível 4 o jogador escolhe o ramo A ou B** (sem voltar); o **nível 5 é o capstone** do ramo escolhido (inspiração: Kingdom Rush e a "especialização única" do BTD6). Cada ramo traz **um ganho e uma fraqueza declarada** (legibilidade: tudo no inspetor). Alcance em **unidades lógicas** (RF-28.15) e cadência em segundos.

Custos (créditos, múltiplos da base `C` por torre): nível 2 = `0,9·C`; 3 = `1,6·C`; 4 (ramo) = `2,4·C`; 5 = `3,6·C`. Total acumulado até o nível 5 ≈ `9,5·C` (a venda devolve 70%). Multiplicadores de dano por nível (somados sobre a base): ×1,0 / ×1,5 / ×2,2 / ramo / capstone.

| id | Nome na UI (tema Azure, só flavor) | Papel | C | dano₁ | cad. (s) | alcance | Alvos | Mira padrão |
|---|---|---|---|---|---|---|---|---|
| `bolt` | **Raio** (VM) | Alvo único, longo alcance, polivalente | 70 | 14 | 0,6 | 150 | solo e aéreo | First |
| `frost` | **Gelo** (WAF) | Controle: lentidão e marca | 90 | 5 | 0,9 | 100 | solo e aéreo | First |
| `cannon` | **Canhão** (DDoS Protection) | Área e dano pesado, só solo | 120 | 45 | 1,9 | 110 | só solo | Strong |
| `balancer` | **Balanceador** (Load Balancer) | Retém inimigos de solo com "nós" (bloqueadores) | 100 | 0 (os nós causam 4/s) | recarga de nó 6 s | 90 | só solo | First |
| `scaleset` | **Scale Set** (VM Scale Sets) | Suporte: bônus às torres vizinhas (não ataca) | 150 | n/a | n/a | 120 (aura) | n/a | n/a |

Ramos (valores iniciais; `★` = fraqueza declarada):

| Torre | Ramo A | Ramo B | Capstone A (nível 5) | Capstone B (nível 5) |
|---|---|---|---|---|
| `bolt` | **Atirador (GPU)**: dano ×2,6, cadência ×1,5 mais lenta; ignora 50% da armadura; ★ lento contra enxames | **Rajada (série B)**: cadência ×0,55; cada acerto seguido no mesmo alvo dá +8% de dano (até +64%); ★ dano baixo por tiro (ruim vs blindado) | "Desligamento": a cada 5º tiro causa 6× de dano | "Sobrecarga": o bônus de foco passa a durar entre alvos |
| `frost` | **Congelar (limite de taxa)**: a cada 4 s paralisa 1,2 s o alvo; ★ chefes imunes à paralisia | **Corrosão (inspeção profunda)**: reduz armadura do alvo em 6 e marca "vulnerável" (+25% de dano de todas as torres); ★ lentidão fraca (×0,85) | "Zero absoluto": paralisia em área | "Auditoria total": vulnerável vira +40% |
| `cannon` | **Barragem**: raio de área ×1,6 e dano em área 0,7; ★ dano alvo único menor | **Penetrador**: dano ×2,2 vs blindado e chefe, sem área; ★ ruim vs enxame | "Saturação": deixa zona de queimadura por 3 s | "Perfuração total": ignora 100% da armadura |
| `balancer` | **Pool amplo**: 3 nós simultâneos retendo 2 inimigos cada | **Failover**: nós explodem ao fim (área); ★ 1 nó por vez | "Anycast": nós nascem no ponto mais avançado do alcance | "Blue/green": nó novo imediato ao romper |
| `scaleset` | **Autoscale**: +25% de cadência nas torres vizinhas no alcance | **Réplica**: +25% de dano nas torres vizinhas e +1 alcance de bolt; ★ não dá cadência | "Elasticidade": bônus ×1,5 durante habilidade "Escalar" | "Capacidade reservada": +1 em `maxTowers` |

- O Scale Set **desbloqueia na fase 4** (RF-28.12). Os demais estão disponíveis desde a fase 1, exceto o `balancer` (fase 2).
- A **contra-escolha** de cada torre está na coluna "Alvos" e nas fraquezas dos ramos; o inspetor mostra em texto "bom contra / ruim contra".
- **Ordem do dano (reaproveita ADR-031):** dano(nível/ramo) → bônus de foco/super hit → vulnerável (marca) → multiplicadores de habilidade → menos armadura (após perfuração) → `max(1, floor(x))`. Tudo em `double`; potências por tabela iterativa (ADR-026); a PowTable continua, mas só para o crescimento de HP (RF-28.12), não para custos.

### RF-28.6: Mira configurável

- Modos expostos na UI: **Primeiro** (mais perto do núcleo), **Último** (mais longe do núcleo), **Mais forte** (maior HP; empate: o mais próximo do núcleo), **Mais fraco**, **Mais próximo da torre**. Padrão por torre na tabela de RF-28.5; trocável a qualquer momento, inclusive em pausa (`SetTargeting`, já existente).
- `Targeting.Pick` (SDD-11 RF-11.5, distância 1D) ganha o modo **Close** e respeita **voador/solo** (RF-28.7). O balanceador ignora mira (ele posiciona nós no inimigo mais avançado do alcance).

### RF-28.7: Inimigos e contra-escolha

HP base e velocidade em **unidades lógicas/s** (RF-28.15: o caminho tem 1.200 unidades; velocidade 40 = 30 s de travessia). `ouro` e `dano ao núcleo` (integridade perdida) são por tipo; HP cresce por fase e onda (RF-28.12).

| id | Nome (flavor) | HP base | Vel. | Armadura | Ouro | Dano ao núcleo | Traço e contra |
|---|---|---|---|---|---|---|---|
| `grunt` | Requisição | 40 | 40 | 0 | 6 | 1 | base de todo o jogo |
| `runner` | Ping | 22 | 78 | 0 | 6 | 1 | **rápido**: fura defesas de cadência lenta; contra: balanceador, gelo |
| `swarm` | Rajada | 14 | 46 | 0 | 3 | 1 | **enxame** (10 a 24 por onda); contra: canhão Barragem, nós |
| `armored` | Pacote blindado | 110 | 32 | 6 | 12 | 2 | **blindado**: pouco dano por tiro some; contra: Penetrador, Atirador, Corrosão |
| `tank` | Tráfego pesado | 380 | 24 | 2 | 20 | 3 | **durável**: testa DPS total |
| `flyer` | Bot aéreo | 60 | 55 | 0 | 10 | 1 | **voador**: ignora balanceador e canhão; contra: Raio, Gelo |
| `regen` | Verme | 120 | 36 | 0 | 14 | 2 | **regenera** 4%/s após 2 s sem dano; contra: burst, Atirador, Congelar |
| `leech` | Parasita | 90 | 38 | 0 | 14 | 2 | **drena**: ao chegar a 80 unidades de uma torre, **desativa-a por 3 s**; contra: matá-lo cedo, alcance longo (introduzido na fase 5) |

- Regras: `flyer` e `leech` são **marcações de traço** no JSON; não existe sprite por variante além do ícone de traço (legibilidade pequena: cor da borda por traço, ver RF-28.15 e SDD-20).
- **Resistências a controle:** chefes são imunes a paralisia e têm lentidão limitada a 20% (sem "piso" confuso como no idle).
- **Mistura por onda é autoral** (RF-28.12), não gerada por perfil `start/slope/cap`.

### RF-28.8: Chefes (a cada 5 ou 10 ondas, autorais)

Chefes aparecem na última onda de cada fase e, em fases longas, no meio. Mecânicas simples e **telegrafadas** (aviso no preview, RF-28.2):

| Chefe | Fase | Mecânica | Contra |
|---|---|---|---|
| **Ransomware** | 3 | HP alto; a cada 10 s "criptografa" a torre mais próxima por 4 s (como `leech`, mas em área limitada); recebe 30% menos dano de torres bloqueadas | tirar torres da área, usar balanceador, habilidades |
| **Botnet (Hidra DDoS)** | 5 | ao chegar a 66% e 33% do HP, **divide-se** em 6 `swarm` | canhão Barragem, nós |
| **Zero-day** | 6 | imune a lentidão e paralisia; **regenera** 2%/s entre dois golpes; fica **invulnerável por 3 s** ao entrar em cada nova metade do caminho | DPS sustentado, Penetrador, "Escalar" |

- Cada chefe tem **barra de vida** própria (única barra de vida desenhada por padrão, RF-28.15) e entrada anunciada no HUD ("CHEFE: Ransomware"). Sem perguntas (RF-28.17).

### RF-28.9: Habilidades ativas (3 iniciais, recarga em segundos de simulação)

| Habilidade | Efeito | Recarga | Desbloqueio |
|---|---|---|---|
| **Escalar** (autoscale) | +50% de cadência em todas as torres por 8 s | 60 s | fase 1 |
| **Firewall global** | lentidão de 50% (ou de 20% em chefe) em todos os inimigos por 6 s | 75 s | fase 3 |
| **Restaurar snapshot** | recupera 3 de integridade; **1 uso por fase** | única | meta (RF-28.13) |

- Atalhos: teclas 1, 2, 3 no HUD focado; botões no HUD. Só valem em Onda (em Preparo ficam desabilitadas). Comando `UseAbility(id)` no log (determinismo, RF-28.16).
- A habilidade "Escalar" **substitui o "pico de demanda" do SDD-26** (×3 de dano por 60 s e minijogo de clique): sem banner, sem janela de clique rápido, só um botão.

### RF-28.10: Estrelas e desafios

- **Estrelas por fase** (máximo, nunca diminui): ★1 = concluir; ★2 = concluir perdendo **no máximo 6** de integridade; ★3 = concluir **sem perder integridade** (ou, se DP-28.8 preferir, perdendo no máximo 2).
- **Desafio opcional por fase** (1 de 3 tipos, dado pela fase): **Frugal** (máx. 4 torres), **Purista** (sem habilidades), **Maratona** (sem chamar ondas antes e com créditos iniciais -30%). Concluir o desafio dá **1 selo extra** (RF-28.13) e a medalha "desafio" no Mapa. Conta separadamente das estrelas.

### RF-28.11: Rejogabilidade

- **Modo infinito "Escala infinita"** (desbloqueado ao concluir a fase 6): ondas geradas (perfil de mistura por fórmula, reaproveitando `IdleWaveBuilder` adaptado, RF-28.12), HP crescendo por `1,09^(w−1)`, **seed** da partida mostrada e gravada; meta = onda máxima por seed. Sem prestígio.
- **Desafio do dia (pós-MVP, DP-28.9):** seed derivada da data local; mesmo conteúdo para todos, ranking **local** (sem rede, ADR-014).
- **Políticas (roguelite leve, pós-MVP, DP-28.9):** no modo infinito, a cada 5 ondas, o jogador escolhe **1 de 3** modificadores (ex.: "+20% de dano, torres custam +15%"); inspirado em Rogue Tower (inferência, §3.1).

### RF-28.12: Fases, ondas autorais e curva de dificuldade

MVP: **6 fases** ("regiões" Azure como flavor: Brazil South, East US, West Europe, Japan East, UK South, Australia East). Fases com **12 a 15 ondas**, partidas de **8 a 15 min** de jogo ativo (DP-28.10). Cada fase introduz **um conceito** e tem um respiro:

| Fase | Conceito novo | Ondas | Créditos iniciais | `maxTowers` | Destaques |
|---|---|---|---|---|---|
| 1 | tutorial: construir, iniciar onda | 10 | 150 | 6 | só `grunt` e `runner`; torres: Raio, Gelo, Canhão |
| 2 | blindados e mira | 12 | 180 | 7 | `armored`; libera Balanceador |
| 3 | controle e habilidade | 12 | 200 | 8 | `swarm`, 1º chefe (Ransomware); libera Firewall global |
| 4 | voadores e suporte | 14 | 240 | 8 | `flyer`; libera Scale Set; fase de respiro (ondas curtas) |
| 5 | regeneração e drenagem | 14 | 260 | 8 | `regen`, `leech`; chefe Botnet |
| 6 | prova final | 15 | 300 | 8 | tudo misturado; chefe Zero-day |

- **HP:** `HP(w) = hpBase × 1,08^(w−1) × (1 + 0,10 × (fase−1))`, via PowTable (determinismo). Quantidade por onda: autoral (`waves.json` por fase), crescendo de ~6 a ~28 inimigos; **uma onda de respiro** a cada 4 a 5 ondas (menos inimigos, mais ouro de bônus).
- **Meta de tuning:** um jogador atento, sem otimizar, conclui as fases 1 a 3 com ★2 na 1ª tentativa; ★3 exige decisões (mira, ramo certo, venda e realocação). A fase 6 é o primeiro lugar em que **perder é esperado**. O `SanityBot` novo (TD.10) mede a curva (créditos × HP total por onda) e falha se uma onda exigir mais de 1,6× o DPS que o orçamento permite.
- O `IdleWaveBuilder` fica como **gerador do modo infinito** (RF-28.11), sem encadeamento automático.

### RF-28.13: Progressão meta (entre partidas)

- **Selos** (inteiro): 1 por estrela nova + 1 por desafio. Gastos numa **árvore de melhorias permanentes** modesta, em 3 ramos de 4 nós (custos 1 a 4 selos):
  - **Infraestrutura:** +10 créditos iniciais por nível; venda 70% → 75% → 80%; +1 `maxTowers` (até 12 no total, 2 nós).
  - **Defesa:** +3% de dano global por nó; +5% de alcance; +1 integridade (2 nós, até 22).
  - **Operação:** recarga das habilidades −10%/nó; desbloqueia "Restaurar snapshot"; auto-início com bônus +25%.
- Torres e ramos **não** são comprados com selos (decisão: o jogador nunca fica sem uma torre de contra), só desbloqueados por fase (RF-28.5). Ramos de nível 4 estão disponíveis assim que a torre aparece.
- **Substitui** Reservas, regiões "Migrar" e missões diárias: sem reinício voluntário (prestígio) e sem recompensa por tempo de presença (decisões de "idle"). Missões diárias, se o usuário quiser, voltam como **metas de partida** (DP-28.9).

### RF-28.14: HUD e controle (o mínimo para caber na barra)

- **HUD compacto dentro da barra** (no canto livre mais perto do núcleo, ou sobre a zona esquerda se houver, DP-28.1): `❤ 20 · 💰 240 · Onda 4/12 · ▶`; texto Silkscreen/JetBrains Mono (ADR-025), 11 a 12 DIP. Tudo o mais é um **popover temporário** (acima da barra, fechável com clique fora ou Esc) acionado por clique no HUD: inspetor de torre (alcance, mira, ramo, vender), construção (arrastar o ícone para uma posição livre ou clicar), pré-visualização da onda, habilidades, Mapa de fases e árvore de selos. O popover **não é gameplay**: nenhuma torre fica nele.
- **Alvos de clique:** torres e HUD via **janelas hotspot** (SDD-22 RF-22.8) pequenas; a cena de desenho é `WS_EX_TRANSPARENT` por padrão para **não bloquear ícones** da barra quando um inimigo passa por cima (ver RF-28.15 e §6). Sem estado "esmaecer": a opacidade é sempre 100% por padrão; existe opção em Configurações "opacidade em segundo plano" (0% a 70% de redução), **desligada** (decisão 5).
- **Bandeja** (ADR do ícone próprio, decisão do usuário 2026-11-16): menu Iniciar onda, Pausar/Retomar, Minimizar (ocultar cena), Configurações, Sair.
- Atalhos globais mínimos: Ctrl+Alt+Espaço (iniciar/chamar), Ctrl+Alt+P (pausar), Ctrl+Alt+G (mostrar/ocultar; já existe).

### RF-28.15: Mundo, render e legibilidade em ~48 DIP

- **Sem faixa acima da barra.** A janela de cena cobre **apenas a barra** (`SceneLayout`: altura = `HeightDip` da barra). Hospedeiro `Taskbar` obrigatório para jogar (DP-28.11 trata o fallback).
- **Onde andam os inimigos (proposta, DP-28.1):** um **trilho** de ~14 DIP no **topo da barra**, de uma ponta à outra (portal junto ao relógio/bandeja, núcleo na ponta esquerda), desenhado **sobre** os ícones (translúcido, sem bloquear clique). Torres ficam nas **zonas livres** (BuildZoneMap, SDD-21), sprite 30 a 34 DIP na metade inferior; linhas de alcance como **barra fina sobre o trilho** ao selecionar/pairar.
- **Mundo lógico fixo (proposta, DP-28.2):** o Core simula um caminho de **1.200 unidades** e **pads lógicos** (posições de torre) derivados das zonas livres; o render converte unidade → DIP por escala (`W_dip / 1200`). Assim monitor, DPI e quantidade de ícones **não mudam a dificuldade** (alcances e velocidades são em unidades). `LayoutChanged` passa a mudar só o **mapa pad lógico → posição física**, e a regra `SEM POSIÇÃO` (ADR-034) sobrevive para o caso em que as zonas encolhem. Se houver menos de 4 pads físicos, o hospedeiro cai em fallback (RF-28.18).
- **Posicionamento:** posição livre em X (ADR-034) com **snap** em passos de 44 DIP dentro da zona (DP-28.3), validado por `PlacementValidator` 1D (SDD-11 RF-11.9, mantido); o limite é `maxTowers` (RF-28.4), não o espaço.
- **Código visual de legibilidade:** (1) tipo de inimigo por **forma + cor de borda** (solo = cinza; voador = ciano; blindado = âmbar; regenera = verde; drena = magenta), nunca só por cor; (2) **sem barras de vida** exceto chefes e inimigos de elite danificados; (3) números de dano **só em acertos grandes** (≥ 25% do HP máx do alvo) para não poluir 14 DIP; (4) torre mostra o ramo por **ponto de cor**; (5) tooltip no passar do mouse sempre mostra a verdade (stats não ocultos).
- **Fps:** 30 fps durante a Onda; **0 fps em Preparo** (redesenho só em mudança; um frame por interação); 0 fps oculto/pausado. Orçamento herdado de SDD-16 (a rever, TD.7).

### RF-28.16: Determinismo e replay (ADR-026, mantido)

- Toda ação do jogador é **comando** no log: `StartWave`, `CallNextWave`, `BuildTower(type, x)`, `SellTower(id)` (**volta**, antes removido), `UpgradeTower(id)`, `ChooseBranch(id, A|B)`, `SetTargeting(id, mode)`, `UseAbility(id)`, `PlaceTower` e `LayoutChanged` (entre ticks). `StateHash` inclui integridade, créditos, torres (ramo, nível, cooldowns), inimigos (traços, regeneração, timers), habilidades (recargas), pads lógicos e o `Rng` (PCG32 semeado por **seed da partida**, não por perfil).
- Proibido no tick: `Math.Pow/Sin/Cos/Exp/Log`, `DateTime`, `Random` (SDD-11 RF-11.1). Regeneração, paralisia, nós e auras usam inteiros/`float` com operações permitidas.
- `ReplayRunner` e `SanityBot` são **reescritos** para o novo loop (TD.3, TD.10) e continuam rodando no Linux.

### RF-28.17: O que sai e o que vira flavor (anti-"jogo de ensino")

| Item atual | Destino |
|---|---|
| Cartão de pergunta do chefe, banco `questions.json`, ledger, estrelas por respostas (SDD-26, SDD-24 RF-24.9, SDD-27 RF-27.9) | **Cortar** do gameplay. O banco pode ser arquivado; **não** reaproveitar como quiz. |
| Temas "AZ-900" por fase (`stageTopic`), nomes de torre por serviço Azure | **Viram flavor**: um texto de uma linha no tooltip ("Raio — como uma Azure VM: capacidade bruta sob demanda"). Sem pergunta, sem pontuação por acerto, sem tela de explicação. |
| "Picos de demanda" por clique (SDD-26) | Substituído pela habilidade **Escalar** (RF-28.9) |
| Caixas de suprimentos por clique (SDD-26) | Cortar do MVP (DP-28.9: se voltar, vira drop opcional e sem pressa) |
| Cartão "Bem-vindo de volta", relatório offline, presença, auto-escala (SDD-25) | **Cortar** (não há progresso offline) |
| Missões diárias, Migrar, Reservas, regiões (SDD-27) | **Cortar**; substituídos por RF-28.10, 28.11, 28.13 |
| Perfil com `Reservas`, `ondaMax` por fase | Migração de save (DP-28.4) |

### RF-28.18: Z-order sobre a barra, UIA e fallback

- Toda a premissa visual (cena **dentro** da barra, sobre ícones) depende do **spike TA.1** (PLANO §ADR-034): janela layered topmost sobre `Shell_TrayWnd`, reaparecimento após clique em Iniciar/pesquisa/bandeja, cliques passantes. **Esta ficha não pode ser aprovada para execução antes do resultado de TA.1** (DP-28.11).
- **Se TA.1 reprovar:** o fallback permitido é **`Band` mínima** (faixa de **28 a 32 DIP** acima da barra, só para trilho e torres; **uma decisão do usuário**, pois contradiz "sem faixa acima"). Alternativa: o jogo desliga as torres na barra e exige `towersInBar = false` com faixa mínima. Isso não é proposto como padrão.
- Mantêm-se: UIA somente leitura, `BuildZoneMap`, reavaliação por `LayoutChanged`, `SEM POSIÇÃO`, `FullscreenWatcher` (com o conserto da referência WINEVENTPROC já em andamento em `WinEventHook.cs`) e `ZOrderCoordinator`.

## 6. Requisitos não funcionais

- **RNF-28.1 (determinismo):** mesma seed, conteúdo e comandos geram o mesmo `StateHash` entre execuções e SOs (Linux e Windows). Verificado por replay de partidas scriptadas das 6 fases.
- **RNF-28.2 (não incomodar o trabalho):** a cena **nunca rouba foco**, nunca bloqueia cliques em ícones da barra (a camada de desenho é transparente a cliques; só os hotspots de torre e HUD recebem), e **não esmaece sozinha**. 0 CPU relevante em Preparo, Pausa e oculto.
- **RNF-28.3 (legibilidade):** todos os traços de inimigo e todas as fraquezas de ramo aparecem em tooltip; nenhum stat oculto (RF-28.15).
- **RNF-28.4 (dificuldade independente do monitor):** mesma curva em 1366×768 a 3840×2160 e com qualquer número de ícones, pelo mundo lógico (RF-28.15, DP-28.2).
- **RNF-28.5 (conteúdo em JSON):** nenhum número de balanceamento em C# (RNF-12.1): torres, ramos, inimigos, chefes, habilidades, fases e árvore em `Content/*.json`, cobertos pelo `ContentHash` e pelo `ContentValidator`.
- **RNF-28.6 (migração segura):** um save do idle **nunca** impede o jogo de abrir (DP-28.4); a falha de leitura cai em perfil novo com aviso, sem crash.

## 7. Estratégia de implementação

1. Aprovar DPs críticos (§11) e registrar ADR-035; rodar TA.1 em paralelo (não depende desta ficha).
2. Reescrever o **Core** em fatias que rodam no WSL (TD.1 a TD.4): conteúdo, máquina de partida e comandos, combate com traços e habilidades, mundo lógico e pads. SanityBot e replay primeiro (TD.10), para ter a curva medida **antes** de desenhar.
3. Trocar Runtime e UI em seguida (TD.5 a TD.8), mantendo Platform e infra Windows existentes.
4. Conteúdo visual e balanceamento (TD.9, TD.10 de novo), depois pós-MVP (TD.11).

## 8. Dependências

SDD-21 (mantido), SDD-22 RF-22.8 (hotspots, mantido), SDD-10/11 (a revisar), SDD-16 (orçamento: a revisar), SDD-17 (testes), TA.1 (z-order). **Não depende** de SDD-24 (reescrito), SDD-25, SDD-26 e SDD-27.

## 9. Critérios de aceitação

- **CA-28.1:** Uma partida da fase 1 só inicia ondas por `StartWave` (ou auto-início se habilitado); em Preparo nenhum inimigo se move nem nenhuma torre recarrega (teste de Core, T-28.1).
- **CA-28.2:** Construir, vender, melhorar e usar habilidade funcionam durante a Onda e entram no log; o replay reproduz o mesmo `StateHash` (T-28.2).
- **CA-28.3:** `CallNextWave` paga `bônus = ceil(ETA × (0,6 + 0,05·w))` e é rejeitado antes de os spawns acabarem (T-28.3).
- **CA-28.4:** Cada torre tem 5 níveis, ramo A/B no 4 e capstone no 5, com custos e fraquezas lidos do JSON; o `ContentValidator` rejeita ramo sem fraqueza declarada (T-28.4).
- **CA-28.5:** Cada tipo de inimigo da RF-28.7 tem pelo menos uma torre/ramo que o contra-ataca e uma que sofre (matriz de teste de Core, T-28.5).
- **CA-28.6:** As 6 fases são concluíveis pelo `SanityBot` com seed fixa na curva esperada (fase 1 a 3 com ★2; fase 6 com derrota possível); resultado registrado na evidência (T-28.6).
- **CA-28.7:** Dois monitores/DPIs com a mesma seed e os mesmos comandos produzem o mesmo `StateHash` (mundo lógico; T-28.7).
- **CA-28.8:** Com o App em execução (MTC, Windows isolado e **com autorização do usuário**, memória `sem-testes-interativos-sem-aviso`): trilho e torres dentro da barra, sem faixa acima, ícones continuam clicáveis, o jogo **não esmaece sozinho** (MTC-28.1 a 28.4).
- **CA-28.9:** Em Preparo e em Pausa o App fica a 0 fps (medição de SDD-16; rótulo "não medido" até medir).
- **CA-28.10:** Nenhuma tela do jogo exibe pergunta, resposta ou pontuação de conhecimento (inspeção + T-28.10 sobre `Strings.cs`).

## 10. Cenários de teste

| ID | Cenário |
|---|---|
| T-28.1 | Máquina de partida: Preparo não avança tick; `StartWave` inicia; Vitória/Derrota |
| T-28.2 | Comandos durante a onda e replay (`StateHash` igual) |
| T-28.3 | `CallNextWave` (bônus, rejeição, sobreposição, teto de 44 inimigos) |
| T-28.4 | Árvore de torres: custos, ramo, capstone, `SellTower` 70% |
| T-28.5 | Matriz contra-escolha (voador × canhão, blindado × Penetrador, regenera × burst, drena × alcance) |
| T-28.6 | SanityBot nas 6 fases; curva de HP × orçamento |
| T-28.7 | Mundo lógico: mesma partida em 3 larguras de barra |
| T-28.8 | Migração de save do idle (DP-28.4) |
| T-28.9 | `LayoutChanged` reavalia pads e `SEM POSIÇÃO` com `maxTowers` |
| T-28.10 | Varredura de textos: nenhuma pergunta ou quiz no jogo |

## 11. Riscos técnicos e decisões pendentes

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| TA.1 reprovar (z-order sobre `Shell_TrayWnd` instável) | Média | Alto | Gate antes de executar TD.7; fallback `Band` mínima (DP-28.11) |
| 48 DIP é pouco para legibilidade (trilho de 14 DIP) | Média | Alto | Código visual (RF-28.15), tooltips, zoom por hover; protótipo de render na TD.7 antes do conteúdo |
| Dificuldade variar por quantidade de ícones | Alta (se DIP físico) | Médio | Mundo lógico e `maxTowers` fixos (RF-28.15) |
| Perder o "jogar de relance" do idle e a atenção do jogador ao trabalho | Média | Médio | Início manual por onda, pausa automática, auto-início opcional |
| Escopo de reescrita grande (Core quase todo) | Alta | Alto | Fatias TD.1 a TD.4 no WSL com replay; reaproveitar Content/Determinismo/Layout |
| Balanceamento inicial ruim | Alta | Médio | Tudo em JSON, SanityBot (CA-28.6), playtest |

### Decisões (DP) — todas decididas em 2026-11-16 (as que exigiam o usuário estavam em **negrito**)

| ID | Questão | Proposta | Decisão |
|---|---|---|---|
| **DP-28.1** | Onde andam os inimigos se não há faixa acima? | Trilho de ~14 DIP no topo da própria barra, sobre os ícones (RF-28.15). Alternativa: faixa mínima de 16 DIP acima | Decidida (2026-11-16): trilho de ~14 DIP no topo da própria barra, sobre os ícones |
| **DP-28.2** | Mundo lógico fixo (1.200 unidades) ou DIP físico (como hoje)? | Lógico fixo, dificuldade igual em qualquer monitor | Decidida (2026-11-16): aceita a proposta (mundo lógico fixo de 1.200 unidades) |
| **DP-28.3** | Torres em pads discretos ou X livre com snap de 44 DIP? | X livre com snap (mantém ADR-034) e limite `maxTowers` | Decidida (2026-11-16): aceita a proposta (X livre com snap de 44 DIP e `maxTowers`) |
| **DP-28.4** | Cortar presença/offline e o que fazer com saves antigos (Reservas, torres, onda)? | Cortar tudo; migrar convertendo `ondaMax` em selos iniciais ou recomeçar | Decidida (2026-11-16): cortar presença, offline, Reservas, Migrar e missões; save antigo convertido em selos iniciais pela onda máxima |
| **DP-28.5** | Banco de perguntas Azure: arquivar, usar como "Códex" opcional sem quiz ou apagar? | Arquivar; só flavor de uma linha em tooltips | Decidida (2026-11-16): banco de perguntas arquivado; Azure só como flavor curto nos tooltips |
| DP-28.6 | Chamada antecipada: fórmula do bônus, teto e se reduz recargas | `ceil(ETA × (0,6 + 0,05·w))`; reduz recargas em `ETA × 0,25` | Decidida (2026-11-16): aceita a proposta (bônus `ceil(ETA × (0,6 + 0,05·w))`, reduz recargas em `ETA × 0,25`) |
| DP-28.7 | Venda 70% (inclusive durante onda) e juros de economia | 70% sempre; sem juros | Decidida (2026-11-16): aceita a proposta (venda 70% sempre, sem juros) |
| **DP-28.8** | Estrelas: ★3 = 0 de integridade perdida ou ≤ 2? | ≤ 2 (menos frustrante para quem joga trabalhando) | Decidida (2026-11-16): três estrelas com até 2 de integridade perdida |
| **DP-28.9** | Conteúdo pós-MVP: modo infinito, desafio do dia, políticas roguelite, caixas, missões como metas de partida | MVP só com 6 fases, estrelas, desafios e árvore de selos | Decidida (2026-11-16): MVP enxuto (6 fases, estrelas, desafios, árvore de selos); modo infinito e desafio do dia pós-MVP |
| DP-28.10 | Número de fases e duração de partida no MVP | 6 fases, 8 a 15 min | Decidida (2026-11-16): aceita a proposta (6 fases, 8 a 15 min) |
| **DP-28.11** | Se TA.1 reprovar: aceitar `Band` mínima (28 a 32 DIP) ou cancelar o redesenho visual | `Band` mínima como último recurso | Decidida (2026-11-16): se TA.1 reprovar, aceitar `Band` mínima de 28 a 32 DIP |
| DP-28.12 | Pausar a simulação ao ocultar (fullscreen/sessão bloqueada) | Sim, sempre | Decidida (2026-11-16): aceita a proposta (pausar sempre ao ocultar) |
| DP-28.13 | Hotkeys globais padrão (Ctrl+Alt+Espaço/P/G) | Como proposto, configuráveis | Decidida (2026-11-16): aceita a proposta (hotkeys padrão configuráveis) |
| DP-28.14 | HUD: posição na barra (canto livre mais próximo do núcleo) e se cabe no widget existente | Canto livre; popover para o resto | Decidida (2026-11-16): aceita a proposta (canto livre mais próximo do núcleo; popover para o resto) |
| DP-28.15 | Modos de mira expostos: Primeiro, Último, Mais forte, Mais fraco, Mais próximo | Todos | Decidida (2026-11-16): aceita a proposta (todos os modos de mira) |

## 12. Plano de migração

### 12.1 Fichas sugeridas

Legenda: 🟢 WSL puro, 🟡 WSL + Windows, 🔴 só Windows. Todas dependem da aprovação desta ficha (**TD.0**, concluída em 2026-11-16).

| Ficha | Descrição | Depende de | Ambiente |
|---|---|---|---|
| TD.0 ✅ | Aprovar DPs da §11 e registrar ADR-035 em `DECISOES.md` (feito em 2026-11-16); **TA.1** (já planejada) segue como gate de TD.7 | — | usuário |
| TD.1 | Conteúdo TD v2: `towers.json` (ramos), `enemies.json` (traços), `waves.json` autoral por fase, `stages.json` (6), `abilities.json`, `meta.json` (selos e árvore); `ContentValidator` com fraqueza declarada | TD.0 | 🟢 |
| TD.2 | Core: máquina de partida (`Preparo/Onda/Derrota/Vitória`), `StartWave`, `CallNextWave`, auto-início, integridade, `SellTower`, créditos e bônus | TD.1 | 🟢 |
| TD.3 | Core: combate com traços (voador, blindado, regenera, drena), balanceador/nós, ramos/capstones, mira `Close`, habilidades, chefes; `StateHash` | TD.2 | 🟢 |
| TD.4 | Core: mundo lógico 1.200 e pads (`LayoutChanged` → mapa lógico→físico), `maxTowers`, `SEM POSIÇÃO` | TD.2, SDD-21 | 🟢 |
| TD.5 | Perfil v2 e persistência (selos, estrelas, árvore, desbloqueios, seed), migração do save do idle (DP-28.4) | TD.1, TD.2, SDD-14 | 🟢 |
| TD.6 | Runtime/host: remover presença/offline/eventos do `GameHost`, pausa por ocultação, auto-início, hotkeys, bandeja | TD.2 | 🟡 |
| TD.7 | Render: cena só na barra (`SceneLayout` com altura da barra), trilho, torres, indicadores de traço, hotspots, 0 fps em Preparo; **gate: TA.1 aprovado** | TD.4, TD.6, TA.1, TA.5 | 🟡/🔴 |
| TD.8 | UI: HUD compacto, popover (inspetor, construção, preview de onda, habilidades, Mapa, árvore), Configurações (auto-início, opacidade em segundo plano desligada) | TD.6, TD.7 | 🟡 |
| TD.9 | Arte e visual: ícones de 8 inimigos, 3 chefes, 5 torres + ramos, código de cor (SDD-20/09 revistos) | TD.7 | 🟡 |
| TD.10 | `SanityBot` novo e curva de balanceamento das 6 fases; evidência E-TD | TD.3 | 🟢 |
| TD.11 | Pós-MVP: modo infinito, desafio do dia, políticas (DP-28.9) | TD.8, TD.10 | 🟡 |
| TD.12 | Limpeza e documentação: arquivar SDD-25/26/27, marcar substituições, `PLANO.md`, `README.md`, MTC-28 | todas | 🟢 |

**Ordem sugerida:** TD.0 → (TD.1 → TD.2 → TD.3 ‖ TD.4 ‖ TD.10) → TD.5 e TD.6 → TD.7 (gate TA.1) → TD.8 → TD.9 → TD.12; TD.11 por último.

### 12.2 O que é reaproveitado (sem retrabalho)

`PCG32`/`StateHash`/`ReplayRunner` (estrutura), `PowTable`, `CreditFormatter`, `ContentLoader`/`ContentValidator`/`ContentHash`, `EntityStore`/`Handle`, `StripLayout` e `LayoutChanged`, `PlacementValidator` 1D, `BuildZoneMap`, UIA (SDD-21), `FullscreenWatcher`/`WinEventHook`, `ZOrderCoordinator`, hotspots (SDD-22 RF-22.8), `TrayIcon`.

### 12.3 Fichas e SDDs substituídos ou a revisar (não editados nesta ficha)

| Ficha | Situação proposta |
|---|---|
| **SDD-27** (economia idle) | **Substituída**, exceto RF-27.2 (`CreditFormatter`), RF-27.3 (`RateMeter`, opcional) e a ideia de `PowTable` |
| **SDD-25** (presença e offline) | **Substituída por completo** |
| **SDD-26** (eventos ao vivo, perguntas) | **Substituída**; só ficam o ledger/RNG como utilitários, se úteis |
| **SDD-13** | Já parcialmente substituída; passa a **substituída** |
| **SDD-24** (painel e cards) | **Reescrita** (RF-24.6 Campanha → Mapa, 24.7 Missões e 24.8 Migrar removidos, 24.9/24.10 cards removidos; Arsenal vira o popover de construção) |
| **SDD-23** (widget HUD) | **Reescrita** como HUD compacto (RF-28.14) |
| **SDD-22** (faixa de batalha) | **Revisar**: sem cena de 104 DIP, só barra; manter RF-22.8 (hotspots), RF-22.10 (fps), RF-22.11 (modo construção) |
| **SDD-21** | **Mantida**; revisar o fallback `Band` (DP-28.11) |
| **SDD-11** | **Revisar** RF-11.4 (ordem do tick), RF-11.5 (mira `Close`/voador), RF-11.6 (dano) e RF-11.10 (comandos: `StartWave`, `CallNextWave`, `SellTower`, `ChooseBranch`, `UseAbility`) |
| **SDD-12** | **Revisar** RF-12.4 a RF-12.8 (formato novo de conteúdo; ondas autorais) |
| **SDD-10** | **Revisar** RF-10.2 (`MatchPhase`), RF-10.7 (`RenderPolicy` sem presença) |
| **SDD-07** | **Revisar** (sem inatividade por presença; pausa por ocultação) |
| **SDD-14** | **Revisar** (perfil v2 e migração, TD.5) |
| **SDD-15, 16, 17** | **Revisar** (atalhos, orçamento de 0 fps em Preparo, testes novos) |
| **SDD-19** | Já histórica |
| **PLANO.md / README.md / DECISOES.md** | Atualizar em TD.12 (ADR-035; este documento **não** os altera) |

> Registro de fontes: todos os links da §3 foram abertos ou retornados pela pesquisa em 2026-11-16. Números de avaliações são de agregadores e mudam; use-os como ordem de grandeza.
