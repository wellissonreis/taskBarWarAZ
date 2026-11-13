# SDD-26: Eventos ao vivo: pico de demanda, chefe com pergunta Azure e caixas de suprimentos

**Status:** Rascunho para revisão · **Etapa:** E6 · **Depende de:** SDD-22, SDD-24, SDD-25, SDD-27 · **Bloqueia:** nada (o estado persistido entra em SDD-14)

## 1. Identificador e título

SDD-26: Eventos que só ocorrem com o jogador presente: pico de demanda, chefe com pergunta Azure, caixas de suprimentos, banco de perguntas, ledger e regras de prioridade.

## 2. Objetivo

- Dar ao jogador presente decisões e recompensas curtas (cliques de poucos segundos), sem exigir atenção contínua e sem pausar o jogo.
- Tornar as perguntas Azure (AZ-900) parte do núcleo do jogo (ADR-024): o chefe é o ponto de aprendizado.
- Manter tudo determinístico (Rng PCG32 persistido) e testável no Linux.

## 3. Contexto técnico

- **ADR-024:** perguntas Azure e picos são **núcleo**. Esta ficha **substitui SDD-19**, exceto o **banco de perguntas** (RF-19.10, schema e importação) e o **ledger** (RF-19.7), que são reaproveitados e referenciados. A decisão D-01 (módulo opcional) fica superada por ADR-024.
- **ADR-015 / ADR-021:** nenhum evento abre janela ativável. Banners e o card de pergunta são Skia, sem ativação (SDD-22 e SDD-24).
- **Design aprovado** (artefato "Azure Defense — Idle de barra", estados de Presence): banner âmbar de pico, banner rosa de chefe com 0:45 e o card de pergunta com alternativas A–D. Textos e números do design são ilustrativos onde divergirem desta ficha.
- **Presença:** os eventos só existem em **Presente** (SDD-25 RF-25.1). O relógio dos eventos é o `eventClockSeconds`: tempo em Presente **acumulado, sem zerar ao ficar Ausente** (distinto do `PresentSeconds` contínuo de SDD-25 RF-25.3, que zera), que não avança em Ausente nem Offline. Persistido entre os "contadores de eventos" (SDD-25 RF-25.11, SDD-14).
- **Economia:** SDD-27 (ondas, chefe a cada 20, "min de renda", baús, XP, missões). **Mundo 1D** (ADR-020; ADR-034): posições x em DIP entre o núcleo e o portal (antes: entre o último slot e o portal; RF-26.11).
- **Rng:** `Rng` PCG32 persistido (SDD-11 RF-11.13). Todos os sorteios desta ficha consomem esse `Rng`, em ordem fixa, e entram no `StateHash`.

## 4. Escopo e exclusões

**Escopo:** `PeakSystem`, `BossEncounter`, `SupplyCrateSystem`, `EventScheduler` (prioridade e não sobreposição), banco e seleção de perguntas, `QuestionLedger`, comandos e eventos novos.

**Exclusões:** visual dos banners, do card de pergunta e da caixa (SDD-22/24); presença e farm (SDD-25); fórmulas de ondas, baús e missões (SDD-27); formato do save (SDD-14); escala vertical, horizontal, elasticidade e debrief de SDD-19 (removidos pelo gênero idle; ver RF-26.14).

## 5. Requisitos funcionais

### RF-26.1: Regras gerais

- Apenas em **Presente**. Ao passar a Ausente ou Offline: banners e caixas ativos são **removidos**, o bônus de pico (RF-26.3) é **encerrado**, e os contadores de sorteio **congelam** (voltam a avançar em Presente). Extras de pico ainda na `PeakQueue` **terminam de nascer**, sem bônus (fazem parte da onda; ADR-032).
- Eventos **não** pausam o jogo. Fechar ou minimizar o card de pergunta **não** pausa o tempo.
- **Prioridade e não sobreposição** (RF-26.12): no máximo 1 banner por vez e no máximo 1 caixa.
- Cliques em banners e caixas contam como **interação** (SDD-25 RF-25.1).
- Todos os sorteios via `Rng` persistido; a ordem de consumo está definida em cada RF.

### RF-26.2: Pico de demanda: agendamento e banner

- **Intervalo:** sorteado entre **8 e 15 min** de tempo presente (uniforme em segundos inteiros), contado desde o **fim** do evento anterior (ou desde o início da presença, na primeira vez). O sorteio ocorre quando o evento anterior termina.
- **Banner âmbar central** (SDD-22): texto "PICO DE DEMANDA · ESCALAR ×3", contagem regressiva **0:30** e anel pulsante. É **clicável**.
- **Ao aparecer:** a onda recebe **6 inimigos extras** (`swarm`), espaçados em **12 s** (extra `i` em `t = i × 12 s`, `i = 0..5`, a partir do surgimento do banner; HP, ouro e XP da onda atual, SDD-27 RF-27.6). Os extras entram em uma fila própria (`PeakQueue`) e contam como trabalho pendente: a próxima onda automática **só começa** depois que todos os extras tiverem nascido (e a onda atual estiver limpa).
- **Ignorado** (contagem chega a 0:00): o banner some, os extras **seguem sem bônus** e o próximo pico é sorteado (8–15 min).

### RF-26.3: Pico de demanda: escala (clique)

- **Clique em até 30 s:** dano de **todas as torres ×3 por 60 s** de SimTime. O banner vira **"DEFESA ESCALADA ×3"** com contagem regressiva de 60 s, **não clicável**.
- O fator ×3 multiplica o `dano(L)` na hora do disparo (SDD-27 RF-27.4), incluindo super hit, área e Scale Set.
- **Conta para a missão** "Escale 5 picos de demanda" (SDD-27 RF-27.11), apenas quando clicado.
- Terminados os 60 s, o banner some e o próximo pico é sorteado. O bônus **não** acumula com outro pico (não há dois picos simultâneos, RF-26.12).
- Passar a Ausente durante o bônus o **encerra** (RF-26.1).
- **Banner de chefe ocupando o slot (ADR-032):** o bônus ativo continua e a contagem aparece como chip **"×3 0:59"** ao lado do selo de presença no widget (SDD-23 RF-23.3; SDD-22). A string "DEFESA ESCALADA ×3" fica em `Strings` (SDD-15 RF-15.11).

### RF-26.4: Chefe: pré-condição e espera

- Quando a próxima onda é **múltipla de 20** (SDD-27 RF-27.7) e o estado é **Presente**, essa onda **não começa**: o **chefe espera no portal** (sprite e barra de HP, SDD-22), e aparece o banner **rosa** "CHEFE · RESPONDER PERGUNTA AZURE" com contagem **0:45**.
- Em **Ausente** a onda do chefe **não começa**: o jogo repete a onda anterior em farm (SDD-25 RF-25.4). Ao voltar a Presente, o encontro começa.
- Se já existe resposta no ledger para `(fase, tentativa, onda)`, **não** se abre novo encontro: a onda começa direto, com o efeito derivado do ledger (RF-26.7).
- **Clique** no banner abre o **card de pergunta** (SDD-24), sem ativar a janela e sem pausar a simulação.

### RF-26.5: Chefe: a pergunta

- **Seleção:** sorteio via `Rng` no banco do `stageTopic` da fase atual (SDD-27 RF-27.9), **sem repetir na mesma fase até esgotar o banco**: o conjunto de perguntas já usadas na `(fase, tentativa)` é persistido; quando todas foram usadas, o conjunto zera. A pergunta do **COLETAR ×2** de SDD-25 usa o mesmo sorteio e o mesmo conjunto.
- A pergunta é sorteada **ao aparecer o banner** (não ao clicar), gravada no estado do encontro e **não muda** até o fim do encontro (ou até o restore).
- **Alternativas:** 3 ou 4 (A a D). A ordem das alternativas é a do banco (sem embaralhar no v1; DP-26.2).
- **Tempo:** 45 s de SimTime, contados a partir do banner, **inclusive com o card aberto, fechado ou minimizado**. Não pausa.

### RF-26.6: Chefe: resultado

| Resultado | Efeito |
|---|---|
| **Acerto** | chefe entra com **HP ×0,4** (SDD-27 RF-27.12) + **baú comum** (SDD-27 RF-27.13) + explicação |
| **Erro** | destaque da alternativa **certa** + **explicação curta**; chefe com **força total** |
| **Tempo esgotado** | chefe entra com **força total**; a pergunta é marcada como `timeout` no ledger |

- A resolução (qualquer uma) **libera a onda**: a onda do chefe começa e o chefe é inserido no meio da fila (SDD-27 RF-27.7). O HP ×0,4 é aplicado no **spawn** do chefe, derivado do ledger.
- **Acerto de chefe** conta para a estrela 2 e 3 da fase (SDD-27 RF-27.9); erro e `timeout` **não** acertam. **Responder** (acerto ou erro) conta para a missão "Responda 3 perguntas de chefe" (SDD-27 RF-27.11); `timeout` não conta.
- **Explicação:** o texto de `explanation` da alternativa escolhida e o da correta (SDD-19 RF-19.10), exibidos pelo card (SDD-24). O aviso de analogias de SDD-19 RF-19.10 continua no card (valores fictícios).
- O chefe derrotado registra `chefesVencidos` (SDD-27 RF-27.8).
- Uma queda do núcleo durante a onda do chefe aplica o recuo de SDD-27 RF-27.8; ao chegar de novo na onda do chefe, **o ledger já tem a resposta** (RF-26.4): sem nova pergunta e **sem bônus duplicado** (RF-26.7).

### RF-26.7: Ledger de respostas (`QuestionLedger`, reaproveita SDD-19 RF-19.7)

- **Chave:** `(fase, tentativa, onda)`. **Valor:** `{ questionId, optionIndex | timeout, correct, bonusConsumed }`.
- Uma segunda resposta à mesma chave é rejeitada (`AlreadyUsed`) e **não altera** o bônus.
- O bônus de HP ×0,4 é **derivado** de `correct == true && !bonusConsumed`; ao spawnar o chefe, `bonusConsumed = true` (um restore após o spawn **não** reaplica; um restore antes do spawn reaplica uma única vez).
- Faz parte do estado salvo (SDD-14) e do `StateHash`.
- Diferente de SDD-19: o ledger **não** é descartado ao fim da partida, e sim guardado por `(fase, tentativa)`. A `tentativa` incrementa a cada Migrar e na entrada de cada fase (SDD-27 RF-27.9). As estrelas leem o ledger da passagem atual.
- O ledger **não** usa mais `Skip` (pular conta como erro em SDD-19; aqui não há "pular": o `timeout` cumpre o papel).

### RF-26.8: Chefe: espera em Presente → Ausente (DP-26.4, aceita por ADR-030)

Se o estado passa a Ausente ou Offline enquanto o chefe espera: o banner, o card e o chefe do portal são removidos, a pergunta sorteada é **descartada** (devolvida ao banco), o ledger **não** é gravado e a onda do chefe não começa (farm, SDD-25). Ao voltar a Presente, o encontro recomeça com uma **nova** pergunta (DP-26.4).

### RF-26.9: Banco de perguntas (`questions.json`, reaproveita SDD-19 RF-19.10)

- Arquivo `src/SideWar.Core/Content/questions.json`, embutido e coberto pelo `ContentHash` (SDD-12 RF-12.1). **Schema de SDD-19 RF-19.10**, com a mudança: `options` com **3 ou 4** itens (antes exatamente 3), **exatamente uma** `correct`.
- As perguntas **importadas da referência** têm 3 alternativas; as **novas** podem ter 4. **Revisão humana obrigatória** antes de entrarem no repositório, como em SDD-19 RF-19.10; o script de importação é manual e único, e o repositório `sideWarAZ` **não** é alterado.
- **Validador** (substitui as regras de SDD-19 RF-19.10): `id` único; `options` entre 3 e 4 e uma só `correct`; `signals` conforme SDD-19 (3), opcional para as novas (DP-26.1); `stageTopic` entre os 10 tópicos de SDD-27 RF-27.9; textos não vazios; **mínimo por tópico** igual ao maior número de chefes da fase (SDD-27: 3, 4 ou 5), proposto 8 para dar variedade (DP-26.3). O campo `kind` (`mandatory`/`transfer`) é mantido e **não** influencia o sorteio.
- O aviso permanente "valores fictícios" e os links só como texto (ADR-014) de SDD-19 RF-19.10 continuam.

### RF-26.10: Chefe: cancelamentos (consolidação)

Cancelam o encontro **sem consumir o ledger**: passar a Ausente ou Offline (RF-26.8) e restore anterior à resposta (a pergunta é regravada no estado do encontro, RF-26.5). **Não** cancelam: fechar ou minimizar o card (o tempo corre).

### RF-26.11: Caixas de suprimentos (`SupplyCrateSystem`)

- **Agendamento:** a cada **3 a 6 min** sorteados de tempo presente (uniforme em segundos inteiros), contado desde o desaparecimento da caixa anterior (coletada ou expirada).
- **Posição:** `x` sorteado (inteiro em DIP) **entre o último slot e o portal**, com margem de 24 DIP em cada extremo; se o intervalo for menor que 48 DIP, usa o ponto médio. Os valores de slots e portal vêm de SDD-21/22. **ADR-034 (Aceita (Tech Lead, 2026-11-13)):** sem slots acima da barra, o intervalo passa a ser **entre o núcleo e o portal** (`[LeakX + 24, PortalX − 24]`), pois as torres ficam dentro da barra e a rota inteira acima dela fica livre (canvas: caixa em `x = 1000`). **Após `LayoutChanged` que encurta a faixa**, o `x` de uma caixa ativa é limitado ao novo intervalo (ADR-032).
- **Aparência:** piscando (SDD-22). **Duração:** 45 s de SimTime. Expira sem aviso.
- **Clique:** dá **1 min de renda**, **mínimo de 50 créditos** (`max(50, taxaRenda × 60)`, SDD-27 RF-27.13), com **número flutuante** (SDD-22). A caixa some. Conta para a missão "Colete 10 caixas de suprimentos" (SDD-27 RF-27.11).
- No máximo **1 caixa** por vez.

### RF-26.12: Prioridade e não sobreposição (`EventScheduler`)

- **Banner:** no máximo **1** por vez (pico em oferta, "defesa escalada" ou chefe).
- **Chefe tem prioridade:** se o chefe precisa aparecer e há um pico em **oferta**, a oferta é **descartada** (sem punição e sem missão) e os extras já nascidos permanecem. Se há um bônus de pico **ativo**, o bônus **continua até terminar**, e o banner do chefe ocupa o slot de banner (a contagem do bônus passa para o chip "×3 0:59" do widget, SDD-23; DP-26.5, resolvida por ADR-032).
- **Pico adiado:** se o sorteio de pico vence enquanto há um chefe pendente (banner ou card de chefe ativo, ou chefe "esperando"), o pico fica **devido** e aparece **assim que o banner do chefe sair** (resolução do encontro). O próximo pico é sorteado a partir daí.
- **Caixas** não usam banner e **podem coexistir** com um banner. Mas nunca há 2 caixas.
- Sorteios simultâneos vencendo no mesmo tick: ordem fixa **chefe, pico, caixa**; cada um consome o `Rng` na ordem de resolução (RF-26.13).

### RF-26.13: Ordem de consumo do `Rng`

1. Sorteio da pergunta do chefe (índice no banco não usado).
2. Intervalo do próximo pico (segundos 480 a 900).
3. Intervalo da próxima caixa (segundos 180 a 360).
4. Posição `x` da caixa.
5. Pergunta do COLETAR ×2 (SDD-25).

Cada sorteio ocorre **no instante** em que o evento anterior termina ou o banner aparece, e nunca em Ausente ou Offline. O `Rng` entra no `StateHash`.

### RF-26.14: O que SDD-19 deixa de valer (sem editar a ficha)

| SDD-19 | Situação |
|---|---|
| RF-19.1 (`ISimModule`, ativação opcional) | substituído: pico e chefe são núcleo (ADR-024) |
| RF-19.2 (pico: 90 s e ×0,8 de HP etc.) | substituído por RF-26.2 e RF-26.3 |
| RF-19.3 a RF-19.5 (escala vertical, horizontal, elasticidade) | removidos pelo gênero idle; o tema vive na torre Scale Set (SDD-27) |
| RF-19.6 (debrief), RF-19.8 (conceitos) | fora do escopo desta ficha (DP-26.6) |
| RF-19.7 (perguntas de chefe) | substituída, **exceto** o ledger (RF-26.7) |
| RF-19.9 (estrelas com módulo) | substituída por SDD-27 RF-27.9 |
| RF-19.10 (conteúdo) | **reaproveitado** (RF-26.9) |
| RF-19.11 e RF-19.12 (menu e comandos) | substituídos por RF-26.15 |

### RF-26.15: Comandos e eventos

- **Comandos:** `ClickPeakBanner`, `OpenBossQuestion`, `AnswerBossQuestion(optionIndex)`, `ClickSupplyCrate`. Todos exigem `RunState == InStage`, estado **Presente** e evento ativo; caso contrário, `CommandStatus.NotAvailable`. `AlreadyUsed` vem do ledger.
- **`SimEvent` novos:** `PeakBannerShown`, `PeakScaled`, `PeakEnded`, `PeakIgnored`, `BossWaiting`, `BossQuestionShown`, `BossAnswered(correct)`, `BossTimeout`, `CrateSpawned`, `CrateCollected(valor)`, `CrateExpired`.
- Os comandos e eventos entram no `CommandLog` e no `StateHash`, como os demais.

### RF-26.16: COLETAR ×2 (pergunta do card de retorno)

- **Pergunta:** do mesmo sorteio e do mesmo conjunto de usadas do tema da fase (RF-26.5). Tempo de **45 s** de SimTime.
- **Ledger próprio**, por **relatório de retorno** (chave = id do relatório, SDD-25 RF-25.9), separado do ledger de chefes `(fase, tentativa, onda)`; um relatório responde no máximo uma vez.
- **Não conta** para estrelas nem para missões (SDD-27 RF-27.9, RF-27.11).
- **Resultado:** acerto **dobra** créditos e XP pendentes; erro, tempo esgotado ou card recolhido valem **×1**.
- O comando e o evento correspondentes entram no `CommandLog` e no `StateHash` (RF-26.15). Decisão: ADR-032.

## 6. Requisitos não funcionais

- **RNF-26.1:** Determinismo: mesma `seed`, conteúdo e comandos geram o mesmo `StateHash`, no Linux e no Windows.
- **RNF-26.2:** Zero alocação por tick em regime (SDD-11 RNF-11.2) com eventos ativos; os cards e textos são montados fora do tick.
- **RNF-26.3:** Os eventos **não** custam nada oculto ou em Ausente (nenhum timer de evento roda).
- **RNF-26.4:** Cobertura de linhas ≥ 90% em `PeakSystem`, `BossEncounter`, `SupplyCrateSystem`, `EventScheduler` e `QuestionLedger`.

## 7. Estratégia de implementação

1. `QuestionLedger` e seleção de perguntas (banco, conjunto usado, `Rng`).
2. `EventScheduler` com os três sistemas e `FakeClock`/ticks, sem UI.
3. `PeakSystem` e `SupplyCrateSystem`.
4. `BossEncounter` integrado ao `IdleWaveBuilder` (SDD-27) e à presença (SDD-25).
5. Validador do `questions.json` e importação revisada.
6. Integração com banners e cards (SDD-22/24).

## 8. Dependências e decisões pendentes

**Dependências:** SDD-27 (ondas, chefe, baús, missões, estrelas), SDD-25 (estado de presença, farm, COLETAR ×2), SDD-22 (banners, sprite do chefe, caixa piscando), SDD-24 (card de pergunta), SDD-19 (banco e ledger reaproveitados, por referência), SDD-14 (estado persistido).

**Decisões pendentes:**

| ID | Questão | Padrão proposto |
|---|---|---|
| DP-26.1 | `signals` nas perguntas novas | opcional |
| DP-26.2 | Embaralhar alternativas | não (ordem do banco) |
| DP-26.3 | Tamanho mínimo do banco por tópico | 8 (mínimo duro = nº de chefes da fase) |
| DP-26.4 | Chefe esperando e o jogador fica Ausente | cancela e sorteia nova pergunta ao voltar |
| DP-26.5 | Bônus de pico ativo com banner de chefe | o bônus continua, contagem no chip "×3 0:59" do widget. **Resolvida por ADR-032** |
| DP-26.6 | Debrief e registro de conceitos de SDD-19 | removidos |
| DP-26.7 | Posição dos extras do pico (`t = i × 12 s`, `i = 0..5`) | como em RF-26.2 |

## 9. Critérios de aceitação

- **CA-26.1:** Pico: o intervalo sorteado está em [480, 900] s de tempo presente; o contador congela em Ausente; o banner dura 30 s; os 6 extras nascem em 0, 12, 24, 36, 48 e 60 s; clique em ≤ 30 s ativa ×3 por 60 s (e o dano das torres triplica); ignorado = sem bônus e os extras seguem; a missão só conta o clique.
- **CA-26.2:** Chefe: nas ondas 20, 40, 60, 80 e 100 em Presente a onda não começa até a resolução; acerto → HP ×0,4 + baú comum; erro → destaque e explicação, força total; tempo esgotado → força total; fechar o card não altera a contagem de 45 s.
- **CA-26.3:** Ledger: segunda resposta rejeitada; restore antes do spawn reaplica o bônus uma vez; restore depois do spawn não reaplica; recuo por queda e retorno à onda do chefe não repete pergunta nem bônus.
- **CA-26.4:** Sorteio de pergunta: nenhuma repetição na mesma fase até esgotar; depois do esgotamento, o conjunto zera; mesma seed gera a mesma sequência.
- **CA-26.5:** Caixas: intervalo em [180, 360] s; `x` entre o último slot e o portal (margens de 24 DIP); duração de 45 s; ganho `max(50, renda × 60)`; no máximo 1 caixa; conta para a missão.
- **CA-26.6:** Prioridade: nunca 2 banners; o chefe descarta pico em oferta; o pico devido aparece após o chefe; a ordem de consumo do `Rng` é a de RF-26.13.
- **CA-26.7:** Em Ausente: nenhum evento nasce, os existentes são removidos e o chefe esperando é cancelado sem gravar o ledger.
- **CA-26.8:** Validador do banco: rejeita 2 ou 5 alternativas, nenhuma ou duas corretas, `id` duplicado, tópico desconhecido e tópico com menos perguntas que o mínimo; aceita 3 e 4 alternativas.
- **CA-26.9 (Windows):** Banners e cards não ativam a janela e não roubam o foco (SDD-22 T-22.6, SDD-25 T-25.8 e SDD-24; resultado **comprovado** ou **reprovado**).

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-26.1 | `PeakSystem`: agendamento, banner, extras, clique, ignorado, bônus ×3 | Linux |
| T-26.2 | `BossEncounter`: acerto, erro, tempo esgotado, card fechado | Linux |
| T-26.3 | Ledger: dupla resposta, restore antes e depois do spawn, recuo por queda | Linux |
| T-26.4 | Seleção de perguntas sem repetição e reciclagem | Linux |
| T-26.5 | `SupplyCrateSystem`: intervalo, posição, expiração, ganho mínimo | Linux |
| T-26.6 | `EventScheduler`: prioridade, pico adiado, caixa coexistindo | Linux |
| T-26.7 | Presença: congelamento e cancelamento em Ausente e Offline; nenhum timer de evento ativo (RNF-26.3) | Linux |
| T-26.8 | Validador e importação de `questions.json` | Linux |
| T-26.9 | Replay e hash com eventos (5 execuções, Linux e Windows) | Linux/Windows |
| MTC-26.1 | Jogar 30 min presente e conferir picos, caixas e chefe | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Banco de perguntas pequeno gera repetição ou bloqueia o sorteio | Média | Médio | Mínimo por tópico no validador (DP-26.3); reciclagem do conjunto |
| Perguntas novas com 4 alternativas sem revisão humana | Média | Alto | Revisão humana obrigatória (RF-26.9); importação manual |
| Pico e chefe se sobrepondo gerar frustração ou confusão visual | Média | Médio | RF-26.12; ajuste visual em SDD-24; playtest |
| O jogador fechar o card e perder o chefe por tempo | Média | Baixo | Texto do banner e da contagem; o chefe "força total" é a consequência prevista |
| Número de eventos reduzir a sensação de idle | Média | Médio | Intervalos em JSON; ajuste no playtest |
| Restore dentro do encontro de chefe divergir | Baixa | Alto | Ledger derivado e pergunta gravada no estado (RF-26.5); T-26.3 |
