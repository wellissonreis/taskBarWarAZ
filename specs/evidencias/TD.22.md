# TD.22 — Bot com runas, economia e patentes

2026-11-17. Conteúdo embarcado, layout típico de 1920 DIP/3 zonas, sem selos permanentes. **20 seeds (100..119), 6 fases × 4 perfis = 480 partidas por versão**. Antes: Core extraído de HEAD (TD.21) com `git archive` para diretório temporário, sem alterar o checkout. Depois: implementação desta ficha. Só comandos Apply/Step, sem modificar o combate nem injetar créditos no bot.

## Resultado antes → depois

Cada célula: **vitórias % / estrelas médias / torres construídas médias**. Torres construídas inclui eventual reposição após venda; o Médio não vende, logo sua contagem também é a de torres em campo. No Otimizado fase 1: 13 construídas, 12 em campo após a venda, sem reposição: a venda financia os próximos nós.

| Fase | Novato | Médio | Otimizado | SemContra |
|---|---|---|---|---|
| 1 | 100/3.00/6.0 → **100/3.00/9.0** | 100/3.00/6.0 → **100/3.00/13.0** | 100/3.00/6.0 → **100/3.00/13.0** | 0/0.00/2.0 → **0/0.00/2.0** |
| 2 | 100/3.00/7.0 → **100/3.00/9.0** | 0/0.00/7.0 → **100/2.35/14.0** | 0/0.00/7.0 → **100/3.00/14.0** | 0/0.00/6.7 → **0/0.00/6.2** |
| 3 | 0/0.00/8.0 → **0/0.00/9.2** | 0/0.00/8.0 → **100/3.00/14.0** | 0/0.00/8.0 → **100/3.00/14.0** | 0/0.00/8.0 → **0/0.00/7.0** |
| 4 | 0/0.00/8.0 → **0/0.00/10.0** | 0/0.00/8.0 → **100/3.00/14.0** | 0/0.00/8.0 → **100/3.00/14.0** | 0/0.00/6.0 → **0/0.00/6.0** |
| 5 | 0/0.00/8.0 → **0/0.00/10.0** | 0/0.00/8.0 → **100/2.75/15.0** | 0/0.00/8.0 → **100/3.00/15.0** | 0/0.00/8.0 → **0/0.00/9.0** |
| 6 | 0/0.00/8.0 → **0/0.00/10.0** | 0/0.00/8.0 → **100/2.95/15.0** | 0/0.00/8.0 → **100/3.00/16.0** | 0/0.00/8.0 → **0/0.00/8.9** |

Novato vence 1–2 e perde 3–6; Médio vence 100% em 1–6, com ≥2 estrelas médias em 1–5; Otimizado vence 100% com **3 estrelas em cada uma das 120 partidas**; SemContra perde 100%. Médio: média global **14,17 torres**, mediana **14** (13/14/14/14/15/15 por fase). Não existe mais teto interno de 6–8 torres: os roteiros definem as decisões e só o teto técnico do jogo permanece. Isto mede esses perfis, não promete que toda estratégia humana termine em 12–16.

## Renda, tempo e score medidos

Créditos são a sobra ao fim; score é o maior das 20 seeds da célula. Tempo inclui 22 segundos estimados de Preparo por onda. Rejeições observadas pelo bot: zero em todas as células.

| Fase | Perfil | Integridade perdida média | Créditos sobrando médios | Melhor score | Tempo total médio (s) |
|---|---|---:|---:|---:|---:|
| 1 | Novato | 0.0 | 941 | 4467 | 513 |
| 1 | Medio | 0.0 | 244 | 4177 | 509 |
| 1 | Otimizado | 0.0 | 316 | 5584 | 490 |
| 1 | SemContra | 20.0 | 1418 | 0 | 380 |
| 2 | Novato | 0.0 | 1522 | 4522 | 686 |
| 2 | Medio | 3.4 | 326 | 4053 | 755 |
| 2 | Otimizado | 0.0 | 245 | 5748 | 612 |
| 2 | SemContra | 20.0 | 287 | 0 | 417 |
| 3 | Novato | 20.0 | 433 | 0 | 524 |
| 3 | Medio | 0.0 | 518 | 4330 | 593 |
| 3 | Otimizado | 0.0 | 473 | 5946 | 527 |
| 3 | SemContra | 20.0 | 457 | 0 | 480 |
| 4 | Novato | 20.0 | 843 | 0 | 535 |
| 4 | Medio | 0.0 | 1784 | 4773 | 680 |
| 4 | Otimizado | 0.0 | 266 | 6171 | 628 |
| 4 | SemContra | 20.0 | 99 | 0 | 272 |
| 5 | Novato | 20.0 | 342 | 0 | 487 |
| 5 | Medio | 1.7 | 1525 | 4579 | 760 |
| 5 | Otimizado | 0.0 | 540 | 6135 | 676 |
| 5 | SemContra | 20.0 | 733 | 0 | 512 |
| 6 | Novato | 20.0 | 616 | 0 | 590 |
| 6 | Medio | 0.2 | 2421 | 4477 | 905 |
| 6 | Otimizado | 0.0 | 709 | 6241 | 810 |
| 6 | SemContra | 20.0 | 181 | 0 | 405 |

Fontes de evento mantidas após medição: início +80; fechar onda 60+5w; perfeita 40+2w; caixa 40+2w; pergunta correta 40+3w; chefe morto +120; abates conforme JSON. O bot não responde perguntas: a tabela não pressupõe renda por acerto. **Créditos iniciais da fase 2: 260→300, fase 6: 520→560**, mais os 80 da fonte (totais 380/640). Demais fases: 300/380/420/460 totais. O reforço inicial permite controle antes dos blindados e da prova final, sem inflar a renda por onda. Sobra tardia permanece uma decisão de score; sem juros.

Curva normal/Low: primeiros quatro fatores preservados; para n≥4, `round(fator antigo × 1,10^(n−3))`, tabela inteira pré-computada. Normal n=12: 1.455→3.431; décima primeira Raio: 838→1.633 créditos. Low continua menor que normal, mas recebe a mesma aceleração. Validador passa a conferir estrutura (48 entradas, início 100, crescimento estrito, Low≤normal), permitindo calibração por JSON. Testes conferem preços e fatores embarcados. HP, inimigos, ondas, estrelas e selos mantidos: não foi necessário reduzir a dificuldade do combate para atingir os alvos.

Perfis: Novato compra dois pequenos adjacentes de dano; Médio completa o setor do contra (Canhão em 2/3, Raio nas demais), depois Geral; Otimizado antecipa Penetrador/Rajada e Congelar, completa keystone e fronteira Raio/Gelo e reinveste a sobra em nós adjacentes. Na fase 1 vende o Canhão ao especializar em Raio. SemContra mantém o roteiro sem runas do contra. `runeSector` visita alvos em ordem estável, pagando também os pré-requisitos encontrados por PathTo; não há compra através de bloqueios.

Patentes: **0 / 5.000 / 12.000 / 22.000 / 29.000 / 34.000** (antes 0/5k/12k/20k/25k/31k). Soma dos melhores por fase nas 20 seeds: Novato **8989**; Medio **26389**; Otimizado **35825**; SemContra **0**; respectivamente Júnior/Sênior/MVP/Estagiário. Fórmula de score intacta.

## Arbitragem e determinismo

Venda = `min(PurchasePrice, floor(custo vigente(tipo,n−1) × taxa))`. Preço pago inclui desconto e curva Low. A runa pode ser devolvida a qualquer momento, 100%, em cascata. Não há taxa/trava nova. Torres baratas podem render menos que torres caras do mesmo tipo: fungibilidade cede ao teto individual. Trocar curva pode gerar perda zero, jamais lucro. `PurchasePrice` entra no hash porque afeta a venda futura.

Varredura automatizada: **5 tipos × 48 compras × 2 estados de Auto-orçamento × 2 taxas de venda (70%/85%, bônus recomprados sem Cota) = 960 torres vendidas**, compradas com Cota folgada, devolvendo o ramo Geral antes de vender e recomprando apenas os bônus de venda no caso de 85%, incluindo os índices altos onde existia o lucro. Cada venda ≤ desembolso individual e saldo final ≤ inicial. Testes de PathTo cobrem todos os 61 alvos, compra de cada passo, caminho alternativo via fronteira, alvo possuído/desconhecido e desempate estável.

| Golden TD StateHash | Antigo | Novo |
|---|---:|---:|
| Placement | 10643278674487539972 | 15168844986091215698 |
| BombCrate | 588953149544461392 | 17027428272486969366 |
| Match | 2075538275561993642 | 4316735678008544025 |
| Combat | 14724320659106336327 | 1135319241239498339 |
| SanityBot (Otimizado, fase 3, seed 100) | 15227494432892406870 | 2514828015037595496 |

Estratégia só de Raio (seis torres + setor inteiro, habilidades e chamadas do Otimizado) perde a fase 6 nas 20 seeds. Isso cobre o risco concreto ensaiado, sem alegar prova exaustiva sobre todos os roteiros.

Replays de mecânica repetidos cinco vezes; bot verifica repetição e mudança de seed. Goldens e código idle intactos. **Nenhum PNG/arquivo de Rendering alterado ou regenerado**. A nova curva altera preços exibidos, portanto goldens de UI com esses preços podem precisar atualização coordenada após TD.24a; não se afirma validação visual nesta ficha.

## Verificação

Core Release: zero warnings/erros. Core.Tests: **469 passando, zero falhas, zero skips**; três skips de balanceamento removidos, matriz de 20 seeds usada como regressão; teste do Otimizado exige exatamente 3★ e 100% de vitórias. Comandos de reprodução: `DOTNET_PROCESSOR_COUNT=4 dotnet test tests/SideWar.Core.Tests -c Release -m:1` e `dotnet build src/SideWar.Core/SideWar.Core.csproj -c Release -warnaserror -p:ContinuousIntegrationBuild=true`.

O build geral Release foi tentado e falhou com zero warnings e dois erros em arquivo da ficha paralela `tests/SideWar.Runtime.Tests/Td/TdOnboardingTests.cs`: CS0649 (Rig.Now) e xUnit2013 (Assert.Equal para tamanho de coleção). Não alterados. Resultado final dos testes e commit no REPORT da ficha. `git diff --check` limpo.
