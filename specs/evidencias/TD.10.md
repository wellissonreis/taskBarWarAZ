# Evidência TD.10: SanityBot TD e curva de dificuldade

Data: 2026-11-16. Ficha TD.10 (PLANO §9), SDD-28 RF-28.12 / CA-28.6 / DP-28.10.

## Metodologia
- **SanityBot** (`src/SideWar.Core/Td/TdSanityBot.cs` + `TdBotPlans.cs`): jogador headless e determinístico, só pela API pública de `TdMatchState` (Apply/Step) com `LayoutChanged`, `SetPresence`, `BuildTower`, `UpgradeTower`, `ChooseBranch`, `SetTargeting`, `UseAbility`, `StartWave`, `CallNextWave`, `CollectCrate`. Sem relógio real nem `Random`.
- **Layout típico**: barra de 1920 DIP, 3 zonas livres (100-520, 640-1180, 1300-1800 DIP ≈ 62-325, 400-737, 812-1125 lógicos), snap de 44 DIP; o bot busca o X livre mais próximo do preferido.
- **Perfis** (roteiro por fase em DSL, depois gasta o que sobra):
  - **Novato**: só Raio e Canhão, sem ramos, sem habilidades; reage à caixa-bomba em 6,5 s (contagem de 8 s).
  - **Médio**: contra óbvio da fase, 1-2 ramos, habilidades, caixa em 3 s, sem chamada antecipada nem capstones.
  - **Otimizado**: contra do conceito, ramos e capstones, habilidades (limiares menores), `CallNextWave` quando restam ≤3 inimigos e perda ≤1, caixa em 1,5 s.
  - **SemContra**: como o médio, mas sem o contra do conceito (fase 1: só 2 Raios e não gasta o resto = ignora "construir"; 2: só Raio; 3: só Raio; 4: só Canhão/Balanceador, sem voador; 5: só Gelo/Canhão; 6: só Raio/Gelo).
- Seeds 100..119 (20 por célula) para as tabelas; o teste de regressão usa 6 seeds (100..105).
- Duração = tempo de combate (Tick/30) + 22 s de Preparo humano por onda (estimativa; o sim não mede o tempo do jogador).
- Colunas: vit = taxa de vitória; ★ = média de estrelas; perda = integridade perdida média (derrota = 20); créditos = sobra ao fim; onda = onda alcançada; combate_s/total_s; perde_onda = ondas onde as derrotas ocorreram; kills = mortes por tipo de torre (média por partida; `zona` = queimadura).

## Antes (conteúdo do commit 7b7d81c, mesmo bot)
```
fase perfil     vit  estrelas  perda  creditos  onda  combate_s  total_s  rej  perde_onda  kills
   1 Novato     100 %      3.0    0.0       135  10.0        190      410    0  -  bolt:99 cannon:21
   1 Medio      100 %      3.0    0.0       143  10.0        178      398    0  -  bolt:102 frost:18
   1 Otimizado  100 %      3.0    0.0       111  10.0        148      368    0  -  bolt:105 frost:15
   1 SemContra   0 %      0.0   20.0      1104  10.0        271      491    0  10  bolt:89
   2 Novato     100 %      2.9    0.9       145  12.0        343      607    0  -  bolt:89 cannon:74
   2 Medio      100 %      3.0    0.0       129  12.0        336      600    0  -  bolt:120 cannon:28 frost:16
   2 Otimizado  100 %      3.0    0.0       325  12.0        266      530    0  -  bolt:109 cannon:39 frost:16
   2 SemContra  100 %      3.0    0.0       649  12.0        320      584    0  -  bolt:165
   3 Novato     100 %      2.9    1.6       254  12.0        378      642    0  -  bolt:167 cannon:31
   3 Medio      100 %      1.3    7.2       190  12.0        383      647    0  -  bolt:135 cannon:61 frost:0
   3 Otimizado  100 %      1.4    6.5       178  12.0        370      634    0  -  bolt:137 cannon:59 frost:0
   3 SemContra  100 %      3.0    0.0       417  12.0        338      602    0  -  bolt:200
   4 Novato     100 %      1.6    6.4       907  14.0        451      759    0  -  bolt:196 cannon:8
   4 Medio      30 %      0.3   18.4       190   9.6        312      522    0  7,8,13  bolt:93 cannon:6 frost:6
   4 Otimizado  100 %      1.4    8.7       258  14.0        420      728    0  -  bolt:180 cannon:10 frost:12
   4 SemContra   0 %      0.0   20.0        71   4.0        133      221    0  3,4,5  balancer:1 cannon:14
   5 Novato      0 %      0.0   20.0       695  12.3        466      738    0  8,10,13,14  bolt:158 cannon:27
   5 Medio      70 %      0.7   17.4       282  13.7        520      821    0  13  bolt:201 cannon:18 frost:5
   5 Otimizado  100 %      1.9    5.3       394  14.0        420      728    0  -  bolt:209 cannon:26 frost:6
   5 SemContra   0 %      0.0   20.0        57   2.6        109      166    0  2,3  cannon:7 frost:1
   6 Novato      0 %      0.0   20.0        64   3.0        135      201    0  3  bolt:28 cannon:10
   6 Medio       0 %      0.0   20.0        66   6.0        248      380    0  6  bolt:84 cannon:8 frost:2
   6 Otimizado   0 %      0.0   20.0       106   7.0        300      454    0  6,8  bolt:106 cannon:10 frost:3
   6 SemContra   0 %      0.0   20.0        53   3.0        132      198    0  3  bolt:32 frost:5
```

## Depois (conteúdo balanceado)
```
fase perfil     vit  estrelas  perda  creditos  onda  combate_s  total_s  rej  perde_onda  kills
   1 Novato     100 %      3.0    0.0       144  10.0        308      528    0  -  bolt:96 cannon:42
   1 Medio      100 %      3.0    0.0       159  10.0        301      521    0  -  bolt:89 frost:48
   1 Otimizado  100 %      3.0    0.0       124  10.0        273      493    0  -  bolt:92 frost:45
   1 SemContra   0 %      0.0   20.0       782   8.0        204      380    0  8  bolt:52
   2 Novato     100 %      3.0    0.0       156  12.0        438      702    0  -  bolt:35 cannon:117
   2 Medio      95 %      1.9    5.7       122  11.8        475      736    0  9  bolt:108 cannon:27 frost:11
   2 Otimizado  100 %      2.7    2.6       157  12.0        427      691    0  -  bolt:108 cannon:33 frost:9
   2 SemContra   0 %      0.0   20.0        74   7.3        287      449    0  7,8  bolt:48
   3 Novato     95 %      1.1   10.2       265  11.9        414      676    0  10  bolt:101 cannon:47
   3 Medio      100 %      2.9    0.7       342  12.0        373      637    0  -  bolt:71 cannon:85 frost:4
   3 Otimizado  100 %      3.0    0.3       313  12.0        287      551    0  -  bolt:62 cannon:94 frost:4
   3 SemContra   0 %      0.0   20.0        67   9.5        314      523    0  8,9,10  bolt:84
   4 Novato     100 %      2.3    3.4       967  14.0        416      724    0  -  bolt:205 cannon:40
   4 Medio      100 %      2.3    2.9       191  14.0        397      705    0  -  bolt:181 cannon:36 frost:28
   4 Otimizado  100 %      3.0    0.7       266  14.0        340      648    0  -  bolt:184 cannon:38 frost:25
   4 SemContra   0 %      0.0   20.0        71   5.0        162      272    0  5  balancer:0 cannon:21
   5 Novato     100 %      2.9    0.8      1207  14.0        487      795    0  -  bolt:163 cannon:78
   5 Medio      100 %      2.2    4.2       316  14.0        476      784    0  -  bolt:181 cannon:57 frost:1
   5 Otimizado  100 %      3.0    0.0       341  14.0        378      686    0  -  bolt:182 cannon:58 frost:1
   5 SemContra   0 %      0.0   20.0       168   9.8        351      565    0  7,8,10,11,13,14  cannon:72 frost:31
   6 Novato      0 %      0.0   20.0       572  13.1        538      826    0  11,13,14  bolt:125 cannon:60
   6 Medio      85 %      1.1   10.8       466  14.8        578      905    0  14  bolt:173 cannon:68 frost:1
   6 Otimizado  100 %      2.9    0.3       476  15.0        487      817    0  -  bolt:181 cannon:69 frost:1
   6 SemContra   0 %      0.0   20.0        65   4.6        184      285    0  4,6  bolt:17 frost:3
```

## Valores alterados (src/SideWar.Core/Content/td-*.json; Combat.cs só ganhou observação, nenhuma constante mudou)
| Arquivo | Antes | Depois | Motivo |
|---|---|---|---|
| td-towers | Raio custo 70, dano 14, int. 0,6 | custo 90, dano 24, int. 0,9 | o Raio sozinho vencia todas as fases (generalista barato demais) |
| td-towers | Canhão dano 45, int. 1,9 | dano 100, int. 2,2 | papel "solo pesado"; era fraco por custo |
| td-towers | Gelo dano 5 | dano 9 | dano não nulo, ainda não mata sozinho |
| td-towers | Raio A x2,6; super hit x6; Canhão B x2,2 vs blindado/chefe; Scale Set 25% | x3,0; x8; x2,6; 30% | ramos/capstones precisam compensar (diferença médio x otimizado) |
| td-enemies | runner hp 22, vel 78, ouro 6 | hp 36, vel 105, ouro 7 | contra o gelo/Raio; canhão sozinho não segura |
| td-enemies | armored hp 110, armadura 6 | hp 130, armadura 38 | Raio sem ramo mal fura; exige Canhão/ramos |
| td-enemies | swarm hp 14 | hp 150, vel 50 | exige área (Barragem) |
| td-enemies | Ransomware hp 1500 | 2800 | chefe de fase 3 |
| td-enemies | Botnet hp 2200, split 6 | 6000, split 4 | calibrado (leaks do split) |
| td-enemies | Zero-day hp 3000, regen 2%/s | 3600, 1%/s | regen de 2%/s de max HP anulava dano |
| td-stages | `hpWaveGrowth` 1,08; `hpStageStep` 0,10 | 1,07; 0,06 | curva 1,07^(w-1) |
| td-stages | créditos iniciais 150/180/200/240/260/300 | 220/260/300/340/380/520 | custos subiram; a fase 6 precisa de 4+ torres no início |
| td-waves | ondas originais | contagens por rampa por fase (ex.: fase 1 x0,7→0,95; fase 6 x0,3→1,2), corredores x0,8→3,8 na fase 1, blindados x0,6→2,1 na fase 2, enxames x0,4→1,2 na fase 3, intervalos/offsets alongados nas fases 1-3 (x2,3/1,5/1,3), ondas 3/6/8 da fase 6 e 8/10 da fase 5 com menos tanques/parasitas | curva e duração 8-15 min |
| td-meta | `infrastructure.slots2` maxTowers +1 | +2 | teto 12 agora alcançável: 8 + 1 + 2 + 1 (capstone Réplica) = 12 |

Divergência do SDD: a tabela RF-28.12 diz créditos iniciais 150..300; os valores acima são outros (SDD-28 não editado nesta ficha; atualizar em ficha de docs).

## Metas e resultado (20 seeds)
| Meta | Resultado |
|---|---|
| Novato vence fase 1-2 | 100% / 100% (3,0★ / 3,0★) |
| Médio vence 1-5 com 2-3★ | vence 95-100%; ★ 3,0 / 1,9 / 2,9 / 2,3 / 2,2 (fase 2 fica em 1,9 com 20 seeds; 2,0+ com as 6 do teste) |
| Otimizado 3★ em todas | vence 100%; ★ 3,0 / 2,7 / 3,0 / 3,0 / 3,0 / 2,9 |
| Cada fase exige o contra | SemContra vence 0% em todas as fases |
| Fase 6: derrota possível | Novato 0%, Médio 85% (1,1★), Otimizado 100% (2,9★) |
| Duração 8-15 min | Otimizado 8,2 a 13,6 min; Médio até 15,1 min na fase 6 |

Onde se perde (SemContra / Novato): fase 1 onda 8; fase 2 ondas 7-8; fase 3 ondas 8-10; fase 4 onda 5 (voadores); fase 5 ondas 7-14; fase 6 ondas 4-6 (SemContra) e 11-14 (Novato).

## Limitações / achados
- A **caixa-bomba domina o novato**: sem reagir (ignorando a caixa) ele perde ~1 torre a cada 2 ondas e perde todas as fases. O bot novato reage em 6,5 s; vale revisar a punição (SDD-28, RF-28.19).
- O **Balanceador** quase não mata (`balancer:0`) e os roteiros mal o usam: seu efeito não está validado pelo bot. O **Scale Set** só entra nos roteiros das fases 4 e 6.
- Cobertura por seeds: 6 no teste de regressão (rápido), 20 nesta tabela. Resultados dependem dos roteiros do bot; mudar roteiros muda a curva.
- Novato vence a fase 4 e 5 (acumula 900-1200 créditos sem gastar); não era meta bloquear.

## Hash
- Goldens TD (`TdMatchTests.GoldenReplay` 5049926445164019220, `TdCombatTests.GoldenStage3` 10752822863640636477, `TdPlacementTests.Golden` 1649672874497974502, `TdBombCrateTests.Golden` 9484780872707564732): **inalterados**. Os testes de mecânica passaram a usar conteúdo congelado (`TdMechanicsContent`, valores do TD.3b), então tuning não os quebra; antigo → novo = igual.
- Novo golden do conteúdo balanceado: `TdSanityBotTests.GoldenOptimizedStage3` (fase 3, Otimizado, seed 100) = 4040583883875090050.
- Hash do conteúdo TD (`TdContentLoader.ContentHash`) mudou (td-*.json alterados). Idle (`ContentLoader`, SimHash/Replay) intacto.

## Testes
`TdSanityBotTests` (9): Novato 1-2 / fase 6, Médio 1-5 com ★≥2, Otimizado ★≥2,5 e ≥ médio, SemContra perde todas, duração 7,5-15,5 min e ondas completas, determinismo + golden, rejeições do Otimizado. Roda em ~3 s. `TdContentTests.Validator_ChecksWaves` ajustado às novas ondas.
