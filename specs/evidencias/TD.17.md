# Evidência TD.17: fase 1 guiada, HUD que ensina, pergunta-bônus V/F e derrota que ensina

Data: 2026-11-17. SDD-28 RF-28.22 (ampliado), RF-28.23 (novo), RF-28.24 (novo); ADR-036; revoga DP-28.5, CA-28.10 e T-28.10.

## Goldens de StateHash TD: antigo → novo (esperado)
Causa: `rules.bonusQuestion` novo em `td-stages.json` (muda o `ContentHash` TD) e o flag "pergunta respondida" no `StateHash`. Goldens idle intactos; simulação inalterada (só os hashes mudam).

| Teste | Antigo | Novo |
|---|---|---|
| TdMatchTests.GoldenReplay | 5049926445164019220 | 16820935973378525438 |
| TdCombatTests (replay de combate) | 10752822863640636477 | 16483169309790164229 |
| TdPlacementTests (layout/colocação) | 1649672874497974502 | 12074968237746493662 |
| TdBombCrateTests (caixa) | 9484780872707564732 | 3885537575890932158 |
| TdSanityBotTests.GoldenOptimizedStage3 | 4040583883875090050 | 11218145559906341500 |

`TdSanityBot`: a curva não quebrou (TdSanityBotTests verdes: fases 1 a 3 com ★2, fase 6 com derrota possível, patentes calibradas). O bot não responde à pergunta-bônus, então a dificuldade medida não muda.

## Goldens de imagem
Novos (revisados visualmente, 100% e 150%): `td-tut-{hudopen,hudcollapse,core,credits,portal,range,hurt,quiz,quizright,quizwrong,quiztime,defeat,chippulse}-*`.
Regenerados: `td-tut-{build,point,tower,wave,ability,crate,help,tip}-*`, `td-ui-hudmin-*` (chip com ícone de expandir e tooltip). Removidos: `td-tut-upgrade*`, `td-tut-upgradeinspector*` (melhorar torre saiu do tutorial por decisão do usuário).

## Verificação
- `scripts/wsl/build.sh Release`: 0 avisos, 0 erros. `scripts/wsl/test.sh`: Core 447, Runtime 777, Persistence 131 (+1 skip), Rendering 612, verdes. `git diff --check` limpo.
- Windows (espelho `C:\dev\sidewar-TD17`): `build.ps1` 0/0; resultado do `test.ps1` no REPORT.
- Testes novos: `TdBonusDefeatTests` (Core, 7), `TdQuizTests` (Runtime, 9), `TdOnboardingTests` reescrito (HUD abrir/recolher/abrir, portão da onda 1, chip pulsante, dano com limite de 15 s, chamar próxima, vender, ≤ 8 palavras com ícone, cartões curtos), `TdOnboardingGoldenTests` (21 cenas × 2 DPIs).
- **Sem validação ao vivo** (o usuário está jogando em `C:\dev\sidewar`; nada foi fechado, aberto ou tocado): cobertura por goldens e testes lógicos. Pontos a observar jogando: posição do balão do chip a 40/88, piscar do chip (1 Hz, poll de 10 Hz) e a pergunta com HUD recolhido.
