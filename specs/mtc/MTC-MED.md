# MTC-MED: medições de desempenho (T1.15, T4.12, T7.1, T7.5)

> Formato: SDD-17 RF-17.6 (resultado em branco). Estas linhas não são MTC no SDD; são as folhas de execução das medições do SDD-16 que o PLANO atribui a fichas Windows (T1.15 go/no-go, T4.12, T7.1, T7.5). Rótulo obrigatório do resultado: **medido** ou **reprovado**; "não medido" enquanto não rodar (SDD-16 §5, CA-16.3/16.5). Metas só valem na **máquina de referência (D-05)** com `machine.json`; números do desktop isolado (GPU virtual) devem ser rotulados como tal e não substituem a medição oficial (recomendação do preparador, a confirmar pelo Tech Lead).
> **Estado do `measure-perf.ps1`** (REPORT T1.14): implementa só os cenários `e1-synthetic`, `hidden`, `idle-static` e `baseline-wpf` (e `e1` = os quatro). Os demais cenários (`presente-onda-media`, `presente-onda-pesada`, `strip-*`, `soak`, `ausente`, `painel-aberto`, `varias-janelas`, `offline-retorno`) **não existem** no script; o script recusa nome desconhecido ("Unknown scenario"). Cada linha abaixo indica se há como rodar hoje.

| ID | passos | esperado (fonte) | resultado (medido/reprovado) | evidência | data | build |
|---|---|---|---|---|---|---|
| T1.15-go | `measure-perf.ps1 -Scenario e1 -Seconds 300 -Repeats 3 -DwmRestSeconds 60` (recomendação do REPORT T1.14; sem `-NoInput` se quiser Ctrl+Alt+G/H e bandeja) | **CA-16.2 (go/no-go da E1):** `e1-synthetic` com CPU de processo + DWM Δ > 2× (3% + 1,5%) → abrir a ADR de contingência (ADR-001) antes da E4 (SDD-16 CA-16.2). Rodar na máquina de referência sem outros workers | | | | |
| P-00 | `-Scenario baseline-wpf` | Registrar (base para P-06) | | | | |
| P-01 | `-Scenario hidden -Seconds 300` | ≤ 0,1% CPU · ≤ 1 wake/s · 0 frames (SDD-16 P-01; SDD-07 CA-07.7) | | | | |
| P-02 | `-Scenario idle-static -Seconds 300` | ≤ 0,2% CPU · ≤ 0,1 fps (SDD-16 P-02; SDD-22 RNF-22.6: 0 Presents em 30 s sem input) | | | | |
| P-11 / P-12 | `e1-synthetic` (primeira passada; ficha T4.12) | Composição p50/p95 ≤ 2,5/4 ms; `UpdateLayeredWindowIndirect` p50/p95 ≤ 1/2 ms (SDD-16 P-11, P-12). Amostra curta de referência no REPORT T1.14: P-11 p50 0,46/p95 2,0 ms; P-12 p50 0,29/p95 0,85 ms (amostra curta, máquina compartilhada, não oficial) | | | | |
| P-17 | Cenário `strip-present` (**não existe no script**) | ≤ 1,2% CPU · ≤ 0,8% DWM Δ · composição p95 ≤ 2 ms (SDD-16 P-17; SDD-22 RNF-22.1/22.5) | | | | |
| P-18 | Cenário `ausente`/`strip-away` (**não existe no script**) | ≤ 0,4% CPU · ≤ 0,3% DWM Δ · 8 ± 1 Presents/s (SDD-16 P-18; SDD-22 RNF-22.4) | | | | |
| T4.12-atlas | Memória do atlas | Registrar (PLANO T4.12; E4 exige "memória do atlas e CSVs dos cenários") | | | | |
| P-22 | Atualização da barra (UIA); T-21.8 com saída CSV | ≤ 2/min visível, 0 oculto; atualização ≤ 30 ms sem contar a consulta UIA; memória retida registrada (SDD-16 P-22; SDD-21 RNF-21.1/21.2). REPORT T1.14: P-22 não medido | | | | |
| T7.1 | Rodada completa P-00..P-22 em `specs/evidencias/perf/` | Todas as metas com rótulo medido aprovado ou reprovado; reprovadas com correção ou ADR aprovada pelo usuário (PLANO E7, Aprovação; SDD-16 CA-16.3/16.5). Depende de D-05 | | | | |
| P-07 (T7.5) | Soak de 2 h: `presente-onda-pesada` em loop (**não existe no script**) | Δ private ≤ 5 MB · Δ handles/GDI/USER ±5 (SDD-16 P-07) | | | | |
| P-19 | `offline-retorno` (**não existe no script**) | Cálculo de 12 h ≤ 50 ms (SDD-16 P-19; SDD-25 RNF-25.2) | | | | |
| P-14 | Latência F11 → oculto / Esc → visível | ≤ 500 ms / ≤ 1 s (SDD-16 P-14; ver `MTC-T2.7.md` MTC-06.2 e 06.4) | | | | |
