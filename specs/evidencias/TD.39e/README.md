# TD.39e — evidências headless (2026-11-19)

Baseline `e100971` em worktree destacado e conteúdo atual `188357f`; 20 seeds 100–119 por perfil (Novato = simples, Otimizado) e cenário: fases isoladas 1–6 e corrida contínua desde a fase 1 até completar duas fases. São 280 resultados por versão, executados duas vezes cada; o programa aborta em divergência de StateHash. Não compara hashes entre conteúdos diferentes como se devessem coincidir. As saídas por seed estão em `baseline.csv` e `atual.csv`.

Reprodução, com SDK .NET 10, a partir da raiz (substitua os caminhos absolutos):

```sh
dotnet run -c Release --project specs/evidencias/TD.39e/Playtest.csproj -p:CoreProject=/CAMINHO/DO/WORKTREE/src/SideWar.Core/SideWar.Core.csproj > resultado.csv
```

Ao trocar o projeto de referência, limpe os diretórios `bin` e `obj` do harness ou use diretórios de build isolados para não reutilizar DLLs. O baseline e o atual foram compilados separadamente. `seconds` é tempo de combate simulado: não inclui os 22 s estimados por onda de preparo humano. Em `ate_fase3`, só conta a amostra que completou as duas fases; todas chegaram. Valores de diversidade são quantidade de tipos que efetuaram abates, não todas as torres construídas. `integrity_lost` é dano acumulado, não quantidade de inimigos vazados. As fases são partidas isoladas, não a continuação da corrida.

## Distribuição até a fase 3

| Perfil | Versão | Chegaram | Média (s) | Mín–máx (s) | Créditos médios | Perda de integridade média |
|---|---|---:|---:|---:|---:|---:|
| Novato | baseline | 20/20 | 628,6 | 627,4–630,3 | 2389 | 0 |
| Novato | atual | 20/20 | 636,4 | 635,2–637,6 | 2447 | 0 |
| Otimizado | baseline | 20/20 | 599,7 | 595,8–602,6 | 997 | 0 |
| Otimizado | atual | 20/20 | 611,9 | 609,2–614,4 | 1052 | 0 |

## Fases isoladas

| Cenário | Perfil | Vitórias antes → atual | Perda de integridade média antes → atual | Créditos médios antes → atual | Tipos com abates médios antes → atual |
|---|---|---:|---:|---:|---:|
| 1 | Novato | 20/20 → 20/20 | 0.0 → 0.0 | 2468.0 → 2489.0 | 2.0 → 2.0 |
| 1 | Otimizado | 20/20 → 20/20 | 0.0 → 0.0 | 1197.5 → 1220.5 | 1.0 → 1.0 |
| 2 | Novato | 20/20 → 20/20 | 0.0 → 0.0 | 3278.0 → 3315.0 | 2.0 → 2.0 |
| 2 | Otimizado | 20/20 → 20/20 | 0.0 → 0.0 | 1530.0 → 1561.5 | 2.0 → 2.9 |
| 3 | Novato | 20/20 → 20/20 | 3.7 → 3.8 | 2895.3 → 2915.9 | 2.0 → 2.0 |
| 3 | Otimizado | 20/20 → 20/20 | 1.8 → 2.0 | 1176.7 → 1229.3 | 2.5 → 2.6 |
| 4 | Novato | 20/20 → 20/20 | 14.9 → 14.1 | 4510.6 → 4538.4 | 2.0 → 2.0 |
| 4 | Otimizado | 17/20 → 16/20 | 24.5 → 24.8 | 593.2 → 601.9 | 3.0 → 3.0 |
| 5 | Novato | 0/20 → 0/20 | 26.4 → 27.0 | 3293.9 → 3280.8 | 2.0 → 2.0 |
| 5 | Otimizado | 20/20 → 20/20 | 6.2 → 6.8 | 1521.0 → 1404.0 | 3.0 → 3.0 |
| 6 | Novato | 0/20 → 0/20 | 26.2 → 26.2 | 3152.5 → 3163.3 | 2.0 → 2.0 |
| 6 | Otimizado | 10/20 → 16/20 | 23.6 → 23.4 | 1537.7 → 1633.2 | 3.0 → 3.0 |

## Interpretação e limites

O bot não chama o comando de fusão, portanto seu uso é zero por construção e não mede descoberta ou adoção humana. O bot usa roteiros de torres antigas; tipos com abates não medem diversidade do catálogo novo. Não há contagem de vazamentos no resultado do bot, somente integridade perdida; não inferir número de vazamentos a partir do dano. Na fase 4, otimizado vence 16/20 no atual contra 17/20 no baseline, enquanto novato vence 20/20 em ambos; na fase 6 otimizado sobe de 10/20 a 16/20. São sinais para investigação, não identificação causal de uma torre ou inimigo quebrado. Nenhum parâmetro foi ajustado.

Os testes existentes exercitam replay TD com fusão, mutação, torres e inimigos e verificam tetos de 44 inimigos e 48 torres em casos específicos; não registram pico de entidades da matriz acima. O benchmark de `scripts/wsl/bench.sh p99` usa `SimState` idle e onda sintética, não o TD: `bench-baseline.csv` e `bench-atual.csv` são indicativos WSL, com 100.000 ticks por repetição, carga compartilhada e sem controle de ruído. No cenário pesado, p99 foi 7,616–48,821 µs no baseline e 13,559–35,677 µs no atual; média 4,592–6,302 versus 6,877–17,168 µs; 0 bytes alocados por tick. Não atribuir a diferença ao TD. A medição oficial de desempenho é Windows, não executada nesta ficha.

O `TdReplayScript` não contém ContentHash e `TdReplayRunner` aceita o conteúdo fornecido sem checá-lo: a rejeição explícita de replay com conteúdo incompatível não está demonstrada e requer implementação própria. O teste de `SessionStore` para hash divergente cobre sessão idle, não comprova compatibilidade de replay TD. O perfil TD tem schema/migrações próprios; testes verdes não substituem teste de abertura de save real anterior. UI/DPI e descoberta humana permanecem para playtest visual.

## Verificação de determinismo e testes

Além das repetições da matriz TD, o teste `DeterminismTests.Writes_Hash_File` do jogo idle foi executado duas vezes no Linux; os arquivos `determinismo-linux-1.json` e `determinismo-linux-2.json` foram comparados com `scripts/wsl/compare-determinism.sh` e os quatro hashes coincidiram. A mensagem fixa do script diz “Linux x Windows”, mas ambos os arquivos deste teste são Linux; não é evidência multiplataforma. A suíte `scripts/wsl/test.sh` passou: Core 597 (3 ignorados), Runtime 922, Persistence 143 (1 ignorado), Rendering 781. Uma primeira compilação simultânea aos testes disputou arquivos; a compilação Release foi repetida isoladamente.
