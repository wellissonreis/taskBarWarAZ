# TD.19: conteúdo da página de runas e tabelas de custo (SDD-29)

Data: 2026-11-17. Escopo: só conteúdo e validador do Core. Sem mudança de comportamento da simulação.

## O que entrou
- `src/SideWar.Core/Content/td-runes.json`: nó central + 6 setores (Raio, Gelo, Canhão, Balanceador, Scale Set, Geral) com 9 nós cada (6 pequenos, 2 notáveis, 1 keystone) + 6 notáveis de fronteira = **61 nós**. Custos em créditos 40 / 120 / 120 / 200 (setor completo 680, página 4.800).
- Estrutura do setor: `core ─ s1`; `s1 ─ s2 ─ n1 ─ s3 ─ k`; `s1 ─ s4 ─ n2 ─ s5 ─ k`; `n2 ─ s6`. Fronteira `i` liga `n2` do setor `i` a `n1` do setor `i+1` (anel fechado). Efeitos em pontos-base inteiros; chaves reaproveitadas dos ramos/capstones.
- `td-stages.json` `rules.towerCostTable` e `towerCostTableLow`: `round(100 × 1,25^n)` e `round(100 × 1,20^n)`, n = 0..47 (conferem com a §5.1 até n = 12).
- Modelos `TdRunesDef` etc., `TdContent.Runes`, loader (`td-runes.json` entra no `ContentHash`) e `TdContentValidator.Runes/CostTable`: grafo conexo a partir do centro, 61 nós, custos por tipo, texto ≤ 6 palavras (notável/keystone/fronteira), fronteira liga 2 setores distintos, **anti-exploit** `descontoMáximoDeCompra (500 bp) + tetoDeVenda (8.500 bp) < 10.000 bp`.
- `creditSources` (§6) gravadas em `td-runes.json` como **valores-alvo**, ainda **não consumidas** pelo Core.

## Legado mantido de propósito (Runtime/Rendering ainda usam)
`TdTowerDef.Levels/Branches/Capstone`, `TdStageDef.MaxTowers`, `UpgradeTower`/`ChooseBranch`. Remoção: TD.20/TD.21. Nota no topo de `TdContentModels.cs`.
Fontes de crédito da §6: os valores **vivos** (`waveBonusBase` 20, `bonusQuestion` 15…) não foram alterados, porque mudar a renda quebra os testes de balanceamento do bot; a TD.20/21/22 liga e calibra a partir de `creditSources`.

## ContentHash e goldens (só Core.Tests)
- ContentHash TD (7 arquivos): `f0f35a6a…a7d5` → `e3168003…aca8` (concatenação dos 6 arquivos antigos vs 7 novos, mesma ordem do loader).
- Conteúdo congelado dos testes de mecânica (`TdMechanicsContent`) ganhou as duas tabelas e o `td-runes.json` embarcado, então seu hash também mudou.
- Goldens `StateHash` regenerados (só mudou o hash de conteúdo; todos os testes de balanceamento/mecânica seguem verdes):

| Teste | Antigo | Novo |
|---|---|---|
| TdPlacementTests.Golden | 12074968237746493662 | 3575971896055893931 |
| TdBombCrateTests.Golden | 3885537575890932158 | 7842923145937331451 |
| TdMatchTests.GoldenReplay | 16820935973378525438 | 6662276505330487579 |
| TdCombatTests.GoldenStage3 | 16483169309790164229 | 6829842889198948226 |
| TdSanityBotTests.GoldenOptimizedStage3 | 11218145559906341500 | 6667367544354793914 |

Goldens idle intactos; nenhum golden fora de Core.Tests foi tocado.

## Verificação
- `scripts/wsl/build.sh Release`: 0 warnings, 0 erros.
- `dotnet test tests/SideWar.Core.Tests`: 452 passando (6 testes novos TD.19).
- `scripts/wsl/test.sh`: Core 452 OK; Runtime com 7 falhas em `TdUiControllerTests`/`TdOnboardingTests` (fluxos de clique do HUD), causadas pelas edições em andamento da ficha TD.16b em `src/SideWar.Runtime/Td` (não tocam conteúdo).
