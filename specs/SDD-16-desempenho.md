# SDD-16: Desempenho: metas mensuráveis, orçamento, instrumentação e otimizações obrigatórias

**Status:** Rascunho para revisão · **Etapa:** transversal (medição desde a E1; comprovação na E7) · **Depende de:** SDD-02, 08, 10, 12, 21, 22, 23, 25, 27 · **Bloqueia:** a aprovação de cada etapa com metas

> **Atualizada pelo jogo idle (ADR-019 a ADR-023, ADR-027, ADR-031).** Os cenários legados (fase Build, 44 inimigos, 120 projéteis) foram **renomeados** nas tabelas e em RF-16.2; entram `presente-onda-media`, `presente-onda-pesada`, `ausente`, `offline-retorno`, `painel-aberto` e `varias-janelas`, os contadores de UIA e o inventário de segundo plano atualizado (RF-16.8). As metas novas são **não comprovadas** até haver medição.

## 1. Identificador e título

SDD-16: Metas de CPU, RAM e GPU; cenários de medição; ferramentas; regras de código de baixo custo; diagnóstico em tempo de execução.

## 2. Objetivo

Tornar a "prioridade absoluta de baixo consumo" verificável:

- metas numéricas por cenário;
- método de medição reprodutível no Windows;
- distinção explícita entre **meta**, **medido (aprovado ou reprovado)** e **não medido**.

## 3. Contexto técnico

- Toda meta abaixo é uma **META NÃO COMPROVADA** até haver medição registrada em `specs/evidencias/`.
- As medições valem **somente no Windows 11**, na máquina de referência (D-05). Números do WSL são indicativos.
- Janelas *layered* movem parte do custo para o **DWM** (`dwm.exe`). Por isso, as medições incluem o delta de CPU e GPU do DWM com o overlay oculto vs. visível.
- A referência não tinha nenhuma gestão de consumo (ANALISE L3, L4, L5): não há "número da referência" para comparar.

## 4. Escopo e exclusões

**Escopo:** tabela de metas, cenários, modo `--bench`, `measure-perf.ps1`, painel de diagnóstico, contadores internos (`Meter`), configuração de runtime e GC, regras de código e o processo de revisão de metas.

**Exclusões:** benchmarks de microcódigo (SDD-17 os define e executa).

## 5. Requisitos funcionais

### RF-16.1: Tabela de metas (status inicial: **não medido**)

CPU% é a porcentagem da **CPU total da máquina** (todos os núcleos lógicos), média no período. "Proc" é o processo SideWar. "DWM Δ" é a diferença do `dwm.exe` entre o overlay visível no cenário e o overlay oculto.

| ID | Cenário | Métrica | Meta | Status |
|---|---|---|---|---|
| P-00 | `baseline-wpf` (app vazio: WPF sem janelas + GameThread ociosa) | Private bytes / Working set | **registrar** (define a base para P-06) | não medido |
| P-01 | `hidden` (oculto 5 min) | CPU proc · wakes/s · frames | ≤ 0,1% · ≤ 1/s · 0 | não medido |
| P-02 | `idle-static` (visível, `reducedMotion`, sem inimigos e sem input, 5 min; SDD-22 RNF-22.6) | CPU proc · frames/s | ≤ 0,2% · ≤ 0,1 | não medido |
| P-03 | `ausente` (Ausente, 8 fps, opacidade 128, mesma carga de inimigos, 5 min; = P-18) | CPU proc · DWM Δ | ≤ 0,4% · ≤ 0,3% (SDD-22 RNF-22.4) | não medido |
| P-04 | `presente-onda-media` (Presente, 20 inimigos, 6 torres, 30 fps, 5 min) | CPU proc · DWM Δ · GPU (proc+DWM Δ) | ≤ 1,5% · ≤ 1% · ≤ 2% | não medido |
| P-05 | `presente-onda-pesada` (Presente, 44 inimigos, 12 torres, banner de pico e números de dano ativos, 30 fps, 5 min) | CPU proc · DWM Δ · GPU | ≤ 3% · ≤ 1,5% · ≤ 3% | não medido |
| P-06 | `presente-onda-pesada` / `hidden` após `ReleaseCaches` | Private bytes | ≤ P-00 + 60 MB / ≤ P-00 + 25 MB | não medido |
| P-07 | `soak` (`presente-onda-pesada` em loop por 2 h) | Δ private · Δ handles/GDI/USER | ≤ 5 MB · ±5 | não medido |
| P-08 | Início (exe → primeiro frame visível) | frio / morno | ≤ 2,5 s / ≤ 1,2 s | não medido |
| P-09 | `presente-onda-pesada` | GCs gen0 / gen2 | ≤ 1 a cada 30 s / 0 em regime | não medido |
| P-10 | `presente-onda-pesada` | Alocação por frame | ≤ 64 B (E1–E6); 0 B (E7+) | não medido |
| P-11 | Composição do frame (`presente-onda-pesada`, faixa de 96 DIP a 150%) | p50 / p95 | ≤ 2,5 ms / ≤ 4 ms | não medido |
| P-12 | `UpdateLayeredWindowIndirect` | p50 / p95 | ≤ 1 ms / ≤ 2 ms | não medido |
| P-13 | Tick da simulação (`presente-onda-pesada`) | média / p99 | ≤ 60 µs / ≤ 150 µs | não medido |
| P-14 | Latência F11 → oculto / Esc → visível | — | ≤ 500 ms / ≤ 1 s | não medido |
| P-15 | Latência clique → feedback | — | ≤ 50 ms | não medido |
| P-16 | Gravações de sessão em 10 min de onda | quantidade | ≤ 12 (+1 por fim de onda) | não medido |
| P-17 | `strip-present` (alias de SDD-22 T-22.12; Presente, 24 inimigos, 12 torres, 1 banner, 1 caixa, 30 fps, 5 min) | CPU proc · DWM Δ · composição p95 | ≤ 1,2% · ≤ 0,8% · ≤ 2 ms (SDD-22 RNF-22.1/22.5) | não medido |
| P-18 | `ausente` (Ausente, 30 Hz de simulação, 8 fps de render, cena a 50%, mesma carga) | CPU proc · DWM Δ · Presents/s | **≤ 0,4%** · ≤ 0,3% · 8 ± 1 (SDD-22 RNF-22.4) | não medido |
| P-19 | `offline-retorno` (snapshot sintético de **12 h**, até 2.000 ondas; só o `OfflineCalculator.Compute`, SDD-25 RF-25.7) | tempo de cálculo | **≤ 50 ms** (SDD-25 RNF-25.2) | não medido |
| P-20 | `painel-aberto` (Presente, painel de SDD-24 aberto com uma aba e cards visíveis, 5 min) | CPU proc · DWM Δ · Presents/s | **≤ +0,1% de CPU** sobre o cenário presente (P-17); DWM Δ e Presents/s registrados (**ajustado por ADR-031**) | não medido |
| P-21 | `varias-janelas` (faixa + widget + painel + card + banner + caixa + selo de ausente alternando, 5 min) | CPU proc · DWM Δ · handles/GDI/USER | **≤ +0,2% de CPU** sobre o cenário presente (P-17), com todas as janelas visíveis (faixa, widget, painel, banner, caixa); sem vazamento de handles (±5, como P-07) (**ajustado por ADR-031**) | não medido |
| P-22 | Atualização da barra (UIA, RF-16.5) | atualizações/min · ms por atualização · memória retida do UIA | ≤ 2/min com visível e 0 oculto (SDD-21 RNF-21.2) · atualização aplicada ≤ 30 ms, sem contar a consulta UIA (SDD-21 RNF-21.1; p95 da consulta registrado, timeout de 300 ms) · memória do UIA a **registrar**, meta provisória ≤ 5 MB retidos (SDD-21 RNF-21.3) | não medido |

> **Observação (ADR-019, ADR-022, ADR-023, ADR-031):** P-17 a P-22 são metas novas, todas "não medido" (meta não comprovada); P-20 e P-21 foram ajustadas pela ADR-031.
>
> **Ajustes nas linhas antigas:** os IDs P-00 a P-16 e seus tipos de métrica foram mantidos; só os cenários foram renomeados (o cenário ambiente da fase Build → `ausente`; os dois cenários de onda legados → `presente-onda-media` e `presente-onda-pesada`: cena 1D, **sem projéteis**, ADR-027). As metas de P-04 e P-05 são as herdadas das fichas anteriores e **não foram recalibradas** (SDD-22 RNF-22.1/22.5 define 1,2% e 0,8% para `strip-present`, P-17); revisão só com dados medidos. P-10, P-11 e P-12 usam a faixa de 96 DIP; P-13 continua sobre a simulação idle.

**Revisão de metas:** se P-00 (base do WPF) inviabilizar P-06, ou se P-04/P-05 reprovarem por causa do DWM, abrir uma ADR com as medições anexadas. As metas não podem ser alteradas sem dados.

### RF-16.2: Modo benchmark (`SideWar.exe --bench <cenario> --seconds <n> --out <arquivo.csv>`)

- Disponível em Release. Inicia sem carregar a sessão do usuário: usa uma pasta de dados temporária, `%TEMP%\SideWar-bench-<guid>`, apagada no fim.
- Cenários:
  - `hidden`, `idle-static`: estados fixos;
  - `presente-onda-media`, `presente-onda-pesada`: o `SanityBot` (SDD-12) monta as torres e uma onda sintética **infinita** mantém a população-alvo (20 inimigos e 6 torres; 44 inimigos e 12 torres com banner de pico e números de dano ativos), em Presente a 30 fps. A base é invulnerável no modo bench;
  - `soak`: `presente-onda-pesada` reiniciado a cada 10 min;
  - **Novos (idle):**
    - `strip-present` (alias de SDD-22 T-22.12; P-17): Presente, 24 inimigos, 12 torres, números flutuantes, 1 banner e 1 caixa, 30 fps;
    - `ausente`: o mesmo, em Ausente (simulação a 30 Hz, render a 8 fps, opacidade 128; alias `strip-away`); a presença é forçada por `FakeClock`/flag de bench, sem esperar 5 min;
    - `offline-retorno`: monta um `OfflineSnapshot` de **12 h** (teto de SDD-25 RF-25.6, 2.000 ondas) e mede só o **cálculo analítico** (`OfflineCalculator.Compute`, sem janelas, `--seconds` = número de repetições); o resultado é comparado campo a campo com o esperado (determinismo, SDD-25 RNF-25.1);
    - `painel-aberto`: Presente com o painel (SDD-24) aberto e cards visíveis;
    - `varias-janelas`: todas as janelas do conjunto de SDD-03 RF-03.9 visíveis ao mesmo tempo;
  - `e1-synthetic`: o quadro de teste de SDD-08 (RF-08.9).
- A cada 1 s, o CSV recebe uma linha: `t, fps, composeP50, composeP95, presentP50, presentP95, tickMean, tickP99, wakes, allocBytes, gen0, gen1, gen2, privateMB, workingSetMB, managedHeapMB, handles, gdiObjects, userObjects`.
- Ao fim de `--seconds`, executa o encerramento normal (SDD-07) e devolve exit 0.
- CLI adicional: `--version` imprime a versão informacional e sai com exit 0; `--diag` imprime um JSON com build do SO, DPI awareness do processo, monitores, pasta de dados e versão, e sai com exit 0. `--debug-crash` existe só em Debug (SDD-07).

### RF-16.3: `scripts/windows/measure-perf.ps1`

```
measure-perf.ps1 -Scenario presente-onda-pesada -Seconds 300 [-Exe <caminho>] [-Out artifacts\perf\<data>-<cenario>]
```

1. Registrar a máquina: CPU (`Get-CimInstance Win32_Processor`), núcleos lógicos, RAM, GPU (`Win32_VideoController`), versão do Windows, monitores, DPI e plano de energia (`powercfg /getactivescheme`). O resultado vai para `machine.json`.
2. Medir o **DWM em repouso** por 60 s (overlay não iniciado): CPU do `dwm.exe` e GPU, via `Get-Counter '\GPU Engine(*)\Utilization Percentage'` filtrado por pid.
3. Iniciar `SideWar.exe --bench …`, aguardar 10 s de aquecimento e amostrar a cada 1 s:
   - CPU do processo (`(Get-Process).TotalProcessorTime` em delta / (Δt · núcleos lógicos));
   - CPU do DWM (idem);
   - GPU do processo e do DWM (contadores `GPU Engine` com `pid_<pid>`).
4. Gerar `summary.json`: média, p95 e máximo de cada métrica, o delta do DWM, a comparação com as metas de RF-16.1 e o veredito `aprovado | reprovado` por meta.
5. Imprimir a tabela-resumo e o caminho dos arquivos. Os resultados são copiados para `specs/evidencias/perf/` quando a etapa for aprovada.

### RF-16.4: Painel de diagnóstico (Ctrl+Alt+F12, configurável; desligado por padrão)

- Desenhado no canto superior direito do campo, com fundo alfa 200: fps, compose p95, present p95, tick média, aloc/s, contagem de GC, private MB, wakes/s, modo, `HiddenReason`, `PauseReasons`, perfil de energia, veredito de fullscreen e retângulos de layout (contornos).
- Com o painel ligado, o render fica no mínimo a 2 fps, para atualizar os números. O painel em si não entra nas medições.

### RF-16.5: Contadores (`System.Diagnostics.Metrics`, `Meter("SideWar")`)

- Contadores: `sidewar.frames`, `sidewar.compose.ms` (histograma), `sidewar.present.ms`, `sidewar.tick.us`, `sidewar.wakes`, `sidewar.saves`.
- **Contadores de UIA (SDD-21; ADR-023):** `taskbar.queries` (consultas UIA executadas), `taskbar.updates` (layouts que mudaram; exibida também como **atualizações por minuto**), `taskbar.events.dropped` (eventos WinEvent descartados no callback), `taskbar.query.ms` (histograma: **ms por atualização**, da chamada ao resultado) e a **memória do `UIAutomationCore`**: working set e private bytes antes, durante e depois de 20 consultas (e após `GC.Collect`), com a thread `SideWar.Shell` viva (SDD-21 RNF-21.3, T-21.8). Os nomes `taskbar.query.ms` e o de memória são propostos aqui; os três primeiros são de SDD-21.
- O CSV de RF-16.2 ganha as colunas `taskbarQueries`, `taskbarUpdates`, `taskbarQueryMsP95` e `uiaRetainedMB`.
- Ficam disponíveis para `dotnet-counters monitor -n SideWar --counters SideWar` (ferramenta do usuário; não é dependência do app).

### RF-16.6: Configuração de runtime (`SideWar.App.csproj`)

```xml
<ServerGarbageCollection>false</ServerGarbageCollection>
<ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>   <!-- sem thread de GC em segundo plano -->
<TieredPGO>true</TieredPGO>
<PublishReadyToRun>true</PublishReadyToRun>                        <!-- SDD-18 -->
<UseSystemResourceKeys>false</UseSystemResourceKeys>
<InvariantGlobalization>false</InvariantGlobalization>             <!-- WPF e texto pt-BR -->
```

E `runtimeconfig.template.json`: `{ "configProperties": { "System.GC.ConserveMemory": 3, "System.GC.RetainVM": false } }`.

### RF-16.7: Regras de código obrigatórias (checadas em revisão; violação reprova o PR)

1. **Nunca** usar `timeBeginPeriod`, `CompositionTarget.Rendering`, `DispatcherTimer` para o jogo, `Thread.Sleep` em laços, polling sem deadline nem espera ativa.
2. Nada roda quando oculto, exceto o polling de fullscreen de RF-06.1. (Oculto = Offline, SDD-25: **0 frames, nenhuma consulta UIA, nenhum timer de evento ou de presença**; o poll de 60 s da barra é desligado, SDD-21 RF-21.6.)
3. Caminhos quentes (tick, render, entrada): sem LINQ, sem `string` nova, sem boxing, sem closures, sem `params`, sem `foreach` sobre interfaces e sem `async`.
4. Objetos Skia (`SKPaint`, `SKFont`, `SKPath`) criados uma vez e reutilizados. `using` só em criação e descarte de recursos fora do frame.
5. Pools e capacidades fixas para entidades, eventos, efeitos e agendas (SDD-11, SDD-08).
6. Renderizar só quando a `RenderPolicy` mandar. Ignorar o frame se não houver nada sujo.
7. Snapshots para a UI WPF só existem com uma janela consumidora aberta, e no máximo a 4 Hz.
8. Janelas WPF são fechadas (não ocultadas) ao sair.
9. Toda nova thread, timer ou hook precisa de uma linha nesta ficha (tabela RF-16.8), com a justificativa.

### RF-16.8: Inventário de atividades em segundo plano (deve permanecer atualizado)

| Atividade | Thread | Frequência | Quando ativa |
|---|---|---|---|
| Timer do jogo | T-Game | 30 Hz de simulação; render 30 fps (Presente) / 8 fps (Ausente) / evento | visível |
| Polling de fullscreen | T-Game | 0,5 Hz visível / 1 Hz com tela cheia rastreada | visível ou oculto por fullscreen |
| Polling do cursor (peek) | T-Game | 8 Hz | visível e Passive, peek ligado, perfil Normal |
| ~~Inatividade (`GetLastInputInfo`)~~ | — | **Removida:** substituída pelos timers de presença (abaixo; SDD-25 RF-25.1) | — |
| Timers de presença (`PresenceTracker.NextDeadline`: Presente → Ausente em 5 min; recolher do card em 60 s) e de eventos (SDD-26: sorteios de pico, caixa, banner e chefe) | T-Game | deadline único do host, **sem polling**; congelados em Ausente (eventos) e ausentes oculto | visível |
| Thread `SideWar.Shell` (UIA somente leitura, MTA) | `SideWar.Shell` | **sob demanda**, por evento, sem polling; encerrada com o app (SDD-21 RF-21.3, ADR-023) | visível (0 consultas oculto) |
| Poll de segurança da barra de tarefas (**condicionado**: só se a E1 mostrar que os botões XAML não emitem WinEvent; senão removido, ADR-032) | T-Game (agenda) → `SideWar.Shell` | a cada **60 s**, só com o overlay **visível** (deadline do host; SDD-21 RF-21.6) | visível |
| Debounce da reavaliação da barra (500 ms) e do widget (200 ms; z-order 100 ms) | T-Game | deadlines únicos reagendáveis, sem timer periódico | visível, após evento |
| Coalescência de créditos/taxa do widget | T-Game | ≤ 2 Hz, só se o texto mudou (SDD-23 RF-23.9) | visível |
| Flush de log | T-IO | 0,5 Hz, só se houver buffer | quando há log pendente |
| Tooltip da bandeja | T-UI | ≤ 1 Hz, só se mudou | sempre |
| Hooks WinEvent (foreground/fullscreen; processo do Explorer para a barra, SDD-21 RF-21.6; foreground na barra para z-order do widget, SDD-23 RF-23.10) | T-Game | por evento, descarte no callback | sempre (a consulta só visível) |
| Hotkeys globais (`RegisterHotKey`) | T-Game | por evento | sempre |
| Espera do evento de ativação (instância única) | ThreadPool (registered wait) | por evento | sempre |

## 6. Requisitos não funcionais

- **RNF-16.1:** O processo de medição é reprodutível: duas execuções do mesmo cenário diferem ≤ 20% na CPU média (se diferirem mais, a medição é inválida e precisa ser repetida com a máquina ociosa).
- **RNF-16.2:** O modo `--bench` não altera os dados do usuário.

## 7. Estratégia de implementação

1. E1: contadores internos, painel de diagnóstico mínimo, `--bench e1-synthetic|hidden|idle-static`, `measure-perf.ps1` e as medições P-00, P-01, P-02, P-11 e P-12 sobre o quadro de teste. **Esta é a decisão go/no-go da ADR-001.**
2. E3: benchmarks P-13 (SDD-17) e `offline-retorno` (P-19, lógica pura, também medível no Linux de forma indicativa).
3. E4/E5: cenários `presente-onda-media`, `presente-onda-pesada`, `ausente`, `painel-aberto` e `varias-janelas` com a simulação real.
4. E7: todas as metas e o soak de 2 h; otimizações (atualização parcial, ajuste de caches) dirigidas pelos dados.

## 8. Dependências

SDD-02 (scripts), SDD-08 (tempos de render), SDD-10 (política), SDD-12 (bot), SDD-21 (contadores e memória do UIA), SDD-22 (cenários `strip-*`), SDD-23 (widget), SDD-25 (offline e Ausente) e D-05 (máquina de referência).

## 9. Critérios de aceitação

- **CA-16.1 (E1):** `measure-perf.ps1` roda os cenários `e1-synthetic`, `hidden` e `idle-static` e gera `machine.json`, CSV e `summary.json`. P-00, P-01, P-02, P-11 e P-12 ficam com o status **medido**, aprovado ou reprovado, em `specs/evidencias/E1.md`.
- **CA-16.2 (E1, go/no-go):** se `e1-synthetic` (equivalente ao `presente-onda-pesada` em contagem de sprites) medir CPU de processo + DWM Δ > 2× (3% + 1,5%), abrir a ADR de contingência (ADR-001) **antes** de avançar para a E4.
- **CA-16.3 (E7):** todas as metas P-01 a P-16 medidas. As reprovadas têm ADR de revisão aprovada pelo usuário ou correção.
- **CA-16.4:** o inventário RF-16.8 confere com o código (revisão).
- **CA-16.5 (idle):** P-17 a P-22 medidos e registrados como aprovado ou reprovado; em particular o retorno de 12 h com cálculo ≤ 50 ms (P-19), Ausente ≤ 0,4% de CPU (P-18) e o custo de memória do UIA (P-22), todos hoje **metas não comprovadas**.

## 10. Cenários de teste

Os cenários de RF-16.2 executados por `measure-perf.ps1`, mais as verificações de reprodutibilidade (RNF-16.1). Cenários: `presente-onda-media`, `presente-onda-pesada`, `ausente`, `offline-retorno` (cálculo analítico de 12 h), `painel-aberto` e `varias-janelas`; a medição da UIA vem de SDD-21 T-21.8.

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Custo do DWM com layered em 4K ou monitores de 144 Hz | Média | Alto | Medir na E1; atualização parcial; contingência DirectComposition |
| Base de memória do WPF alta (dezenas de MB) | Alta | Médio | P-00 mede; janelas WPF fechadas; `ConserveMemory`; metas relativas a P-00 |
| Contadores de GPU por processo ausentes ou imprecisos em alguns drivers | Média | Baixo | Registrar "indisponível"; usar a CPU do DWM como proxy |
| Medição contaminada por outras cargas | Média | Médio | Fechar apps; plano "Equilibrado"; repetir duas vezes (RNF-16.1) |
| UIA (`UIAutomationCore`) reter memória ou custar CPU no Explorer além do previsto | Média | Alto | P-22 e SDD-21 RNF-21.3/21.5; thread sob demanda; interruptor `taskbar.useUia`; ADR se reprovar |
| Retorno offline de 12 h custar mais de 50 ms | Baixa | Médio | P-19; tabelas `PowTable`, limite de 2.000 ondas (SDD-25 RF-25.7) |
