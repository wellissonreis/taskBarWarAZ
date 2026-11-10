# SDD-25: Presença (Presente, Ausente, Offline), auto-escala e retorno offline analítico

**Status:** Rascunho para revisão · **Etapa:** E6 · **Depende de:** SDD-07, SDD-27, SDD-24 · **Bloqueia:** SDD-26

## 1. Identificador e título

SDD-25: Máquina de presença, multiplicadores, modo Ausente, auto-escala, cálculo offline analítico (ADR-022) e card "Bem-vindo de volta".

## 2. Objetivo

- Recompensar a presença sem punir a ausência: com o jogador na tela o jogo rende mais e oferece eventos (SDD-26); sem ele, o jogo segue em **modo gerenciado** (×0,25, auto-escala, até 12 h).
- Calcular o retorno de períodos em que nada rodou (overlay oculto, app fechado, sessão bloqueada, sono) com um modelo **analítico**, determinístico e testável no Linux.
- Garantir que os ganhos pendentes nunca se percam.

## 3. Contexto técnico

- **ADR-022:** offline analítico. Enquanto o overlay está oculto ou o app fechado, **nada roda** (SDD-07 RNF-07.1 continua valendo: 0 frames, wakes ≤ 1/s, CPU ≤ 0,1%). O ganho é calculado no retorno.
- **Design aprovado** (artefato "Azure Defense — Idle de barra"): tabela de sistemas "Ausente vs Presente" (Presence), card "Bem-vindo de volta" (Offline), bloco "Bônus de presença" e "Auto-escala (ausente)" (Main). Exemplos do design (3h 12min, +186K, 22 → 37) são ilustrativos; valem as fórmulas.
- **ADR-021:** o card é Skia e **não ativa** a janela. Visual em SDD-24. **ADR-015:** o jogo não abre janela ativável.
- **Relógios (SDD-10 RF-10.8):** `WallClock` (`IClock`) para presença e deadlines; **UTC** do sistema (`DateTime.UtcNow`) apenas para o intervalo offline.
- O ganho por abate, os multiplicadores de Reservas e a taxa de renda vêm de SDD-27 (RF-27.1, RF-27.3, RF-27.12).

## 4. Escopo e exclusões

**Escopo:** `PresenceTracker` (estado e multiplicador), política do modo Ausente (simulação 30 Hz, render 8 fps, farm), `AutoScaler`, `OfflineCalculator` (pura), `OfflineReport`, `PendingRewards`, regras do card, badge, métricas "presente há N min" e a lista de campos a persistir em SDD-14.

**Exclusões:** o desenho do card, do badge e da cena esmaecida (SDD-24/22); eventos ao vivo, pergunta do **COLETAR ×2** e chefe (SDD-26); economia e fórmulas de torres e ondas (SDD-27); formato dos arquivos (SDD-14).

## 5. Requisitos funcionais

### RF-25.1: Estados de presença (`enum PresenceState { Presente, Ausente, Offline }`)

| Estado | Condição |
|---|---|
| **Presente** | overlay visível **e** interação com o jogo há **≤ 5 min** |
| **Ausente** | overlay visível **e** sem interação com o jogo há **> 5 min** |
| **Offline** | overlay oculto por **qualquer** `HiddenReason` de SDD-07 RF-07.1 (`User`, `Fullscreen`, `SessionLocked`, `RemoteDisconnected`, `DisplayOff`, `Suspended`, `Starting`), app fechado, sessão bloqueada ou sono |

- **Interação com o jogo** = clique em **qualquer janela do jogo** (faixa, widget, painel, card, banner, caixa; a HandleWindow não existe mais, ADR-028) **ou** atalho (hotkey) do jogo. As janelas chamam a porta `IPresenceSink.NotifyInteraction()` (SDD-23 RF-23.8, SDD-24), que encaminha para `PresenceTracker.InteractionObserved(now)`. Atividade em **outras** aplicações **não** conta. Isso **substitui SDD-07 RF-07.4** (`IdlePolicy` com `GetLastInputInfo`, `idleMinutes` e `pauseWhenIdle`): a ausência não pausa mais a simulação.
- `PresenceTracker` (Runtime, puro, com `IClock`) recebe `InteractionObserved(now)` e `VisibilityChanged(visible, now)` e expõe `State`, `PresentSeconds` e `LastInteractionUtc`.
- A transição Presente → Ausente ocorre quando `agora − últimaInteração > 5 min` (estritamente maior: aos 300 s ainda é Presente). O `NextDeadline` é agendado em `últimaInteração + 5 min + 1 ms`, sem polling.
- **Ausente → Presente:** qualquer interação.
- **Offline → visível:** o `Show` por ação do usuário (hotkey, menu, bandeja ou segunda instância, SDD-07 RF-07.8) **conta como interação**; um `Show` automático (saída de fullscreen, desbloqueio de sessão, fim de sono, fim de `Starting`) **não** conta, e o estado volta a ser Ausente se a última interação tiver mais de 5 min.
- Os eventos de visibilidade vêm da `AppStateMachine` (SDD-07), sem acesso ao Win32 nesta ficha.

### RF-25.2: Substituições em SDD-07 e SDD-10 (sem editar as fichas)

| Ficha e RF | Efeito desta ficha |
|---|---|
| SDD-07 RF-07.2 (parte `Hidden`) | `Hidden` pausa **sempre**, ignorando `pauseWhenHidden` (nada roda oculto; o ganho vem do cálculo offline). A opção `pauseWhenHidden = false` deixa de existir |
| SDD-07 RF-07.2 (`Idle`) e RF-07.4 | `PauseReason.Idle` removido; `IdlePolicy` substituída pelo `PresenceTracker` |
| SDD-07 RF-07.2 (`Question`) | a pergunta **não pausa** (SDD-26): `PauseReason.Question` deixa de ser usado |
| SDD-07 RF-07.6 (passo 6) | ao exibir, **não** converter `Hidden` em `User` nem mostrar "PAUSADO": o jogo retoma sozinho (idle). `User` e `Dialog` continuam |
| SDD-07 RF-07.6 (passo 3) | o acumulador zera, **e** o `OfflineCalculator` roda antes do frame de reexibição (RF-25.9) |
| SDD-10 RF-10.7 (`RenderPolicy`) | com `PresenceState == Ausente` e a simulação rodando, o render cai para **8 fps** (`LoopConstants.AmbientFps`; padrão, configurável 4/8 em `performance.absentFps`, ADR-031 item 7); a simulação continua a 30 Hz |

### RF-25.3: Multiplicador de presença (`PresenceMultiplier`)

Aplica-se a **créditos e XP** ganhos (SDD-27 RF-27.12). O tempo presente **contínuo** é `PresentSeconds`, que só avança em Presente e **zera** ao ficar Ausente ou Offline.

| Estado | Tempo presente contínuo | Multiplicador |
|---|---|---|
| Presente | 0 a 15 min | ×1,5 |
| Presente | 15 a 60 min | ×2,0 |
| Presente | ≥ 60 min | ×2,5 |
| Ausente | — | ×0,25 |
| Offline | — | ×0,25 |

- Os limites (15 min, 60 min, ×) ficam em `presence.json` (conteúdo, inicial e ajustável).
- Os ganhos de **baú** e de **caixa** usam a renda sem multiplicador de presença (SDD-27 RF-27.13).
- O `PresentSeconds` também alimenta a métrica "presente há N min" e o multiplicador exibido no bloco "Bônus de presença" da aba Missões (SDD-24), e **não** alimenta a missão "Fique presente por 60 min" (SDD-27 RF-27.11), que soma o tempo em Presente **do dia, contínuo ou não**: o `MissionService` acumula esses segundos em seu próprio contador (não zera ao ficar Ausente; persistido com as missões, SDD-14). O relógio dos eventos de SDD-26 (`eventClockSeconds`, também acumulado e sem zerar) é um terceiro contador, distinto de `PresentSeconds`.

### RF-25.4: Modo Ausente (simulação real)

- A simulação **real** continua a 30 Hz, com ondas automáticas (SDD-27 RF-27.7), ganhos ×0,25 e render a 8 fps (SDD-22).
- **Farm no chefe:** se a próxima onda é de chefe (múltiplo de 20), ela **não começa**. A onda anterior (`w − 1`) é **repetida em loop** até o jogador voltar (Presente). Cada repetição rende os ganhos normais dessa onda. A `ondaMax` não avança.
- Os eventos de SDD-26 (pico, caixa, chefe) **não** ocorrem em Ausente. Ao passar de Presente a Ausente com um banner ou um chefe pendente, o encontro é **cancelado** sem consumir o ledger (SDD-26 RF-26.10; DP-25.3).
- Uma queda do núcleo segue as regras de SDD-27 RF-27.8 (recuo de 5 ondas) e conta como "perdeu vida".
- EcoQoS ligado em Ausente (DP-25.6, aceita por ADR-030).

### RF-25.5: Auto-escala (`AutoScaler`, toggle no Arsenal, **ligado por padrão**)

- Vale em **Ausente e Offline** (nunca em Presente).
- A cada **15 min** acumulados de tempo Ausente ou Offline, gasta **50% dos créditos atuais** melhorando **repetidamente** a torre de **menor custo de melhoria**: ordem por custo de SDD-27 RF-27.5, empate por menor `Id`. O custo é recalculado após cada melhoria. Para quando a próxima melhoria não cabe no orçamento (50% do saldo no início da rodada).
- Sem torres ou com o toggle desligado, não gasta.
- O contador `autoScaleAccumSeconds` é persistido e **zera** ao voltar a Presente.
- As melhorias da auto-escala **não** contam na missão "Melhore torres 10 vezes" (DP-27.7) e entram na lista do card (RF-25.9).

### RF-25.6: Offline analítico: regras gerais (`OfflineCalculator.Compute`, pura)

- **Entradas:** `OfflineSnapshot`, `nowUtc`, conteúdo. **Saída:** `OfflineReport` e o novo estado. Sem `Rng`, sem relógio interno, sem I/O.
- **`OfflineSnapshot`** (gravado em `Save(Hidden)` de SDD-07 RF-07.3 passo 4, nos saves periódicos e no encerramento): `savedUtc`, fase, onda, `ondaMax`, região, Reservas, nível, XP, créditos, `carry`, torres (id, tipo, slot, nível), `autoScaleOn`, `autoScaleAccumSeconds`, chefes vencidos, desbloqueios e pendências (RF-25.11).
- **Tempo:** `elapsed = nowUtc − savedUtc`, com **teto de 12 h**. Se `elapsed ≤ 0` (relógio atrasado ou igual), o resultado é "sem ganhos" e **registra log** (`"offline: clock skew"`). Se o snapshot estiver no futuro em mais de 12 h, idem, com log (DP-25.1).
- Só se calcula quando o `elapsed` (já com teto) é **> 0**. O card aparece só se for **≥ 10 min** (RF-25.9). Com menos de 10 min, os ganhos vão **direto** ao saldo, sem card (DP-25.2).
- Resultado **determinístico**: mesma entrada, mesma saída, no Linux e no Windows (RNF-25.1).

### RF-25.7: Offline analítico: modelo de onda

Para cada onda `w` tentada, na fase e na região do snapshot:

```
DPS            = Σ (dano(L) / cadência) das torres com slot      # sem super hit, chilled, lentidão, área
                 (Scale Set usa o dano com o bônus de Raios; SDD-27 RF-27.4)
HPtotal(w)     = Σ HP(w) por inimigo da onda                      # SDD-27 RF-27.6, com região e fase
duraçãoOnda(w) = quantidade(w) × intervalo(w) + 10 s              # SDD-27 RF-27.7
vencida(w)     ⇔ DPS × (duraçãoOnda × 0,8) ≥ HPtotal(w)
```

- Onda **vencida** (sem perder vida): soma `ouro(w)` e `XP(w)` de cada inimigo, com multiplicador **×0,25** (Offline) e o multiplicador de Reservas para os créditos (SDD-27 RF-27.1); a onda avança em 1 e o tempo consumido é `duraçãoOnda + 1,4 s` (DP-27.5).
- O avanço **para** (e passa ao farm) em:
  1. na onda de chefe (múltiplo de 20), que **não é jogada**;
  2. na **primeira onda não vencida**.
- **Farm:** no tempo restante, repete a **última onda vencida** (ou, se a onda inicial não é vencida, a onda `w − 1`, se ≥ 1; se nenhuma é vencida, não rende nada, DP-25.4). Os ganhos do farm são proporcionais ao tempo: `Δt / (duraçãoOnda + 1,4 s) × ganhoPorOnda`, com `carry` fracionário (SDD-27 RF-27.1).
- **Auto-escala** (RF-25.5): aplicada a cada **15 min simulados** (acumulando `autoScaleAccumSeconds` do snapshot). Depois de cada rodada, o cálculo **reavalia** a onda onde parou: se agora é vencida e não é chefe, o avanço continua.
- **Limite:** no máximo **2000 ondas** avançadas por chamada; o excedente do tempo vira farm.
- **Fase concluída:** o cálculo **nunca** conclui uma fase (para na onda do chefe final). Chefes nunca são derrotados offline.
- **Queda:** o modelo não gera quedas (RF-25.9 mostra "sem perder vida"). O campo "nº de quedas" existe no relatório para o caso de uma versão futura (DP-25.5).

### RF-25.8: `OfflineReport`

```
OfflineReport { double TempoAcumuladoS; long Créditos /* bruto, a ×0,25 */; int OndaDe; int OndaPara;
  int Quedas; long Xp; int NívelDe; int NívelPara;
  List<(TowerType tipo, int deNível, int paraNível)> MelhoriasAuto; int NMelhorias; long GastoAuto;
  int PerdaPicos; int PerdaCaixas; int PerdaChefes; bool ParouNoChefe }
```

- `PerdaPicos = floor(min / 11,5)`; `PerdaCaixas = floor(min / 4,5)`; `PerdaChefes = 1` se parou num chefe, senão 0. `min` = minutos de `elapsed` (com teto). As estimativas pelas fórmulas **prevalecem** sobre o exemplo textual do design (ADR-032). Os divisores (11,5 e 4,5) são as **médias** dos intervalos de SDD-26 (8–15 min e 3–6 min) e ficam em `presence.json`.
- **Contabilidade do gasto da auto-escala:** o gasto sai do **saldo atual** imediatamente, até o limite do saldo; o **excedente** (gasto maior que o saldo inicial, pago com os ganhos do próprio período) é descontado do **pendente** de créditos. Assim, o saldo nunca fica negativo. O card mostra o pendente líquido.

### RF-25.9: Card "Bem-vindo de volta" (visual em SDD-24)

- **Quando:** ao ficar visível, se Offline ≥ **10 min** (tempo com teto). Aparece pelo **mesmo** canal dos cards de SDD-24, sem ativar a janela (ADR-021).
- **Campos** (todos do `OfflineReport`): tempo acumulado / 12 h; créditos (marcados "a ×0,25"); ondas `a → b` com "sem perder vida" (ou o nº de quedas, se > 0); XP e nível (`LV x → y`); nº de melhorias da auto-escala e a lista (`Raio 12→14 · Gelo 8→9`); "Perdido por estar ausente": picos, caixas e chefes (RF-25.8).
- **Botões:**
  - **COLETAR ×2:** abre **uma pergunta Azure da fase** (SDD-26 RF-26.16: 45 s, ledger próprio por relatório, não conta para estrelas nem missões): acerto **dobra** os pendentes (créditos e XP, DP-25.7); erro coleta **×1**. Tempo esgotado ou card fechado sem resposta: ×1.
  - **COLETAR +X:** coleta ×1 na hora.
- **Pendências:** os ganhos ficam **pendentes e persistidos** até coletar. Créditos e XP só entram no saldo e no nível **ao coletar** (nível pode subir ao coletar, com possível múltiplos níveis).
- **Ignorado:** com o card sem ação por **60 s**, ele **recolhe para um badge "+X"** no widget (o `X` é o pendente de créditos formatado, SDD-27 RF-27.2). Clicar no badge reabre o card. O pendente **nunca se perde** (nem ao fechar o app).
- **Novo Offline com pendência:** se ocorrer outro período offline ≥ 10 min com pendência não coletada, os valores **somam** em um único relatório.
- **Interação:** abrir, recolher ou clicar no card conta como interação (RF-25.1).
- O card **não ativa** a janela e não pausa a simulação.

### RF-25.10: Aplicação do resultado

- No `Show`, **antes** do primeiro frame (SDD-07 RF-07.6): `OfflineCalculator.Compute` → atualiza onda, `ondaMax`, torres (auto-escala) e saldo; cria o `PendingRewards` e o card. O primeiro frame já reflete o novo estado.
- A onda atual volta a ser jogada **ao vivo**: o relatório só dá a "foto" (`OndaPara`), e a simulação reinicia na onda `OndaPara` (sem inimigos em campo).
- Os eventos de SDD-26 reiniciam seus contadores a partir de Presente (sorteio novo).

### RF-25.11: Persistência (lista para SDD-14; **não** editada aqui)

Campos novos a serem incorporados em SDD-14 (`session.json` e `profile.json`):

- `savedUtc` (timestamp UTC do último snapshot, ISO 8601);
- `presence`: `state`, `lastInteractionUtc`, `presentSeconds`, `autoScaleAccumSeconds`;
- `autoScaleOn`;
- `pending`: `{ credits, xp, melhorias[], quedas, ondaDe, ondaPara, tempoS, perdas }` e `cardState` (`Aberto | Recolhido`);
- do SDD-27: seed e estado do `Rng`, `carry`, `Reservas`, região, torres desbloqueadas, `StageRun`, missões do dia (data local, estado de cada uma), desbloqueios;
- do SDD-26: ledger, banco já sorteado por fase, contadores de eventos.

A gravação segue a política de SDD-14 RF-14.7, e o save no `Hide` é obrigatório (SDD-07 RF-07.3).

## 6. Requisitos não funcionais

- **RNF-25.1:** O `OfflineCalculator` é determinístico: mesma entrada gera a mesma saída (comparada campo a campo), no Linux e no Windows; usa só `+ − × ÷` e tabelas de `PowTable`.
- **RNF-25.2:** O cálculo de 12 h com 2000 ondas leva ≤ 50 ms (meta; medição em CI e Windows).
- **RNF-25.3:** Oculto ou fechado, o app mantém as metas de SDD-07 RNF-07.1 (0 frames e CPU ≤ 0,1%); nenhum trabalho de ganho roda oculto.
- **RNF-25.4:** Em Ausente, o custo médio de CPU a 30 Hz de simulação e 8 fps de render deve ficar ≤ o do cenário de onda ativa de SDD-16 (meta; medição na E6; não comprovada até lá).
- **RNF-25.5:** Cobertura de linhas ≥ 90% em `PresenceTracker`, `AutoScaler` e `OfflineCalculator`.

## 7. Estratégia de implementação

1. `PresenceTracker` e `PresenceMultiplier` com `FakeClock` (Linux).
2. `AutoScaler` puro (ordem, orçamento e custo).
3. `OfflineSnapshot`, `OfflineCalculator` e `OfflineReport`, com tabelas de teste.
4. Integração na `AppStateMachine` (Show/Hide), `RenderPolicy` (8 fps) e farm no chefe.
5. `PendingRewards`, card e badge (com SDD-24), e a pergunta do COLETAR ×2 (com SDD-26).
6. Campos para SDD-14.

## 8. Dependências e decisões pendentes

**Dependências:** SDD-07 (motivos de ocultação, sequência Show/Hide, estados), SDD-27 (economia, torres, ondas), SDD-24 (visual do card e do badge), SDD-26 (pergunta do COLETAR ×2), SDD-22 (render a 8 fps), SDD-10 (RenderPolicy), SDD-14 (campos de persistência).

**Decisões pendentes:**

| ID | Questão | Padrão proposto |
|---|---|---|
| DP-25.1 | "Tempo negativo ou relógio adiantado > 12 h = 0 com log": leitura literal | `elapsed ≤ 0` ou snapshot > 12 h no futuro = 0 com log; `elapsed` > 12 h = teto de 12 h |
| DP-25.2 | Offline < 10 min sem card | ganhos entram direto no saldo |
| DP-25.3 | Presente → Ausente com chefe esperando no portal | cancela o encontro, sem consumir o ledger; o chefe volta ao ficar Presente |
| DP-25.4 | Nenhuma onda vencida no offline | nenhum ganho, com log |
| DP-25.5 | Quedas offline | o modelo não gera quedas (campo reservado) |
| DP-25.6 | EcoQoS em Ausente | ligado |
| DP-25.7 | "COLETAR ×2" dobra créditos e XP | sim |
| DP-25.8 | Tempo de cada onda vencida: duração + 1,4 s (ver DP-27.5) | sim |

## 9. Critérios de aceitação

- **CA-25.1 (T-25.1):** `PresenceTracker`: as transições Presente → Ausente (299 s e 300 s: Presente; 300 s + 1 ms: Ausente) e Ausente → Presente por interação; `Offline` por cada `HiddenReason`; o `Show` automático não conta como interação; `PresentSeconds` zera ao ficar Ausente (FakeClock).
- **CA-25.2 (T-25.2):** `PresenceMultiplier`: ×1,5 em 0–14:59, ×2,0 em 15:00–59:59, ×2,5 em ≥ 60:00 e ×0,25 em Ausente e Offline; zera ao ficar ausente.
- **CA-25.3:** Ausente: 300 s simulados → 9000 ticks, e o `RenderPolicy` cai para 8 fps; no chefe, a onda `w − 1` repete em loop (T-25.3).
- **CA-25.4 (T-25.4):** `AutoScaler`: gasta 50% do saldo; escolhe a torre de menor custo; recalcula o custo a cada melhoria; para quando não cabe; só em Ausente e Offline; respeita o toggle.
- **CA-25.5:** `OfflineCalculator`: casos fixados à mão para (a) 0 s, (b) 5 min, (c) 10 min, (d) 3 h, (e) 12 h, (f) 30 h (teto), (g) tempo negativo (0 com log), (h) parada no chefe, (i) parada na primeira onda não vencida, (j) auto-escala destravando a onda seguinte, (k) limite de 2000 ondas (T-25.5); perdas estimadas de RF-25.8 em T-25.6.
- **CA-25.6 (T-25.5):** `vencida(w)` nas fronteiras `DPS × duração × 0,8` igual a `HPtotal` e logo abaixo.
- **CA-25.7:** Card: aparece em Offline ≥ 10 min (9:59 não, 10:00 sim); COLETAR ×2 com acerto dobra e com erro coleta ×1; ignorado por 60 s recolhe para o badge "+X"; o pendente sobrevive a save e restore; não ativa a janela (T-25.7 para a lógica; T-25.8 no Windows interativo; MTC-25.1).
- **CA-25.8 (T-25.4, T-25.7):** Gasto da auto-escala: saldo nunca negativo; excedente descontado do pendente.
- **CA-25.9 (T-25.7, T-25.9; T-14.8 em SDD-14):** O estado persistido (RF-25.11) faz o ciclo save → restore com hash igual.

Os CAs de medição (RNF-25.2 e RNF-25.4) são cobertos por T-25.10 (SDD-16 P-19 e P-18) e registrados como **medido** ou **reprovado**.

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-25.1 | `PresenceTracker` com `FakeClock` e todas as transições | Linux |
| T-25.2 | `PresenceMultiplier`: tabela de tempos | Linux |
| T-25.3 | Ausente: ticks, render a 8 fps, farm no chefe | Linux |
| T-25.4 | `AutoScaler`: ordem, orçamento, empates | Linux |
| T-25.5 | `OfflineCalculator`: tabela CA-25.5 | Linux |
| T-25.6 | Perdas estimadas (picos, caixas, chefes) | Linux |
| T-25.7 | Pendentes: coleta ×1, ×2, soma de relatórios, persistência | Linux |
| T-25.8 | Card e badge sem ativação de janela | WindowsInteractive |
| T-25.9 | Hide/Show com 3 h simuladas (FakeClock de UTC) e primeiro frame com o estado novo | Linux (lógica) / WindowsInteractive |
| T-25.10 | Benchmarks: `OfflineCalculator` com 12 h e 2000 ondas (RNF-25.2, SDD-16 P-19) e cenário `ausente` (RNF-25.4, SDD-16 P-18), com saída CSV | Linux (indicativo) / WindowsInteractive |
| MTC-25.1 | Bloquear a tela, suspender, fechar e reabrir: o card correto aparece | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Relógio do sistema adiantado ou atrasado gerar ganhos indevidos | Média | Médio | Teto de 12 h; `elapsed ≤ 0` = 0 com log; ganhos ×0,25 |
| O modelo analítico divergir do jogo real (ondas vencidas offline que perderiam vida ao vivo) | Alta | Médio | Fator 0,8 e 10 s de folga; playtest; números em JSON |
| Ausente a 30 Hz custar mais CPU do que o esperado | Média | Médio | Render a 8 fps; EcoQoS; RNF-25.4 medido; ajuste por JSON |
| Interação "no jogo" não detectada (clique que atravessa a janela) | Média | Baixo | Qualquer clique na região interativa e todo atalho contam (SDD-04) |
| Perda de ganhos pendentes por falha de save | Baixa | Alto | Save atômico (SDD-14) no `Hide`, no coletar e no encerramento |
| Card cobrir o trabalho do usuário | Média | Baixo | Recolhe em 60 s para o badge; não ativa a janela (ADR-021) |
