# Evidência TD.29a

Teia de runas do design (SDD-29 §21) e correção do vazamento no núcleo desenhado.

## Matriz do SanityBot (10 seeds por célula, conteúdo embarcado depois da recalibração)

Colunas: fase, perfil, vitórias, estrelas médias, integridade perdida, créditos sobrando, torres construídas, melhor score, onda alcançada, segundos de combate, total, rejeições, ondas onde perdeu, abates.

```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 4 s - SideWar.Core.Tests.dll 
fase perfil     vit  estrelas  perda  creditos torres score onda  combate_s  total_s  rej  perde_ond
   1 Novato     100 %      3.0    0.0      2449 9.0 5268  10.0        289      509    0  -  bolt:78 
   1 Medio      100 %      3.0    0.0      1017 11.0 4900  10.0        285      505    0  -  bolt:13
   1 Otimizado  100 %      3.0    0.0      1199 11.0 6370  10.0        270      490    0  -  bolt:13
   1 SemContra   0 %      0.0   24.0      2864 2.0 0   9.2        268      470    0  8,9,10  bolt:79
   2 Novato     100 %      3.0    0.0      3505 9.0 5103  12.0        402      666    0  -  bolt:9 c
   2 Medio      100 %      3.0    0.0      1433 12.0 4921  12.0        408      672    0  -  bolt:14
   2 Otimizado  100 %      3.0    0.0      1754 12.0 6867  12.0        355      619    0  -  bolt:14
   2 SemContra   0 %      0.0   23.6       293 7.5 0   7.0        261      415    0  7  bolt:43
   3 Novato     100 %      1.0   12.5      2777 10.0 3008  12.0        398      662    0  -  bolt:11
   3 Medio      100 %      1.3    8.4      1028 12.9 3261  12.0        355      619    0  -  bolt:11
   3 Otimizado  100 %      1.4    7.0      1057 13.0 4534  12.0        305      569    0  -  bolt:11
   3 SemContra   0 %      0.0   25.2       529 9.0 0  10.2        321      546    0  10,11  bolt:102
   4 Novato      0 %      0.0   26.3      4114 10.0 0  14.0        394      702    0  14  bolt:204 c
   4 Medio       0 %      0.0   26.2       261 13.9 0  13.7        393      695    0  11,14  bolt:19
   4 Otimizado   0 %      0.0   25.8       435 14.0 0  14.0        374      682    0  14  bolt:201 c
   4 SemContra   0 %      0.0   22.9        95 7.0 0   5.0        162      272    0  5  balancer:0 c
   5 Novato      0 %      0.0   25.5      2872 10.0 0  12.4        358      630    0  11,13  bolt:13
   5 Medio      70 %      0.7   22.9      1227 13.7 2317  13.7        449      750    0  13  bolt:17
   5 Otimizado  100 %      1.0   21.3      1552 14.0 3617  14.0        405      713    0  -  bolt:18
   5 SemContra   0 %      0.0   25.1      2687 9.0 0  11.0        331      573    0  11  cannon:68 f
   6 Novato      0 %      0.0   26.1      2712 10.0 0  11.7        406      664    0  11,12,13  bolt
   6 Medio       0 %      0.0   26.2      1246 13.4 0  13.8        492      795    0  13,15  bolt:14
   6 Otimizado   0 %      0.0   25.9      1334 14.4 0  13.8        453      757    0  13,15  bolt:14
   6 SemContra   0 %      0.0   25.1      1570 9.0 0   8.8        302      495    0  7,9  bolt:68 fr
```

Recalibração: `creditSources` (td-runes.json) waveBonus 60+5w → 120+10w e noLeak 40+2w → 80+4w; `hpWaveGrowth` 1,07 → 1,05 e `hpStageStep` 0,06 → 0,05; Médio fase 2 usa o roteiro de Canhões; Médio/Otimizado até 8 torres extras no fallback. Testes de mecânica usam `TdMechanicsContent` com as fontes de crédito congeladas (valores da TD.22).

## StateHash TD old → new (idle intactos)

| Replay | Old (TD.22) | New |
|---|---:|---:|
| Placement | 15168844986091215698 | 3919082148073959523 |
| BombCrate | 17027428272486969366 | 1577133918984122771 |
| Match | 4316735678008544025 | 5478324055637942033 |
| Combat | 1135319241239498339 | 493369521142980097 |
| SanityBot (Otimizado fase 3 seed 100) | 932256803118187624 | 4305625737701642162 |

## Vazamento
Teste `Enemy_reaching_the_drawn_core_damages_integrity_and_disappears_there`: com layout típico `LeakX = 63 × 1200 / largura`; o inimigo some entre `LeakX` e `LeakX + 5` unidades, causa o `CoreDamage` e emite `EnemyLeaked`. A lógica de dano em si já funcionava (X ≤ 0); o defeito era posicional.
