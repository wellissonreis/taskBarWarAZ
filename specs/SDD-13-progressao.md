# SDD-13: Progressão: economia, experiência, fases, estrelas e checkpoint

**Status:** Rascunho para revisão; **parcialmente substituída por SDD-27 (ADR-019)** · **Etapa:** E6 (o stub da economia entra na E3) · **Depende de:** SDD-11, SDD-12, SDD-27 (economia, modos, catálogo, estrelas, checkpoint e seed), SDD-25 (multiplicador de presença) · **Bloqueia:** SDD-14 (perfil), SDD-15 (SDD-24 para o painel de Fases)

> **Substituída em grande parte por SDD-27 (ADR-019) e SDD-25 RF-25.3.** A economia, os modos, o catálogo numérico, as estrelas, o checkpoint anti-farm e a seed deixam de valer (RF-13.1, 13.2, 13.5, 13.6, 13.7 e a parte numérica de 13.3). **Continua valendo:** os temas AZ-900 por fase, que viram o campo `stageTopic` de `stages.json` (RF-13.3), e a ideia de `Profile` persistente (RF-13.4, ajustado). O texto original é mantido como histórico.

## 1. Identificador e título

SDD-13: Regras de ouro, XP e nível dentro da partida; perfil persistente; modos Clássico e Campanha; desbloqueio; estrelas e recordes; checkpoint anti-farm.

> **Substituído por SDD-27 (ADR-019):** não há modos Clássico/Campanha, nem checkpoint anti-farm; a economia é a de créditos/XP/Reservas de SDD-27 e o único conteúdo remanescente desta ficha é o `stageTopic` de cada fase.

## 2. Objetivo

Dar um senso de avanço de longo prazo coerente com um jogo de overlay, com sessões curtas e retomáveis, e reaproveitar as regras comprovadas da referência sem os acoplamentos dela.

## 3. Contexto técnico

- Regras da referência: ANALISE §3.
- As perguntas antes dos chefes e as estrelas ligadas a elas dependem de D-01 (SDD-19). Esta ficha define a regra de estrelas **sem** o módulo e o ponto de extensão para o módulo.
- A duração das fases (60 a 100 ondas) depende de D-08 e fica parametrizada.
- **Atualização (ADR-019, ADR-024):** D-01 está resolvido (perguntas e picos são núcleo, SDD-26) e D-08 foi substituído pelas fases idle de SDD-27 RF-27.9; a regra de estrelas passa a usar o ledger de SDD-26 (SDD-27 RF-27.9). Estas referências abaixo são históricas.

## 4. Escopo e exclusões

**Escopo:** `Economy` (dentro de Core), `StageCatalog` (`stages.json`), `Profile`, `RunCoordinator` (Runtime: iniciar, abortar, repetir, avançar), regras de estrelas, recordes e checkpoint.

**Exclusões:** formato de arquivo (SDD-14), telas (SDD-15) e perguntas (SDD-19).

## 5. Requisitos funcionais

### RF-13.1: Economia na partida (`Economy`, implementa `IEconomy` da SDD-11)

> **Substituído por SDD-27 RF-27.1, RF-27.5, RF-27.6, RF-27.8 e RF-27.9 (ADR-019) e por SDD-27 RF-27.12 / SDD-25 RF-25.3 para os multiplicadores.** Créditos, XP e Reservas são `long` com carry fracionário; não há `gold` inicial por modo, bônus de fim de onda, fase `Build`/`Victory`, `+20` créditos por level-up nem `rewardMult`/`leakMult` por fase. Permanece a fórmula de XP `xp ≥ nível × 45` (SDD-27 RF-27.9) e o HP do núcleo cresce com o nível (SDD-27 RF-27.8: 100 + 5 por nível). O texto abaixo é histórico.

| Evento | Regra |
|---|---|
| Início da partida | `gold = startGold` (Clássico 140; fase: `stages[i].gold`); `baseHp = maxBaseHp = 100`; `xp` e `level` vêm do perfil (Campanha) ou começam em 0/1 (Clássico) |
| `OnKill(kind)` | `gold += floor(reward(kind) · rewardMult)`; `xp += xp(kind)` |
| `OnLeak(kind)` | `baseHp = max(0, baseHp − max(1, round(leak(kind) · leakMult)))` |
| `CheckLevelUp()` | enquanto `xp ≥ level·45`: `xp −= level·45`; `level += 1`; `gold += 20`; `maxBaseHp += 5`; `baseHp = min(maxBaseHp, baseHp + 5)`; emitir `LevelUp` |
| `FinishWave()` com `wave < total` | bônus: Clássico `18 + 3·wave`; Longo `floor(12 + 5·√wave)` (`√` calculado no fim da onda e quantizado); `phase = Build`; emitir `WaveCleared(wave, bonus)` |
| `FinishWave()` com `wave == total` | sem bônus; `phase = Victory`; emitir `Victory` |

`rewardMult = 1` e `leakMult = 1` no MVP. Os campos existem para ajustes por fase.

### RF-13.2: Modos de jogo

> **Substituído por SDD-27 RF-27.9 (ADR-019).** Os modos **Clássico** e **Campanha** deixam de existir: há uma única partida idle contínua, com ondas automáticas e progressão entre as 10 fases. O recorde do Clássico e o algoritmo Clássico/Longo de SDD-12 são obsoletos (SDD-27 RF-27.14/27.15).

| Modo | Conteúdo | Progressão |
|---|---|---|
| **Clássico** (primeiro jogável, E3/E4) | 10 ondas, ouro inicial 140, chefe na onda 10, algoritmo Clássico (SDD-12) | Grava apenas o recorde do Clássico (melhor onda alcançada e vitórias). Não altera o XP do perfil |
| **Campanha** (E6) | Fases de `stages.json`, algoritmo Longo | XP e nível no perfil, estrelas, desbloqueio |

### RF-13.3: Catálogo `stages.json`

> **Substituído por SDD-27 RF-27.9 (ADR-019), exceto os temas AZ-900.** A tabela de ondas/ouro/escalas abaixo é histórica: o catálogo vigente é o de SDD-27 RF-27.9 (60/60/60/80/80/80/80/100/100/100 ondas, sem `gold`, `hpScale`, `countScale`, `hpGrowth`, `speedGrowth`, `spawnIntervalFactor`, `bossWaves` derivado nem `azureModule`). **Mantido:** a coluna "título" desta tabela vira o campo `stageTopic` (Computação em nuvem, Modelos de implantação, IaaS, PaaS, SaaS, Responsabilidade compartilhada, Consumo, Escala vertical, Escala horizontal, Elasticidade), usado para escolher o banco de perguntas (SDD-26 RF-26.5); os títulos exibidos são os de SDD-27 RF-27.9 (08 a 10 provisórios, D-14).

Gerado pela fórmula da referência (`campaign.ts:223-246`) e **escrito explicitamente** no JSON, sem fórmula em tempo de execução:

| id | título | ondas | gold | hpScale | countScale | hpGrowth | speedGrowth | spawnIntervalFactor |
|---|---|---|---|---|---|---|---|---|
| 1 | Computação em nuvem | 60 | 150 | 1,00 | 1,25 | 26,0 | 0,40 | 1,000 |
| 2 | Modelos de implantação | 60 | 158 | 1,05 | 1,32 | 27,5 | 0,43 | 0,985 |
| 3 | IaaS | 60 | 166 | 1,10 | 1,39 | 29,0 | 0,46 | 0,970 |
| 4 | PaaS | 80 | 174 | 1,15 | 1,46 | 30,5 | 0,49 | 0,955 |
| 5 | SaaS | 80 | 182 | 1,20 | 1,53 | 32,0 | 0,52 | 0,940 |
| 6 | Responsabilidade compartilhada | 80 | 190 | 1,25 | 1,60 | 33,5 | 0,55 | 0,925 |
| 7 | Consumo | 80 | 198 | 1,30 | 1,67 | 35,0 | 0,58 | 0,910 |
| 8 | Escala vertical | 100 | 206 | 1,35 | 1,74 | 36,5 | 0,61 | 0,895 |
| 9 | Escala horizontal | 100 | 214 | 1,40 | 1,81 | 38,0 | 0,64 | 0,880 |
| 10 | Elasticidade | 100 | 222 | 1,45 | 1,88 | 39,5 | 0,67 | 0,865 |

- Todos têm `countBonus = 0` e `bossWaves` derivado por `bossWavesFor(waves)` (SDD-12), gravado no JSON para ficar explícito. Cada fase no JSON tem também os campos `bossWaves` e `azureModule` (padrão `false`).
- Os títulos mantêm o tema Azure apenas como nomes de fase. Se D-01 for "remover o tema", trocar só os títulos. *(D-01 resolvido por ADR-024: o tema Azure fica como `stageTopic`.)*

### RF-13.4: Perfil (`Profile`, persistido em SDD-14)

> **Ajustado por SDD-27 RF-27.9/27.10/27.11 e SDD-25/26 (ADR-019).** O `Profile` continua existindo, mas sem `ClassicRecord`, sem `Wins`/`Attempts`/`BestHpRatio`/`BestWave` por derrota e sem desbloqueio por `Completed` isolado: concluir a fase desbloqueia a próxima dentro da mesma partida (SDD-27 RF-27.9). Passa a conter também Reservas, regiões desbloqueadas, torres desbloqueadas, missões do dia, contadores de eventos, ledger e perguntas sorteadas (lista em SDD-14, a partir de SDD-25 RF-25.11). `Concepts` (SDD-19 RF-19.8) foi removido (SDD-26 DP-26.6). O texto abaixo é histórico.

```
Profile { int Xp; int Level; Dictionary<int, StageRecord> Stages; ClassicRecord Classic; int TotalKills; long PlaySeconds;
  Dictionary<string,int[]> Concepts /* usado só por SDD-19 */ }
StageRecord { int Stars; int Wins; int Attempts; float BestHpRatio; bool Completed; int BestWave }
ClassicRecord { int Wins; int BestWave; float BestHpRatio }
```

- Desbloqueio: a fase 1 está sempre disponível. A fase `n` fica disponível se `Stages[n−1].Completed`.

### RF-13.5: Estrelas (sem o módulo de perguntas)

> **Substituído por SDD-27 RF-27.9 (ADR-019, ADR-024).** Estrelas: 1 = fase concluída; 2 = ≥ 2 perguntas de chefe acertadas; 3 = todas acertadas e nenhuma queda do núcleo (ledger de SDD-26). Não há mais `hpRatio` de fim de partida. `IStarRule` deixa de ser ponto de extensão do módulo (SDD-19 RF-19.9 também é substituído).

`IStarRule.Stars(StarContext ctx)`, com `ctx` = `won`, `hpRatio = baseHp / maxBaseHp` no fim e evidências opcionais do módulo (SDD-19):

- derrota: 0;
- vitória: 1;
- vitória com `hpRatio ≥ 0,60`: 2;
- vitória com `hpRatio ≥ 0,90`: 3.

O recorde guarda o **máximo** e nunca diminui. **Ponto de extensão:** `IStarRule`. O módulo SDD-19, quando ativo, substitui a regra pela da referência (perguntas).

### RF-13.6: Checkpoint anti-farm (`RunCoordinator`)

> **Substituído por SDD-27 RF-27.9 (ADR-019).** Não existe derrota de fase, `Result`, REPETIR, PRÓXIMA, abandono ou checkpoint: o XP ganho entra direto no nível, e o "farm" é o comportamento normal do idle. O prestígio "Migrar" (SDD-27 RF-27.10) cumpre o papel de nova passagem.

- **Iniciar fase:** criar o `Checkpoint { ProfileXp, ProfileLevel, StageId, Seed }` e `Attempts += 1`.
- Durante a fase, o XP ganho vive **somente** na `Simulation`. O perfil não muda.
- **Vitória:**
  - `Profile.Xp/Level = sim.Xp/Level`;
  - atualizar `StageRecord` (`Wins += 1`, `Completed = true`, `Stars = max`, `BestHpRatio = max`, `BestWave = total`);
  - `TotalKills` e `PlaySeconds` somados;
  - salvar o perfil e apagar a sessão.
- **Derrota ou abandono:**
  - o perfil volta ao checkpoint (na prática, nada a desfazer, porque o perfil não mudou);
  - atualizar apenas `BestWave = max` e `TotalKills` e `PlaySeconds`;
  - apagar a sessão.
- **REPETIR (após o resultado):** nova tentativa com novo checkpoint, a partir do perfil atual.
- **PRÓXIMA:** apenas após vitória e se houver a próxima fase.
- **Retomar sessão salva:** restaura a simulação e o checkpoint (SDD-14). As regras acima seguem valendo.

### RF-13.7: Seed

> **Substituído por SDD-27 RF-27.9 (ADR-019).** A seed do `Rng` é gerada na primeira partida, persistida (SDD-14) e nunca reiniciada; `hash(StageId, Attempts)` deixa de ser usado.

- `Seed = (uint)hash(StageId, Attempts)` no MVP. Determinístico e testável.

## 6. Requisitos não funcionais

- **RNF-13.1:** Regras puras em Core e Runtime, testáveis no Linux, com cobertura ≥ 90% em `Economy` e `RunCoordinator`.
- **RNF-13.2:** O perfil não muda durante a partida, exceto no fim (atomicidade conceitual).

## 7. Estratégia de implementação

1. E3: `Economy` completa (já usada pelo combate) e o modo Clássico.
2. E6: `stages.json`, `Profile`, `RunCoordinator`, regra de estrelas, integração com SDD-14 e as telas de SDD-15.

## 8. Dependências

SDD-11, SDD-12 e **SDD-27** (economia, fases, estrelas e prestígio; substitui o conteúdo desta ficha), com SDD-25 (multiplicador de presença) e SDD-26 (ledger de perguntas). Os consumidores são SDD-14 (campos persistidos) e SDD-15/SDD-24 (painel de Fases). SDD-19 foi substituída (ADR-024), então não traz mais extensão de estrelas.

## 9. Critérios de aceitação

> **CA-13.1 a CA-13.3 substituídos por SDD-27 (CA-27.x; T-27.3, T-27.8, T-27.9, T-27.13).** Permanecem como histórico.

- **CA-13.1:** Os testes da economia (cada linha de RF-13.1, com múltiplos level-ups num único kill) passam.
- **CA-13.2:** Os testes do `RunCoordinator`:
  - vitória consolida o XP e o recorde;
  - derrota não altera o XP;
  - REPETIR após vitória parte do XP consolidado;
  - estrelas nunca diminuem.
- **CA-13.3:** `stages.json` bate com a tabela de RF-13.3 (teste de tabela).

## 10. Cenários de teste

> **T-13.1 a T-13.4 substituídos:** economia e XP → T-27.3/T-27.9; `RunCoordinator`, estrelas e catálogo → T-27.8 e T-27.13. Permanecem como histórico.

| ID | Cenário |
|---|---|
| T-13.1 | Economia: kill, leak, level-up (incluindo 2 níveis de uma vez), bônus Clássico e Longo |
| T-13.2 | `RunCoordinator`: vitória, derrota, abandono, repetir, próxima, desbloqueio |
| T-13.3 | Estrelas: fronteiras 0,599/0,60/0,899/0,90 |
| T-13.4 | Catálogo: valores e `bossWaves` |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Fases de 60 a 100 ondas longas demais para o hábito de jogo em overlay | Alta | Médio | Save mid-wave (SDD-14); D-08; modo Clássico curto *(histórico: o idle de SDD-27 tem ondas automáticas e offline analítico, SDD-25)* |
| Regra de estrelas sem perguntas parecer rasa | Média | Baixo | Extensão via `IStarRule` (SDD-19) |
