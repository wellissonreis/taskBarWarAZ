# SDD-10: Game loop, relógio, política de render e máquinas de estado do jogo

**Status:** Rascunho para revisão · **Etapa:** E1 (laço + política) e E3 (integração com a simulação) · **Depende de:** SDD-01, SDD-03, SDD-25, SDD-27 · **Bloqueia:** SDD-07, 11, 13, 15, 16, 22, 25, 26

> **Revisada (rev2, ADR-019, ADR-022, ADR-026, ADR-027).** Fases `Build`/`Victory`/`Defeat`, `StartWave` e `RunState.Result` foram substituídos por **ondas contínuas automáticas** (SDD-27 RF-27.7); `RunState` e `PauseReasons` seguem SDD-25/26; a `RenderPolicy` usa a presença (presente 30 fps, ausente 8 fps, offline 0; SDD-22 RF-22.10, SDD-25). O passo fixo de 30 Hz foi mantido.

## 1. Identificador e título

SDD-10: `GameHost`, passo fixo, `RenderPolicy`, `PauseReasons` e estados de partida e de tela.

## 2. Objetivo

Coordenar quando a simulação avança e quando o frame é desenhado, com custo mínimo e resultado determinístico. Isso inclui suspender totalmente quando não há o que fazer.

## 3. Contexto técnico

- ADR-004: 30 Hz fixos, sem interpolação (mantido em Ausente, SDD-25 RF-25.4). ADR-019 (idle, ondas automáticas), ADR-022 (offline analítico: oculto, fechado ou em sono nada roda) e ADR-026 (layout da faixa é estado da simulação e muda só por comando). A referência usava 1/60 com `dt` limitado a 0,25 s (`game.ts:534`).
- O laço físico (espera, mensagens) é o da GameThread (SDD-03). Esta ficha define o **laço lógico** `GameHost.OnWake(now)` e o cálculo de `NextDeadline`.
- O `IClock` usa `Stopwatch.GetTimestamp()` no Windows e `FakeClock` nos testes.

## 4. Escopo e exclusões

**Escopo:** `GameHost`, `FixedStepper`, `RenderPolicy`, `PauseReasons`, `RunState` (sem partida, em partida), fases da partida (`Wave` contínua e `StageCleared`), velocidade única (sem 2×; ADR-031), relógio de apresentação e despacho de comandos (inclui `LayoutChanged`).

**Exclusões:** regras de combate (SDD-11), conteúdo e economia (SDD-12, SDD-27), progressão (SDD-27; SDD-13 substituída), presença e offline (SDD-25), eventos ao vivo (SDD-26) e estados de visibilidade (SDD-07).

## 5. Requisitos funcionais

### RF-10.1: Constantes

```csharp
public static class LoopConstants {
    public const int SimHz = 30;                       // ticks por segundo de jogo
    public const double TickSeconds = 1.0 / SimHz;
    public const double MaxAccumulatedSeconds = 0.25;  // acumulador máximo (anti spiral of death)
    public const int AmbientFps = 8;                   // fps de render em Ausente (SDD-22 RF-22.10, SDD-25 RF-25.4)
    public const int DefaultActiveFps = 30;            // fps de render em Presente (SDD-22 RF-22.10); a lista 20 | 30 | 60 ficou pendente
    public const int SaverActiveFps = 15;
    // DragFps removido: não há arraste de torre (SDD-22 RF-22.3); clique em torre/slot abre o Arsenal
}
```

### RF-10.2: Estados da partida (Core)

> **Substituído por SDD-27 RF-27.7 e RF-27.9 (ADR-019).** Não há `Build`, `Victory`, `Defeat` nem comando `StartWave`: a próxima onda começa sozinha 1,4 s após limpar; a queda do núcleo recua a partida (SDD-27 RF-27.8) em vez de terminá-la; concluir a fase continua na seguinte. O texto original abaixo é histórico; vale o que está em "Estados vigentes".

**Estados vigentes:** `enum MatchPhase { Wave, StageCleared }`. `Wave` inclui o intervalo de 1,4 s entre ondas. `StageCleared` é o estado terminal transitório de "fase concluída".

| De | Para | Condição |
|---|---|---|
| Wave | Wave | fila vazia e sem inimigos em campo, com descanso acumulado ≥ 1,4 s (SDD-27 RF-27.7): começa a onda `w + 1` (o chefe de cada 20 ondas obedece SDD-26 em Presente e SDD-25 em Ausente) |
| Wave | Wave | `baseHp ≤ 0` (queda): onda atual abandonada, núcleo restaurado e recuo de 5 ondas (SDD-27 RF-27.8); sem perda de créditos |
| Wave | StageCleared | a última onda da fase (sempre de chefe) é vencida |
| StageCleared | Wave | o Runtime inicia a fase seguinte, com torres e créditos mantidos e onda 1 (SDD-27 RF-27.9); na fase 10, a partida fica em farm da última onda |

**Histórico:**

```
enum MatchPhase { Build, Wave, Victory, Defeat }
```

| De | Para | Condição |
|---|---|---|
| Build | Wave | comando `StartWave` válido (não pausado, `wave < totalWaves`) |
| Wave | Build | fila vazia, sem inimigos e sem projéteis, e descanso acumulado ≥ 1,4 s (SDD-11) com `wave < totalWaves` |
| Wave | Victory | mesma condição com `wave == totalWaves` |
| Wave | Defeat | `baseHp ≤ 0` |
| Victory/Defeat | — | terminal; o Runtime decide o próximo `RunState` |

### RF-10.3: Estado de execução (Runtime)

> **Substituído por SDD-27 RF-27.9 (ADR-019).** `Result` deixa de existir (sem derrota de fase, REPETIR, PRÓXIMA ou MAPA); a escolha de modo e o botão "▶ JOGAR" saem junto com os modos Clássico e de ondas manuais. `RunState` vigente: `{ NoRun, InStage }`, em que `NoRun` é só o estado transitório até o Runtime criar ou restaurar a partida (a partida começa **automaticamente na primeira execução**, sem ação do usuário, e o restore após offline retoma sem pausa; ADR-031). O texto abaixo é histórico.

> **Ajustado por ADR-031.** `NoRun` é transitório; não há botão "▶ JOGAR".

```
enum RunState { NoRun, InStage, Result }
```

- `NoRun`: sem partida. O overlay mostra a faixa com o cenário estático e a HUD com o botão "▶ JOGAR" (abre a escolha de modo, SDD-15).
- `InStage`: existe uma `Simulation` ativa.
- `Result`: a partida terminou. A HUD mostra o resumo e as ações REPETIR, PRÓXIMA (se vitória e houver próxima) e MAPA.

### RF-10.4: Motivos de pausa

> **Substituído em parte por SDD-25 RF-25.2 (ADR-022).** `Idle` e `Question` removidos; `Hidden` pausa sempre e retoma sozinho; presença não é pausa.

- `[Flags] PauseReason { None=0, User=1, Hidden=2, Dialog=16 }`, mantido no `GameHost`. `Idle=4` e `Question=8` foram **removidos** (SDD-25 RF-25.2): a ausência não pausa (é o modo gerenciado, a 30 Hz) e a pergunta de chefe não pausa (SDD-26). Os valores 4 e 8 ficam reservados. `Hidden` pausa **sempre** (nada roda oculto, ADR-022; o ganho vem do cálculo offline, SDD-25 RF-25.6). **Não** é serializado: o save **não** grava `userPaused` e o restore **retoma sem pausa** (SDD-14 RF-14.8; ADR-031). Também o `User` (menu Pausar/Retomar, SDD-15) vale só na sessão. Ao exibir, o `Hidden` **não** vira `User`: o jogo retoma sozinho (SDD-25 RF-25.2).
- A simulação avança somente se `RunState == InStage && PauseReasons == None && phase == Wave`. O estado de presença (SDD-25) **não** é motivo de pausa: em Ausente a simulação real continua.
- Não existe fase `Build`: o tick de simulação roda em toda a `Wave` (inclusive no intervalo de 1,4 s) e os comandos são aplicados no início do `OnWake`, **antes** dos ticks (RF-10.6).

### RF-10.5: `FixedStepper`

```
OnWake(now):
  dt = clamp((now − lastNow) em s, 0, MaxAccumulatedSeconds); lastNow = now
  if simRunning: acc += dt
  ticks = 0
  maxTicks = 8                                        # 8 ticks por wake (ADR-031: sem velocidade 2×)
  while acc >= TickSeconds and ticks < maxTicks:
      sim.Tick(); acc -= TickSeconds; ticks++
  if ticks == maxTicks: acc = 0                       # descarta excesso (sem catch-up)
```

- Ao retomar de qualquer pausa ou ocultação: `lastNow = now; acc = 0`.

### RF-10.6: Despacho de comandos

> **Estendido por SDD-11 RF-11.3 e RF-11.10 (ADR-026):** comando `LayoutChanged` aplicado entre ticks; comandos de SDD-26/27.

- No início de cada `OnWake`, drenar a fila (SDD-01, RF-01.4) e aplicar os comandos em ordem com `sim.Apply(cmd) → CommandResult`.
- O resultado volta para a entrada e a HUD (feedback visual, SDD-15) via `CommandFeedbackQueue`.
- Comandos de UI pura (abrir o Arsenal ou o painel, abrir menu) **não** passam pela simulação. São tratados no Runtime (`UiState`).
- **`LayoutChanged` (ADR-026):** o layout da faixa (`SpawnX`, `LeakX`, posições dos slots, núcleo e portal; SDD-11 RF-11.3) é estado da simulação e **só muda** por este comando. O Runtime o cria a partir de `TaskbarLayoutChanged` (SDD-21) e do `SceneLayout` (SDD-22), e o `GameHost` o aplica no início do `OnWake`, **entre ticks**, nunca no meio de um tick. Vai para o `CommandLog` (quando ligado), para o save e para o `StateHash`.
- Os comandos de SDD-26 RF-26.15 (`ClickPeakBanner`, `OpenBossQuestion`, `AnswerBossQuestion`, `ClickSupplyCrate`) e de SDD-27 RF-27.5 (`BuildTower(type, slot)`, `UpgradeTower`, `SetTargeting`) passam por este mesmo despacho. O cálculo offline (SDD-25 RF-25.10) é aplicado pelo Runtime no `Show`, antes do primeiro frame, e não é comando.
- Para determinismo e replay, todo comando aplicado à simulação é registrado em `CommandLog` (anel de 4096 entradas, com tick) **somente** quando `settings.diagnostics.recordCommands` está ligado (padrão desligado; usado nos testes).

### RF-10.7: `RenderPolicy` (puro)

> **Substituído em parte por SDD-22 RF-22.10 e SDD-25 RF-25.2 (ADR-019, ADR-022, ADR-027).** Fps por presença: **Presente 30, Ausente 8, Offline 0** (Saver: `min(fps, 15)`); sai o arraste (`dragActive`) e o `userIdle` vira `presence`. A tabela abaixo foi atualizada; a regra "`!visible` → 0" e o cálculo de `renderNow`/`nextDeadline` continuam.

Entrada:

```
RenderInputs(
  bool visible, bool simRunning, bool animationsEnabled /* !reducedMotion && !Saver */,
  PresenceState presence /* Presente | Ausente | Offline, SDD-25 */, bool dirty, int activeFps /* 30 */, PowerProfile profile, double now, double lastPresent)
```

Saída: `RenderDecision(bool renderNow, double? nextDeadline)`.

| Condição (avaliada em ordem) | fps alvo | Comportamento |
|---|---|---|
| `!visible` ou `presence == Offline` | 0 | `renderNow=false`, sem deadline de render (0 frames, 0 wakes; SDD-22 RF-22.10) |
| `simRunning && presence == Ausente` | `AmbientFps` (8; Saver: `min(8, 15)`) | render a cada `1/8` s; a simulação continua a 30 Hz; cena a 50% por `SetOpacity` (SDD-22 RF-22.7) |
| `simRunning && presence == Presente` | `profile==Saver ? 15 : activeFps` (30) | render a cada `1/fps` |
| demais (jogo pausado por `User` ou `Dialog`) | 0 | **por evento**: render somente se `dirty`, sem deadline |

- `renderNow = dirty || (fps > 0 && now − lastPresent ≥ 1/fps − 0,5 ms)`. Um `renderNow` sem mudança visual não apresenta nada: o frame é **ignorado** (SDD-08 RF-08.7), o que garante 0 Presents com `reducedMotion` e sem entidades em movimento (SDD-22 RNF-22.6).
- A `RenderPolicy` descrita aqui rege a **cena**. Widget, painel e cards renderizam **por evento** (SDD-08 RF-08.10, SDD-23 RF-23.9) e banners e caixas por deadlines discretos (SDD-22 RF-22.10); todos seguem `!visible`/`Offline` → 0.
- `nextDeadline = fps > 0 ? lastPresent + 1/fps : null`. O `GameHost.NextDeadline` é o mínimo entre os deadlines de render, de simulação (próximo tick, se `simRunning`), do polling (SDD-04/06/07) e de debounce (SDD-05).
- Em `simRunning` com `activeFps < SimHz`, a simulação continua a 30 Hz e o render pula frames. Com 60 fps, há 2 renders por tick (o mesmo estado).

### RF-10.8: Relógios

| Relógio | Avança quando | Uso |
|---|---|---|
| `SimTime` (ticks inteiros) | tick de simulação | toda a lógica de jogo |
| `PresentationTime` (s) | `!paused` e visível, inclusive em Ausente | bob, números flutuantes, piscar e pulso (SDD-22) |
| `WallClock` (`IClock`) | sempre | deadlines, presença (SDD-25), histerese |

### RF-10.9: Velocidade

> **Ajustado por ADR-031.** Não existe velocidade 2× nem comando de velocidade: a simulação roda sempre a 1×. O campo de velocidade sai do save.

### RF-10.10: Sujeira ("dirty") vinda da lógica

- O host marca `dirty=true` quando:
  - um comando é aplicado com `Ok`;
  - a fase ou a onda mudam;
  - o `HudModel.Version` (widget, SDD-23) muda;
  - o modo (Passivo/Interativo) ou o estado de borda (presença, chefe) mudam;
  - o layout muda (`LayoutChanged` aplicado);
  - o estado de presença muda (SDD-25) ou o selo de ausente atualiza (a cada minuto);
  - um banner, uma caixa ou um número flutuante muda de estado discreto (SDD-22 RF-22.10).
- Durante `simRunning`, o render ocorre de qualquer forma pelo fps.

## 6. Requisitos não funcionais

- **RNF-10.1:** `OnWake` sem trabalho (nada a fazer) leva ≤ 20 µs.
- **RNF-10.2:** Determinismo: dados `seed`, conteúdo e `CommandLog` (inclui `LayoutChanged`), o hash do estado (inclui o layout, SDD-11 RF-11.12) após N ticks é idêntico em execuções, em Linux e Windows, e entre x64 e arm64 (SDD-11 exige aritmética determinística, RF-11.12).
- **RNF-10.3:** Jitter do tick: p99 do intervalo entre ticks ≤ 40 ms com o timer de alta resolução (meta; Windows).

## 7. Estratégia de implementação

1. `FixedStepper`, `RenderPolicy` e `PauseReasons` com testes de tabela (Linux).
2. `GameHost` com uma simulação falsa (E1), contando ticks e renders.
3. Integração com a `Simulation` real (E3/E4).

## 8. Dependências

SDD-01 e SDD-03. SDD-07 fornece os sinais de visibilidade, inatividade e energia. SDD-11 fornece a `Simulation`.

## 9. Critérios de aceitação

- **CA-10.1:** A tabela da `RenderPolicy` (todas as combinações de booleanos × perfis) passa (Linux).
- **CA-10.2:** Com FakeClock, 10 s simulados produzem exatamente 300 ticks. Um salto de relógio de 5 s produz no máximo 8 ticks e zera o acumulador.
- **CA-10.3:** Com o jogo pausado (`User`) ou com `reducedMotion` e sem entidades em movimento, 0 Presents em 30 s sem eventos (teste com FakeClock + contagem). Em Presente a cena renderiza a 30 fps, em Ausente a 8 fps e em Offline 0 (T-10.1).
- **CA-10.4 (Windows):** Medição do jitter (RNF-10.3) registrada na evidência E1, com o rótulo **medido** e o valor.

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-10.1 | Tabela `RenderPolicy` | Linux |
| T-10.2 | `FixedStepper`: velocidade, salto, retomada | Linux |
| T-10.3 | Pausa por múltiplos motivos (remover um mantém pausado) | Linux |
| T-10.4 | Comandos aplicados no início do `OnWake`, antes dos ticks; `LayoutChanged` aplicado entre ticks e presente no `CommandLog` e no hash | Linux |
| T-10.5 | `NextDeadline` mínimo entre fontes | Linux |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| 30 Hz deixar o movimento "travado" a 60 fps | Média | Baixo | 30 fps é o padrão; interpolação como melhoria pós-MVP com ADR |
| Projéteis rápidos "atravessarem" o alvo a 30 Hz | — | — | **Obsoleto (ADR-027):** acerto instantâneo, sem projéteis (SDD-11 RF-11.4) |
| `LayoutChanged` frequente (UIA, DPI) alterar o estado no meio de uma onda | Média | Médio | Aplicado só entre ticks; entra no hash; regras de ajuste em SDD-11 RF-11.3 (DP-11.1) |
| Deadlines concorrentes gerarem wakes excessivos | Média | Médio | `NextDeadline` mínimo + alinhamento dos polls a múltiplos de 250 ms |
