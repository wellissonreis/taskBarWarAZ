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
