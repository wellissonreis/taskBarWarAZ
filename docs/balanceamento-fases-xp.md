# Fases, patente e HP

A vida de um inimigo, inclusive o chefe regional da última onda, é `HP base × (1 + 0,002 × (onda − 1)) × (1 + 0,07 × (fase − 1)) × 1,4^ciclo`. O crescimento por onda é linear, em vez de exponencial; o fator de ciclo continua calculado por multiplicações sucessivas.

Cada comprimento é `arredondar para o inteiro mais próximo (1,25 × média de ondas entre as primeiras promoções)`, com empate arredondado para cima. A média inclui cinco seeds (1 a 5) de cada perfil, médio e otimizado, em `TdSanityBot.RunContinuous`, com reputação inicial zero, sem parar ao colapsar e com torres, créditos e runas acumulados entre fases. Foram realizadas rodadas sucessivas com os próprios comprimentos resultantes; a tabela abaixo corresponde à configuração instalada. Arquiteto não possui próxima patente: sua fase repete o comprimento da fase anterior.

| Fase / patente inicial | Próxima patente | Intervalos das seeds 1–5, médio | Intervalos das seeds 1–5, otimizado | Média | Ondas instaladas |
|---|---|---|---|---:|---:|
| 1 Estagiário | Júnior | 67, 67, 67, 67, 67 | 67, 67, 67, 67, 67 | 67 | 84 |
| 2 Júnior | Pleno | 144, 144, 144, 144, 144 | 144, 144, 144, 144, 144 | 144 | 180 |
| 3 Pleno | Sênior | 242, 242, 242, 242, 242 | 242, 242, 242, 242, 242 | 242 | 303 |
| 4 Sênior | Especialista | 311, 311, 311, 311, 311 | 311, 311, 311, 311, 311 | 311 | 389 |
| 5 Especialista | Arquiteto | 378, 378, 378, 378, 378 | 397, 378, 378, 378, 378 | 379,9 | 475 |
| 6 Arquiteto | — | — | — | — | 475 |

As promoções do perfil médio ocorreram, respectivamente, nas ondas 68 da fase 1, 128 da fase 2, 190 da fase 3, 198 da fase 4 e 187 da fase 5. Os intervalos são contados em ondas concluídas desde a promoção anterior; a numeração exibida durante a observação já aponta para a onda seguinte.

## Escala de campanha (BAL-1)

Vale fora do modo clássico (`TdMatchOptions.UsesCampaignRules`). Os valores por região ficam em `td-stages.json` (`hpStart`, `hpEnd`, `bossHp`).

- HP e escudo dos inimigos comuns = fórmula acima × curva da região (reta de `hpStart` na onda 1 a `hpEnd` na última, sem platô) × dificuldade (Casual 1, Difícil 1,05, Extremo 1,1).
- Chefes ficam fora da curva e da dificuldade (crescem só por onda, fase e ciclo).
- Chefe regional da última onda: × `bossHp`, escudo de 35% do HP e armadura +10.
- Nas regiões 1 a 3, torre sem vantagem contra o chefe regional causa só 25% do dano (`bossOffStrategy`).
- Chefes (não aliados) da última onda não são empurrados, retidos nem paralisados.
- Fila: o mesmo inimigo só é empurrado de novo depois de ter tempo de andar 4× o trecho devolvido, para nenhuma onda ficar presa.
- Chefe regional da região final (6, em qualquer ciclo): só recebe dano com a teia 128/128.
- Chefe da última onda chega à base: volta 10 ondas, mantém torres e créditos e **pausa** (única pausa automática; retomada manual).
- A calibração usa formações compráveis com a renda real (~4 mil créditos na fase 1; ~105 mil acumulados até a fase 6).

| Região | hpStart | hpEnd | bossHp |
|---|---:|---:|---:|
| 1 | 1 | 9 | 4,2 |
| 2 | 4 | 13 | 3,4 |
| 3 | 4,2 | 7,7 | 2 |
| 4 | 4,7 | 7,5 | 1,2 |
| 5 | 4,5 | 7,5 | 1,3 |
| 6 | 3,5 | 5,8 | 1,2 |

## Retorno decrescente das runas (BAL-2)

Fora do modo clássico, o total de Alcance, dano do Raio, dano do Canhão e Créditos passa por `bruto × joelho / (bruto + joelho)` (inteiro). Joelhos em `td-runes.json`. Totais com a teia 128/128:

| Efeito | Antes | Depois | Joelho |
|---|---:|---:|---:|
| Alcance | +39% | +7,95% | 1000 pb |
| Dano do Raio | +28,5% | +7,40% | 1000 pb |
| Dano do Canhão | +27% | +7,29% | 1000 pb |
| Créditos por onda | 20 | 0,17 | 30 centésimos |

A queda dos créditos é necessária: com as runas de créditos lineares (1 crédito por poder) e todo o resto igual ao instalado, a formação simples volta a vencer as fases 1 e 2 em Casual com 59 runas (3 de 3 seeds cada), porque compra uma torre a mais na fase 1 (9 em vez de 8) e duas a mais na fase 2 (13 em vez de 11). Valores intermediários não foram medidos.

## Simulação com economia real (BAL-2)

Harness: `tests/SideWar.Core.Tests/TdBalanceHarnessTests.cs`, fora da suíte normal:

```
dotnet test tests/SideWar.Core.Tests -c Release --filter Category=Balance -- xUnit.Explicit=on
```

Nada é injetado: créditos iniciais, bônus de onda, ouro e runas são os do jogo; a teia fica fixa na contagem do cenário. "Simples" são até 20 torres Raio/Canhão alternadas, sem fusão. "Estratégica" são torres escolhidas pelos contras de cada região, com fusões e mira no mais forte contra chefe. "Vence" é concluir a região sem o chefe chegar à base e com menos de 3 voltas da última onda; pode haver vazamento. Seeds 1 a 3. Os cenários f6 são a campanha contínua desde a fase 1, com torres e créditos mantidos. Antes = 76b4509.

| Cenário | Antes: vitórias | Antes: dano vazado | Depois: vitórias | Depois: dano vazado |
|---|---:|---|---:|---|
| Fase 1 Casual, 59 runas, simples | 3/3 | 0 | **0/3** | 5 (chefe chega à base) |
| Fase 1 Casual, 59 runas, estratégica | 3/3 | 0 | 3/3 | 0 |
| Fase 2 Casual, 59 runas, simples | 3/3 | 0 | **0/3** | 7 (chefe chega à base) |
| Fase 2 Casual, 59 runas, estratégica | 3/3 | 16 | 3/3 | 20 |
| Fase 1 Extremo, 0 runas (Estagiário), estratégica | 3/3 | 19 a 20 | 3/3 | 26 |
| Campanha até a fase 6, 128/128, estratégica | 0/3 | 5 (para na fase 3) | **3/3** | 1 a 5 |
| Campanha até a fase 6, 127/128, estratégica | 0/3 | 5 (para na fase 3) | **0/3** | 17 a 23 (chefe final imune) |

Limites da medição: as formações são roteiros fixos, não jogadores ótimos; um jogador pode encontrar formação simples melhor que a medida ou vencer com folga maior. A estratégica ainda vaza na fase 2 e no Extremo da fase 1 (perde XP, mas conclui). Na campanha, a trava de patente faz regiões se repetirem, e a seed 2 usa 2 voltas da última onda. As curvas das regiões 3 a 6 ficaram abaixo das anteriores ao BAL-2, e a curva da região 6 é menor que a da região 5, porque com os valores antigos o roteiro não passava da fase 3.

### XP de patente

- Ganho: 60% a partir de Sênior (rumo a Especialista) e 40% a partir de Especialista (rumo a Arquiteto). Os limiares não mudaram.
- Perda abaixo de Sênior: 200 XP por ponto de dano no núcleo, com escudo (regra anterior).
- Perda de Sênior em diante: cada inimigo que chega à base custa metade da barra da patente atual (do limiar dela ao próximo), sem escudo. Se rebaixar, as fusões acima do limite da nova patente são destruídas, escolhidas ao acaso; a torre base fica.
