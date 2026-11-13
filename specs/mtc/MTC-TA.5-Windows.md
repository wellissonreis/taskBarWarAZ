# MTC-TA.5-Windows: cena sobre a barra (`SceneWindow` + `ZOrderCoordinator`)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Ficha: PLANO §8.1 TA.5 (parte Windows; CA-22.1, RNF-22.11, MTC-22.1). Depende de TA.1 e TA.2 (PLANO) e do spike TA.1 decidir `towersInBar`.
> **Pré-condição de código:** o REPORT TA.5 (parte WSL) registra como **pendentes** os pontos Windows: `DockController.Relayout` ainda usa `TowerHost.Band`; `SceneWindow` sobre a barra e `ZOrderCoordinator` reafirmando a cena (debounce 100 ms; eventos da barra) **não foram tocados**. Este checklist só vale após a parte Windows existir. Até lá o padrão é `towersInBar=false` (Band).

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-22.1-a | Iniciar o jogo com a barra embaixo em 100% | Cena de 104 DIP acima da barra, torres dentro dela, largura do monitor (SDD-22 MTC-22 #1; ADR-034) | | | | |
| MTC-22.1-b | Idem em 125% | Idem (SDD-22 #1) | | | | |
| MTC-22.1-c | Idem em 150% | Idem (SDD-22 #1) | | | | |
| MTC-22.1-d | Hospedeiro `Taskbar`: observar a altura da janela da cena | `abovePx = round(104·dpi/96)`; altura = `abovePx + round(HeightDip·scale)` (cobre a barra, sem margem de auto-hide) (REPORT TA.5; SDD-22 RF-22.1). Hospedeiro `Band`: só a cena, y −2 px com auto-hide | | | | |
| TA.5-e | Clicar em ícone, Iniciar, pesquisa e bandeja com o hospedeiro `Taskbar` | Torres voltam acima da barra em ≤ 150 ms; `ZOrderCoordinator` reafirma a cena em eventos de foreground em `Shell_TrayWnd`/`Shell_SecondaryTrayWnd` e `EVENT_OBJECT_REORDER`/`LOCATIONCHANGE`, com debounce de 100 ms e `SWP_NOACTIVATE` (SDD-21 DP-33.4 (a); RNF-22.11; mesmo critério de MTC-22.11 e T-22.14) | | | | |
| TA.5-f | Mudar monitor, DPI ou `WorkArea` | A cena é redimensionada e o host emite `LayoutChanged` a partir do `BuildZoneMap`; a faixa reposiciona em ≤ 1 s (REPORT TA.5, pontos de ligação; SDD-22 MTC-22 #9) | | | | |
| TA.5-g | Cena em Passivo sobre a barra | Um clique em qualquer ponto da janela (inclusive sobre torres e ícones da barra) chega à janela de baixo (`WindowFromPoint`; SDD-04 CA-04.1) (SDD-22 CA-22.3) | | | | |
