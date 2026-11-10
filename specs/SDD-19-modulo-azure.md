# SDD-19: Módulo educativo Azure: escalabilidade e perguntas de chefe

**Status:** **Substituída por ADR-024 / SDD-26** (D-01 resolvido: perguntas Azure e picos são núcleo, não módulo opcional) · **Etapa:** E9 (pós-MVP) *(histórico; as partes reaproveitadas entram na E6 via SDD-26)* · **Depende de:** SDD-11, SDD-12, SDD-13, SDD-15 *(histórico; reaproveitado por SDD-26: ver abaixo)* · **Bloqueia:** nada

> **Substituída por ADR-024 e SDD-26 (ADR-019).** O texto abaixo é histórico.
>
> **Reaproveitado:** (1) o **banco de perguntas** (RF-19.10: `questions.json`, schema, aviso de valores fictícios, links só como texto), agora com 3 ou 4 alternativas e `signals` opcionais nas novas (SDD-26 RF-26.9, DP-26.1); (2) o **ledger** de respostas (RF-19.7), agora por `(fase, tentativa, onda)`, não descartado ao fim da partida e persistido no perfil (SDD-26 RF-26.7; SDD-14 RF-14.5).
>
> **Removido/substituído:** o módulo opcional e `ISimModule` (RF-19.1; pico e chefe são núcleo, sem `settings.gameplay.azureModule` nem `azureModule` por fase); o pico de 90 s e ×0,8 de HP (RF-19.2, por SDD-26 RF-26.2/26.3); escala vertical, horizontal e elasticidade (RF-19.3 a RF-19.5, removidas; o tema vive na torre Scale Set, SDD-27); debrief por onda e registro de conceitos (RF-19.6, RF-19.8; SDD-26 DP-26.6); perguntas de chefe e `BossQuestionGate` (RF-19.7, por SDD-26 RF-26.4 a RF-26.6, exceto o ledger); estrelas com módulo e `AzureStarRule` (RF-19.9, por SDD-27 RF-27.9); acesso pela interface e comandos/eventos (RF-19.11, RF-19.12, por SDD-26 RF-26.15 e SDD-24); `QuestionWindow` WPF, `PauseReason.Question` e o `Skip` (a pergunta é um card Skia que **não pausa**; o `timeout` cumpre o papel de pular).

## 1. Identificador e título

SDD-19: Módulo opcional e plugável com pico de demanda, escala vertical, escala horizontal, elasticidade, debrief por onda, perguntas de chefe (AZ-900), banco de perguntas e regra de estrelas com perguntas.

## 2. Objetivo

Reproduzir, como **módulo desligado por padrão**, a camada educativa do SideWarAZ (ANALISE §2.7 e §3.2) sem repetir o problema L1 (classe "deus"):

- o combate (SDD-11) não conhece o módulo: com ele desligado, a `Simulation` se comporta e produz hash exatamente como no MVP;
- o módulo entra por uma interface (`ISimModule`) e por sistemas plugáveis em `SideWar.Core`;
- as perguntas aparecem como aviso no overlay e só abrem janela por clique do usuário (ADR-015).

## 3. Contexto técnico

- **Decisão pendente:** D-01 (manter ou não o tema educativo). Enquanto estiver pendente, esta ficha é só proposta e nada dela entra no MVP. O MVP usa o tema visual "Azure Defense" sem perguntas.
- **Gancho de combate:** SDD-11 RF-11.4 (passo 2, `ModuleHooks.PreTowers`, lista vazia no MVP) e RF-11.9 (linha 5 do `PlacementValidator`, `Reserved`).
- **Ondas de chefe:** `bossWaves` de cada fase (SDD-12 RF-12.8; SDD-13 RF-13.3).
- **Estrelas:** ponto de extensão `IStarRule` (SDD-13 RF-13.5).
- **Interface:** HUD em Skia (ADR-009), sem janelas ativáveis por iniciativa do jogo (ADR-015), seção "Selos sobre o campo" de SDD-15 RF-15.1, menu nativo (ADR-010).
- **Rede:** nenhuma (ADR-014). Links oficiais aparecem só como texto.
- **Referência (somente leitura):** `game.ts:42-45,499-524,601-617` (escalabilidade), `campaign.ts:249-271` (perguntas e estrelas), `world-one-content.ts` (cenários e casos de transferência). Os valores abaixo são os do Tech Lead; onde diferem da referência, vale esta ficha.
- **Passo fixo:** 30 Hz (ADR-004). Durações do módulo são contadas em **ticks inteiros** para o determinismo: 1 s = 30 ticks (3 s = 90; 12 s = 360; 2 s = 60; 10 s = 300).

## 4. Escopo e exclusões

**Escopo:**

- `ISimModule` e o registro de módulos em `SideWar.Core`;
- `AzureModule` e seus sistemas: `PeakSystem`, `VerticalScaleSystem`, `ReplicaSystem` (horizontal e elástica), `WaveDebrief`;
- `QuestionBank`, `QuestionLedger`, `BossQuestionGate`, `AzureStarRule` (implementa `IStarRule`);
- comandos `ActivateVertical`, `ActivateHorizontal`, `SetElasticity`, `AnswerQuestion`;
- eventos de módulo no `SimEventBuffer`;
- `QuestionWindow` (WPF, App), selos e debrief na HUD;
- `questions.json`, `azure-module.json` e o script de conversão (manual, único).

**Exclusões (não portados da referência):**

| Item da referência | Motivo |
|---|---|
| Modos de implantação (local, pública, híbrida) | ANALISE L1: pertencem à classe `Game` "deus"; fora do núcleo mínimo |
| IaaS: VM, inimigo `payload` e patches | ANALISE L1; `payload` já está excluído em SDD-12 |
| Empréstimos | ANALISE L1 |
| Custo operacional (upkeep) e telemetria de custos | ANALISE L1; o debrief usa apenas o ouro gasto no módulo |
| Cenários de revisão espaçada (`kind: review`) e recuperação | Fora do pedido; o banco só tem `mandatory` e `transfer` |
| Conteúdo gráfico novo, áudio | Fora do escopo (SDD-09, D-06) |

> As fases 2 (implantação) e 3 (IaaS) mantêm suas perguntas conceituais, mas **sem a mecânica** correspondente. Isso é intencional e fica registrado como risco (seção 11).

## 5. Requisitos funcionais

### RF-19.1: Ativação, interface `ISimModule` e determinismo

- **Padrão:** desligado. Fica ativo numa partida somente se `settings.gameplay.azureModule == true` **e** a fase tem `"azureModule": true` em `stages.json`. O modo Clássico nunca ativa o módulo.
- **`stages.json` e `settings.json`:** os campos novos são opcionais. Ausentes, valem `false`. O conteúdo MVP entrega todas as fases com `false`.
- **Interface (Core):**

```csharp
public interface ISimModule {
    void OnWaveStarted(int wave);                          // StartWave, depois de montar a agenda
    void PreTowers(float dt);                              // RF-11.4, passo 2
    float DamageFactor(Handle tower);                      // 1f se não houver efeito; lido no disparo (RF-11.4, passo 3.3)
    void ModifyBossSpawn(int wave, ref float hpMult, ref float speedMult); // RF-11.8
    bool HasPendingWork { get; }                           // impede CheckEnd enquanto houver extras ou pico
    void OnWaveFinished(WaveEndReason reason);             // WaveCleared, Victory ou Defeat
    bool TryGetReservedPoint(out float x, out float y, out float radius);  // RF-11.9, linha 5
    void WriteHash(ref Fnv1a64 hash);                      // só chamado se o módulo está ativo
}
```

- **Regra de hash (invariante):** `StateHash` chama `WriteHash` **apenas** quando há módulo ativo. A `Simulation` sem módulo não muda de comportamento nem de hash em relação a SDD-11 (teste de regressão contra o hash de CA-11.2).
- **Sistemas plugáveis:** `ModuleHooks` mantém uma lista de `ISimModule` (vazia no MVP). Os métodos são chamados nos pontos citados; nenhum código de combate referencia `AzureModule` diretamente.
- **Sem aleatoriedade:** o módulo **não** consome o `Rng` (consumi-lo alteraria o hash, SDD-11 RF-11.13).
- **Conteúdo numérico:** todos os valores abaixo ficam em `Content/azure-module.json` (RNF-12.1), validado pelo `ContentValidator`. Esta ficha apresenta os valores iniciais.

```json
{
  "peak":       { "fromWave": 2, "warningSeconds": 3, "durationSeconds": 12, "extraGrunts": 4 },
  "vertical":   { "cost": 18, "durationSeconds": 10, "damageFactor": 2 },
  "replica":    { "cost": 35, "provisionSeconds": 2, "activeSeconds": 12, "towerType": "bolt", "level": 1,
                  "maxReplicas": 1, "elasticMinEnemies": 4 },
  "reserved":   { "radius": 36, "points": { "strip-a": { "x": 1030, "y": 140 } } },
  "bossBonus":  { "hpMult": 0.6, "speedMult": 0.75 },
  "debriefSeconds": 8
}
```

### RF-19.2: Pico de demanda (`PeakSystem`)

- **Quando:** a partir da onda 2, em partidas com o módulo ativo. No `OnWaveStarted(wave ≥ 2)` começa o aviso.
- **Linha do tempo (em ticks desde o início da onda):**
  1. `0`: emitir `PeakWarning(wave, 3)`; contagem de 90 ticks.
  2. `90`: emitir `PeakStarted(wave, 12)`; contagem de 360 ticks; `extrasPending = 4`.
  3. Spawn `k` (k = 0..3) no tick `90 + 90·(k+1)`, isto é, em `12·(k+1)/4 s` após o início do pico (3, 6, 9 e 12 s).
  4. `450`: emitir `PeakEnded(wave, extrasSpawned)` quando as 360 ticks acabaram **e** `extrasPending == 0`.
- **Extras:** inimigos `grunt` com HP e velocidade da onda atual (`hpMult`, `speedMult`, `hpScale`, SDD-11 RF-11.8), faixa pela continuidade do `spawnIndex` da onda, ouro e XP de um grunt comum. Emitem `EnemySpawned`.
- **Limite de campo:** o spawn respeita `MaxEnemiesOnField` (44). Se o campo estiver cheio no tick devido, o extra fica **pendente** e nasce no primeiro tick com vaga (adiado, nunca descartado). O pico só termina com `extrasPending == 0`.
- **Fim da onda:** `HasPendingWork` é verdadeiro durante o aviso, o pico e com extras pendentes. A onda não termina nesse intervalo (`CheckEnd`, RF-11.4 passo 9).
- **HUD:** selo no campo (camada Overlay, SDD-15 RF-15.1): "PICO EM {n}s" durante o aviso (n = 3, 2, 1) e "PICO {n}s" durante os 12 s do pico (contagem regressiva). Desaparece em `PeakEnded`.
- **Derrota:** o pico é cancelado sem `PeakEnded`.

### RF-19.3: Escala vertical (`ActivateVertical(towerId)`)

| Item | Regra |
|---|---|
| Custo | 18 de ouro |
| Efeito | `DamageFactor(torre) = 2` por 10 s (300 ticks), aplicado ao `Damage` do projétil **no disparo**; o super hit do Bolt multiplica por cima (×2 × ×2) |
| Limite | uma vez por onda (qualquer torre) |
| Validações, em ordem | módulo ativo (`ModuleDisabled`); `RunState == InStage` e `phase == Wave` (`InvalidPhase`); não pausado (`Paused`); torre existe (`NotFound`); não usada na onda (`AlreadyUsed`); `gold ≥ 18` (`InsufficientGold`, `Missing`) |
| Eventos | `VerticalStarted(towerId)`, `VerticalEnded(towerId)` |
| Fim | aos 300 ticks. Se a torre for vendida antes, o efeito termina sem registrar o conceito (RF-19.8). Fim da onda também encerra sem registro |

### RF-19.4: Escala horizontal (`ActivateHorizontal()`)

| Item | Regra |
|---|---|
| Custo | 35 de ouro, debitado ao ativar |
| Ciclo | `Provisioning` por 2 s (60 ticks, sem disparar) e depois `Active` por 12 s (360 ticks) |
| Réplica | torre `bolt` **nível 1** (alcance, dano, intervalo e velocidade de `towers.json`), no ponto reservado `(1030, 140)` do mapa `strip-a`, modo de mira `First`, **sem super hit** (projéteis com `Owner = Handle.None`, factor 1; SDD-11 RF-11.6) |
| Natureza | entidade **do módulo**: não ocupa o `TowerStore` (capacidade 16), não conta no limite de 16 torres, não pode ser selecionada, vendida ou melhorada |
| Limite | uma vez por onda (manual) e **no máximo UMA réplica global** (manual ou automática) |
| Validações, em ordem | módulo ativo; `phase == Wave`; não pausado; `AlreadyUsed` (manual já usada na onda); réplica existente ou provisionando: `LimitReached` e bloqueio `limit`; `gold ≥ 35`: senão `InsufficientGold` e bloqueio `balance` |
| Eventos | `ReplicaProvisioning(cause)`, `ReplicaActive(cause)`, `ReplicaRemoved(cause, reason)`, `ScaleBlocked(reason)`; `cause ∈ {Manual, Elastic}` |
| Remoção | aos 12 s ou no fim da onda; projéteis em voo da réplica são removidos junto com ela; **sem reembolso** |

- **Ponto reservado:** com o módulo ativo, `TryGetReservedPoint` devolve `(1030, 140)` com raio 36 e o `PlacementValidator` retorna `PlacementReason.Reserved` para `dist < 36` desse ponto (SDD-11 RF-11.9, linha 5; a ordem das linhas 1 a 4 e 6 não muda). Com o módulo desligado, o ponto não é reservado.
- **Validade geométrica:** o ponto precisa estar fora da estrada (`DistanceToRoad ≥ 34`), dentro do campo e longe do núcleo e da entrada. Um teste confirma isso (T-19.6).
- **Sem ponto para o mapa:** se o mapa ativo não tiver ponto em `reserved.points`, o validador de conteúdo rejeita `azureModule: true` para fases desse mapa.

### RF-19.5: Elasticidade (`SetElasticity(bool)`)

- **Padrão:** desligada em toda partida. O estado vive no módulo e é salvo na sessão.
- **Demanda alta:** `peakActive && (enemiesOnField ≥ 4 || extrasPending > 0)`. Só existe entre `PeakStarted` e `PeakEnded`.
- **Provisionamento automático:** com a elasticidade ligada, demanda alta, nenhuma réplica ativa ou provisionando e a escala horizontal manual ociosa:
  - `gold ≥ 35`: debitar 35 e provisionar por 2 s; depois fica ativa (`cause = Elastic`);
  - `gold < 35`: bloqueio `balance` (`ScaleBlocked(balance)`);
  - réplica existente (manual) com demanda alta: bloqueio `limit`.
- **Anti-spam:** `ScaleBlocked` é emitido uma vez por motivo em cada episódio de demanda alta (a marca reinicia quando a demanda cai).
- **Retirada por queda de demanda:** réplica automática ativa e demanda não alta: remover (`ReplicaRemoved(Elastic, DemandDrop)`), **sem reembolso**. Isso registra o conceito `elasticity` (RF-19.8).
- **Desligar durante o pico:** se havia réplica automática ativa ou provisionando, encerra sem reembolso e **sem** registrar conceito (`ReplicaRemoved(Elastic, Disabled)`).
- **Limite compartilhado:** a réplica automática e a horizontal manual dividem o limite global de 1 réplica (RF-19.4).
- **Validações de `SetElasticity`:** módulo ativo (`ModuleDisabled`) e `RunState == InStage`. Vale também em Build e com o jogo pausado (é uma preferência, não um gasto).

### RF-19.6: Debrief por onda (`WaveDebrief`)

- **Dados por onda** (zerados em `OnWaveStarted`): `costSpent` (ouro gasto com vertical, horizontal e réplicas automáticas), `tempDamage` e `tempKills` (dano e abates **temporários**), `peakBaseDamage` (dano à base sofrido durante o pico, de `PeakStarted` a `PeakEnded`) e `concepts` (conceitos vividos na onda).
- **Atribuição de temporários:** dano da réplica inteiro mais a parcela extra (`Damage − Damage/2`) do vertical. Abate temporário é aquele cujo último acerto veio da réplica ou de torre com vertical ativo. Para isso o `Projectile` precisa carregar uma origem (`ProjectileOrigin { None, Vertical, Replica }`); ver a dependência registrada na seção 8.
- **Exibição:** painel curto na HUD (camada Overlay, não janela), no fim da onda (`WaveCleared`), por `debriefSeconds` (8 s) ou até o próximo `StartWave`. Linhas: "Custo ₵{n} · Dano temp. {n} · Abates temp. {n} · Pico: −{n} núcleo · Conceitos: {lista}" e a nota "valores fictícios".
- Sem clique, sem foco e sem pausa. Não é salvo (efeito de apresentação, ADR-013); os totais da onda vivem no módulo apenas até o fim do debrief.

### RF-19.7: Perguntas de chefe (`BossQuestionGate`)

- **Quantidade:** 3 por fase, uma antes de cada onda de chefe (`bossWaves` da fase, SDD-12). Ordinais 0 e 1 são `mandatory`; o ordinal 2 (chefe final) é a **transferência** (cenário inédito, `kind: transfer`).
- **Seleção:** determinística por `(stageId, attempt, ordinal)`, sem `Rng`: `mandatory` por índice `hash(stageId, attempt, ordinal) mod n` no conjunto do `stageTopic` (sem repetir a pergunta do ordinal 0 no ordinal 1); `transfer` é a do tópico da fase.
- **Pergunta pendente (derivada):** na fase Build, `pendente ⇔ (wave + 1) ∈ bossWaves && !ledger.Has(stageId, attempt, wave + 1)`. Como é derivada, sobrevive a restore sem estado extra.
- **Aviso na HUD:** selo "PERGUNTA DO CHEFE — clique para responder" sobre o campo, no mesmo nível de "CHEFE NA PRÓXIMA ONDA" (SDD-15 RF-15.1). A região do selo é clicável.
- **Bloqueio:** com pergunta pendente, `StartWave` (botão, menu e Ctrl+Alt+N) é rejeitado com `CommandStatus.QuestionPending`, e o toast mostra "Responda a pergunta do chefe". Sai do bloqueio ao **responder** ou **pular**.
- **`QuestionWindow` (WPF, App):**
  - abre **somente** por clique do usuário no selo (ADR-015); nenhum evento do jogo, restore, fim de onda ou atalho abre a janela;
  - enquanto aberta, `PauseReason.Question` está ativo; ao fechar, é removido e `ForegroundRestorer.Restore(prev)` devolve o foco (SDD-15 RF-15.9);
  - conteúdo: problema, **3 sinais** e **3 alternativas**; botão [Pular]; rodapé permanente com o aviso de analogias (RF-19.10);
  - ao responder: mostra o feedback (correto ou incorreto), a explicação da alternativa escolhida e a da correta, e o botão [Fechar];
  - fechar sem responder (botão do sistema) mantém a pergunta pendente; **pular** conta como erro.
- **Resultado:** `AnswerQuestion(stageId, attempt, wave, optionIndex | Skip)` grava no ledger e emite `QuestionAnswered(wave, correct)`.
- **Efeito no chefe:** resposta correta arma o bônus do chefe **daquela** onda: `hpMult ×0,6` e `speedMult ×0,75` aplicados em `ModifyBossSpawn` (RF-11.8; HP arredondado com `RoundToEven`, velocidade com `Det.Quantize`). Erro ou pulo: força total. O bônus é derivado do ledger (`ledger[(stage, attempt, wave)].Correct`) e só afeta o spawn do chefe; um restore depois do spawn não reaplica.
- **Ledger (`QuestionLedger`):** chave `(stageId, attempt, wave)`; valor `{ questionId, optionIndex | skipped, correct }`.
  - Segunda resposta à mesma chave é rejeitada (`AlreadyUsed`) e não altera o bônus.
  - Faz parte da sessão salva (SDD-14) e do `StateHash` (apenas com o módulo ativo).
  - É descartado quando a sessão é apagada (vitória, derrota, abandono; SDD-13 RF-13.6).

### RF-19.8: Registro de conceitos experienciados

- **Evento de registro:**
  - `vertical`: ao fim dos 10 s da escala vertical;
  - `horizontal`: ao fim da réplica **manual** (12 s ativa);
  - `elasticity`: quando a réplica automática sai por queda de demanda.
- Cada registro guarda `(conceito, stageId, wave)`. O texto exibido no perfil é "conceito experienciado nas ondas X" (lista de ondas por conceito).
- **Consolidação:** os registros vivem no módulo durante a partida e entram no `Profile` **só na vitória**, junto da consolidação de SDD-13 RF-13.6. Em derrota ou abandono não entram (coerente com RNF-13.2: o perfil não muda durante a partida).
- O conceito vivido na onda também alimenta o debrief (RF-19.6).

### RF-19.9: Estrelas com módulo (`AzureStarRule : IStarRule`)

Substitui a regra de SDD-13 RF-13.5 quando o módulo está ativo. Entradas: `won`, `hpRatio`, `errors` (de 0 a 3, contando pulos como erros) e `transferCorrect`.

| Condição | Estrelas |
|---|---|
| Derrota | 0 |
| Vitória | 1 |
| Vitória com `errors ≤ 1` | 2 |
| Vitória com `errors == 0` **e** `transferCorrect` **e** `hpRatio ≥ 0,90` | 3 |

- Avalia-se de cima para baixo e vale a maior condição satisfeita.
- O recorde guarda o **máximo** e nunca diminui (SDD-13 RF-13.5).
- Uma fase com o módulo desligado usa a regra de SDD-13, mesmo que outra fase use esta.

### RF-19.10: Conteúdo (`questions.json`) e avisos

- **Arquivo:** `src/SideWar.Core/Content/questions.json`, embutido e coberto pelo `ContentHash` (SDD-12 RF-12.1). Schema:

```json
{ "questions": [ {
    "id": "cloud-m1", "stageTopic": "cloud", "kind": "mandatory",
    "problem": "…", "signals": ["…", "…", "…"],
    "options": [ { "text": "…", "correct": true,  "explanation": "…" },
                 { "text": "…", "correct": false, "explanation": "…" },
                 { "text": "…", "correct": false, "explanation": "…" } ] } ] }
```

- **Validador:** `id` único; `signals` com exatamente 3; `options` com exatamente 3 e **exatamente uma** `correct`; `stageTopic` entre os 10 tópicos das fases (SDD-13 RF-13.3); cada tópico tem ao menos 2 `mandatory` e 1 `transfer`; textos não vazios.
- **Importação:** **manual e única**, por um script de conversão a ser escrito nesta etapa (local definido na etapa). Entrada somente leitura: `/home/wellissonreis/repos/sideWarAZ/src/world-one-content.ts` (cenários e casos de transferência) e `campaign.ts` (contexto de decisão). A saída é revisada por um humano antes de entrar no repositório; o script **não** é executado no build. O repositório `sideWarAZ` não é alterado.
- **Aviso permanente:** "Os valores deste jogo (₵, segundos, limites) são analogias fictícias e **não** representam preços nem tempos do Azure." Aparece no rodapé da `QuestionWindow`, no briefing da fase com módulo (`CampaignWindow`), na `AboutWindow` e no debrief (forma curta: "valores fictícios").
- **Links AZ-900:** só **como texto** (sem `Hyperlink`, sem navegação, sem rede; ADR-014), na `AboutWindow` e no rodapé da `QuestionWindow`, a partir de `az900-link.ts` da referência (guia de estudo e avaliação de prática).
- Strings em `Strings.cs` (SDD-15 RF-15.11).

### RF-19.11: Acesso aos comandos na interface

- Os comandos de escala não ganham botões na HUD (a caixa de 216×176 DIP, RF-15.1, já está cheia). Com o módulo ativo, o **menu nativo** (RF-15.8) ganha, abaixo de "Pausar / Retomar":
  - "Escala vertical (torre selecionada) · 18 ₵";
  - "Escala horizontal · 35 ₵";
  - "Elasticidade" (✓ se ligada).
- Itens ficam `MF_GRAYED` quando a validação falharia. O motivo de uma falha aparece no `FeedbackToast`: `balance` → "Saldo insuficiente"; `limit` → "Limite de 1 réplica"; `AlreadyUsed` → "Já usada nesta onda"; `QuestionPending` → "Responda a pergunta do chefe".
- Com o módulo desligado, nenhum desses itens, selos ou textos existe.

### RF-19.12: Eventos e comandos adicionados

- **`SimEvent` (tipos novos, só com módulo ativo):** `PeakWarning, PeakStarted, PeakEnded, VerticalStarted, VerticalEnded, ReplicaProvisioning, ReplicaActive, ReplicaRemoved, ScaleBlocked, QuestionAnswered`.
- **`CommandStatus` (valores novos):** `ModuleDisabled, AlreadyUsed, QuestionPending`. Os demais motivos reutilizam `InsufficientGold`, `LimitReached`, `InvalidPhase`, `Paused` e `NotFound` (SDD-01).
- **Comandos novos:** `ActivateVertical(towerId)`, `ActivateHorizontal()`, `SetElasticity(bool)`, `AnswerQuestion(...)`. Comandos de gasto exigem `RunState == InStage`, `phase == Wave` e jogo não pausado (RF-19.3, RF-19.4).

## 6. Requisitos não funcionais

- **RNF-19.1:** Com o módulo **desligado**, não há custo mensurável no tick: o benchmark `wave-heavy` (SDD-16/17) compara a `Simulation` sem módulos registrados com a base de SDD-11, e a diferença fica dentro do intervalo de erro do BenchmarkDotNet. 0 B alocados por tick.
- **RNF-19.2:** Com o módulo **ativo**, o `Tick()` custa no máximo **+10 µs na média** sobre o `wave-heavy` sem módulo (meta; BenchmarkDotNet em Windows, com indicação no Linux). 0 B alocados por tick em regime.
- **RNF-19.3:** Cobertura de linhas ≥ 90% nos sistemas do módulo (`AzureModule`, `PeakSystem`, `VerticalScaleSystem`, `ReplicaSystem`, `WaveDebrief`, `QuestionBank`, `QuestionLedger`, `BossQuestionGate`, `AzureStarRule`).
- **RNF-19.4:** Determinismo: a mesma partida com o módulo ativo produz `StateHash` idêntico em 5 execuções (Linux e Windows), como em SDD-11 CA-11.2.
- **RNF-19.5:** Nenhum número de balanceamento no código C# (RNF-12.1): custos, durações e limites vêm de `azure-module.json`.
- **RNF-19.6:** A `QuestionWindow` abre em ≤ 400 ms e libera a árvore visual ao fechar (como RNF-15.4).

## 7. Estratégia de implementação

1. Registrar a decisão D-01 e as revisões de SDD-11, SDD-01, SDD-13 e SDD-14 listadas na seção 8. Sem isso, não começar.
2. `ISimModule`, `ModuleHooks` e testes de regressão: hash sem módulo igual ao de SDD-11.
3. `azure-module.json`, validador e `PlacementValidator.Reserved` com o módulo ativo.
4. `PeakSystem` (ticks inteiros, limite de 44, extras pendentes).
5. `VerticalScaleSystem` e `ReplicaSystem` (manual e elástica, limite global, bloqueios, sem reembolso).
6. `QuestionLedger`, `QuestionBank`, `BossQuestionGate`, bônus do chefe e `AzureStarRule`.
7. Script de conversão e revisão humana do `questions.json`.
8. HUD (selos, debrief), menu, `FeedbackToast`, `QuestionWindow` e textos pt-BR.
9. Benchmarks (RNF-19.1/2) e testes manuais (MTC-19.x).

## 8. Dependências

- **SDD-11:** `ModuleHooks.PreTowers` (RF-11.4), `Reserved` (RF-11.9), comandos e eventos. **Exige revisão de SDD-11** para o que `PreTowers` sozinho não cobre: `OnWaveStarted`, `DamageFactor` (passo 3.3), `ModifyBossSpawn` (RF-11.8), `HasPendingWork` (passo 9), `OnWaveFinished`, `ProjectileOrigin` em `Projectile` (RF-11.2) e os tipos novos de `SimEvent` (RF-11.11).
- **SDD-12:** `bossWaves`, `towers.json` (Bolt), `rules.json` (limite 44), mapa `strip-a`. O ponto reservado vive em `azure-module.json` (não altera o schema do mapa).
- **SDD-13:** `IStarRule` e consolidação no `Profile`. **Exige revisão:** a assinatura de RF-13.5 (`Stars(won, hpRatio)`) não recebe as respostas; é preciso um contexto com `errors` e `transferCorrect`. O `Profile` precisa de um campo para conceitos (RF-19.8).
- **SDD-15:** selos, `FeedbackToast`, menu nativo, `ForegroundRestorer`, regra de janelas (RF-15.9) e `Strings.cs`.
- **Tocadas por extensão (fora das dependências declaradas):** SDD-01 (`CommandStatus`, `QuestionWindow` já citada na camada App), SDD-10 (`PauseReason.Question` já existe) e SDD-14 (ledger na sessão, conceitos no perfil, `azureModule` nas configurações).
- **Decisão D-01:** bloqueia toda a ficha.

## 9. Critérios de aceitação

- **CA-19.1:** Com o módulo desligado, o replay de SDD-11 CA-11.2 produz o mesmo `StateHash` de antes da ficha; o benchmark RNF-19.1 não mostra diferença mensurável.
- **CA-19.2:** As tabelas de custo, duração e limites de RF-19.1 a RF-19.5 são verificadas por testes de tabela (T-19.1 a T-19.5), incluindo as bordas (tick exato, ouro = custo, ouro = custo − 1).
- **CA-19.3:** Os bloqueios `balance` e `limit` ocorrem nos casos de RF-19.4 e RF-19.5, e **não há reembolso** em nenhuma remoção de réplica (T-19.4, T-19.5).
- **CA-19.4:** O ponto `(1030, 140)` é válido fora da estrada e `Reserved` dentro do raio 36 só com o módulo ativo (T-19.6).
- **CA-19.5:** O ledger impede responder duas vezes e duplicar o bônus, inclusive após serializar e restaurar (T-19.9).
- **CA-19.6:** As estrelas respeitam as fronteiras de RF-19.9 (T-19.10) e o recorde nunca diminui.
- **CA-19.7:** `StartWave` fica bloqueado com `QuestionPending` enquanto a pergunta estiver pendente e é liberado ao responder ou pular (T-19.8).
- **CA-19.8:** A `QuestionWindow` **nunca** abre sem clique do usuário (T-19.11 e MTC-19.1).
- **CA-19.9:** O `questions.json` real passa no validador, tem 3 perguntas utilizáveis por fase e foi revisado por um humano (registro na evidência E9).
- **CA-19.10:** RNF-19.2 (+10 µs) e RNF-19.3 (cobertura) medidos e registrados com o rótulo "comprovado" ou "reprovado".

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-19.1 | Ativação: `config × stages.json` (4 combinações); Clássico nunca ativa; hash igual ao de SDD-11 sem módulo | Linux |
| T-19.2 | Pico: nenhum na onda 1; aviso em 90 ticks; spawns nos ticks 180, 270, 360 e 450 (relativos ao início da onda: 90 + 90·(k+1)); eventos `PeakWarning/Started/Ended` na ordem; selo "PICO EM 3s" | Linux |
| T-19.3 | Pico com campo em 44: extra adiado, nasce com vaga, `PeakEnded` só após esgotar; onda não termina antes | Linux |
| T-19.4 | Vertical: custo 18, dano ×2 por 300 ticks, uma vez por onda, venda da torre cancela sem conceito, validações em ordem (`ModuleDisabled`, `InvalidPhase`, `Paused`, `NotFound`, `AlreadyUsed`, `InsufficientGold`) | Linux |
| T-19.5 | Horizontal e elasticidade: custo 35, provisão 60 ticks, ativa 360; sem super hit; réplica fora do `TowerStore`; no máximo 1 réplica (`limit`); `balance`; desligar no pico encerra sem reembolso; saída por queda de demanda registra `elasticity`; fim da onda remove sem reembolso | Linux |
| T-19.6 | `PlacementValidator`: `(1030, 140)` válido com `DistanceToRoad ≥ 34`; `Reserved` a 35,99 e válido a 36,01 do ponto, só com módulo ativo | Linux |
| T-19.7 | Debrief: custo, dano e abates temporários, dano da base no pico e conceitos de uma onda scriptada | Linux |
| T-19.8 | `StartWave` com pergunta pendente: `QuestionPending`; livre ao responder e ao pular; derivação da pendência após restore | Linux |
| T-19.9 | Ledger: segunda resposta rejeitada; bônus do chefe (HP ×0,6, velocidade ×0,75) aplicado uma vez; serializar, restaurar e responder de novo não duplica | Linux |
| T-19.10 | Estrelas: derrota 0; vitória 1; 1 erro 2; 2 erros 1; 0 erros e HP 0,899 → 2; 0 erros e HP 0,90 → 3; 0 erros com transferência errada impossível; recorde não diminui | Linux |
| T-19.11 | `IUiBridge` falso conta aberturas da `QuestionWindow`: 0 após `StartWave`, `WaveCleared`, restore e `StartWave` via atalho; exatamente 1 após o clique no selo | Linux |
| T-19.12 | `QuestionBank`/validador: JSON real e JSON com cada erro (sinais ≠ 3, opções ≠ 3, 0 ou 2 corretas, `id` duplicado, tópico faltando) | Linux |
| T-19.13 | Golden images da HUD: selos "PICO EM 3s", "PICO 12s", "PERGUNTA DO CHEFE" e debrief | Linux + Windows |
| T-19.14 | Benchmarks `wave-heavy`: módulo desligado × sem módulo; módulo ativo (+10 µs) | Windows |
| MTC-19.1 | Com outro app em foco, a pergunta pendente nunca abre janela sozinha; o clique no selo abre a `QuestionWindow`; fechar devolve o foco ao app anterior | Manual |
| MTC-19.2 | Jogar uma fase com módulo ativo em HUD de tamanho S e conferir legibilidade dos selos e do debrief | Manual |
| MTC-19.3 | Conferir o aviso de analogias e os links AZ-900 como texto (sem navegação, sem rede) | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Conteúdo didático desatualizado ou incorreto (o Azure muda; o banco é importado uma vez) | Média | Médio | Importação manual com revisão humana (CA-19.9); aviso de analogias; links oficiais como texto; revisão periódica do `questions.json` |
| Poluição visual da HUD pequena (selos, debrief e aviso de pergunta no tamanho S) | Média | Médio | Debrief temporário de 8 s; selos pequenos na camada Overlay; comandos no menu nativo; MTC-19.2 |
| Dependência da decisão D-01 (a ficha pode ser descartada ou alterada) | Média | Alto | Status "bloqueada por D-01"; módulo plugável e desligado por padrão; nada no MVP depende dela |
| Revisões em SDD-11, SDD-13 e SDD-14 (ganchos, origem do projétil, assinatura de estrelas) | Alta | Médio | Registrar as revisões antes da E9 (seção 8); teste de regressão do hash (CA-19.1) |
| Pico mais difícil que o previsto ao dividir o limite de 44 com ondas densas | Média | Baixo | Valores em `azure-module.json`; playtest na E9 |
| Fases 2 e 3 com perguntas de implantação e IaaS sem a mecânica correspondente | Média | Baixo | Registrado como não portado (seção 4); perguntas conceituais; avaliar nas fases depois do playtest |
| `PauseReason.Question` ficar preso se a janela fechar de forma anormal | Baixa | Médio | Remover o motivo em `Closed` e no encerramento da janela (inclusive em exceção); teste manual de fechar pelo sistema |
