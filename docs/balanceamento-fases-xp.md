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
| 1 | 1 | 6 | 4 |
| 2 | 4 | 13 | 4 |
| 3 | 6 | 11 | 4,5 |
| 4 | 7 | 11 | 1,2 |
| 5 | 9 | 15 | 2 |
| 6 | 10 | 14 | 1,2 |

### XP de patente

- Ganho: 60% a partir de Sênior (rumo a Especialista) e 40% a partir de Especialista (rumo a Arquiteto). Os limiares não mudaram.
- Perda abaixo de Sênior: 200 XP por ponto de dano no núcleo, com escudo (regra anterior).
- Perda de Sênior em diante: cada inimigo que chega à base custa metade da barra da patente atual (do limiar dela ao próximo), sem escudo. Se rebaixar, as fusões acima do limite da nova patente são destruídas, escolhidas ao acaso; a torre base fica.
