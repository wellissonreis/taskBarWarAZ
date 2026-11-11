# TU.1 — integração host + eventos + modo Ausente + regras offline

Data: 2026-11-10. Worker claude2. Branch ficha/TU.1, base 6a71e6a (T6.6B sobre T3.8). Refs: SDD-10 RF-10.5, SDD-25 RF-25.4/25.5/25.8, SDD-26 RF-26.1/26.13, SDD-27 RF-27.9.

## Cherry-picks
- T3.11 549aee4 -> 16c8143; T6.2 a416f81 -> 1b2b578; T6.3 67b8713 -> 16a7363. Sem conflito de texto.
- Conflito semântico (compilação): `AutoUpgrade` definido em AutoScaler.cs (T6.2, por passo) e OfflineModels.cs (T6.3, por torre). A da T6.2 virou `AutoUpgradeStep`; `AutoUpgrade` (relatório) ficou.

## O que foi feito
- **GameHost** (Runtime/Hosting): `StartEvents(ctx)` uma vez, no primeiro wake em Presente (contadores congelam em Ausente, não reiniciam); antes de cada tick escreve `Present`, `PresenceMultiplier` e `IncomePerSecond` (= `RateMeter.IncomeRate`); `RateMeter` alimentado pelos `Kill` do tick (ouro base × Reservas = renda; × presença = exibido) e `Rate.Tick()` por tick; `RunState {NoRun, InStage}` (novo, default InStage): sem tick e comandos rejeitados com `CommandReason.InvalidPhase` (novo, ao fim do enum) fora de InStage.
- **Modo Ausente** (host + Core): `SimState.StartFarmWave(ctx, w)` + `FarmWave` (no hash). Host, entre ticks, com `BossHold` e sem agenda em Ausente, chama `AbsentMode.WaveToPlay` e `StartFarmWave`; fim da onda de farm limpa a agenda e o host repete (loop); em Presente o encontro do chefe começa. `Wave`/`StageWaveMax` não avançam. `AutoScaleAccumulator` avança 1/30 s por tick; cada rodada de 900 s chama `AutoScaler.Run` (toggle `AutoScaleEnabled`, default ligado); `AutoScaleAccumSeconds` get/set para SDD-14; contadores `AutoScaleUpgradeCount/Spent` para o card.
- **Regras únicas**: `AutoScaler.Plan(Span<AutoScaleCandidate>, EconomyDef, budget)` (ordem por custo, empate menor Id, custo recalculado, para ao não caber) é a única implementação; `AutoScaler.Run` (simulação) e `OfflineCalculator.Round` (offline) só montam candidatos. `LevelRules.TryLevelUp/Advance` (Core) é usada por `SimState.LevelSystem` e `OfflineCalculator`.
- Core: `HoldForBoss` agora também retém em Ausente com o ledger respondido (SDD-25 RF-25.4: chefe nunca começa em Ausente); queda durante o farm solta a retenção e recua.

## Divergências de regra encontradas
1. XP/nível: mesma fórmula (`xp ≥ nível × 45`) nos dois; diferença só no carry: simulação guarda `XpCarry` fracionário, offline descarta a fração (`floor`) e o snapshot não tem XpCarry. Mantido (SDD-25 RF-25.8 não especifica carry de XP); registrado.
2. Auto-escala: regra idêntica (50% floor, menor custo, empate menor Id). Offline tem `AutoScaleBudgetFraction` configurável; `AutoScaler.Budget(credits, fraction)` preserva (0,5 = `credits/2`). O offline considera só torres com slot e desconta o excedente do pendente (CA-25.8, fora desta ficha).
3. Chefe com ledger respondido em Ausente: T6.6B liberava direto (jogaria o chefe em Ausente), contra SDD-25 RF-25.4; seguido SDD (retenção + farm).
4. Farm ao voltar a Presente: a onda de farm em curso termina antes do encontro começar (SDD diz "ao voltar a Presente, o encontro começa"; sem prazo). Decisão minimalista, registrada.

## Goldens (SimHashTests) atualizados — `FarmWave` entrou no hash
- inicial 14921813339393961454 -> 887695674483967726
- roteiro 900 ticks 13845706898258846089 -> 10744328335295700329

## Comandos e resultados
```text
scripts/wsl/build.sh Release -> 0 Warning(s), 0 Error(s)
scripts/wsl/test.sh          -> Core 239, Runtime 311, Persistence 35, Rendering 100, todos passando
git diff --check             -> sem saída
```

## Medido
Testes novos (Core `SimFarmWaveTests` 4; Runtime `GameHostIntegrationTests` 7): StartEvents/Present/congelamento, RunState (tick e comando InvalidPhase), IncomePerSecond do RateMeter, auto-escala em 900 s (não antes; zera em Presente; toggle), início do farm pelo host, loop e Presente após farm, queda no farm, LevelRules, paridade offline × simulação do gasto, dos níveis e do saldo da auto-escala.

## NÃO medido
Windows/GameThread; jitter; persistência SDD-14 (FarmWave, AutoScaleAccumSeconds não são salvos ainda); paridade de XP offline × simulação ponta a ponta; custo de CPU do Ausente; COLETAR ×2; missões; HudModel; cobertura.

## Para T4.10/App
- Construir `GameHost` como antes; chamar `host.InteractionObserved()` em cliques/hotkeys (é o que liga Presente e StartEvents).
- Persistir/restaurar `host.AutoScaleAccumSeconds`; `host.AutoScaleEnabled` vem do Arsenal; `host.RunState` = NoRun até existir partida.
- HUD: `host.Rate.DisplayedRate` ("+N/s"); render do farm: `State.FarmWave > 0` (onda exibida pode ser w−1 enquanto `State.Wave` é a do chefe).
- SDD-14: salvar `FarmWave` não é necessário (a retenção é reavaliada), mas o restore deve chamar `RestoreEvents` e deixar o host recomeçar o farm.
