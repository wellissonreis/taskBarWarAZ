# MTC-TA.13: fechamento da faixa sobre a barra (MTC-21 1 a 13, MTC-22.11 a 22.13)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Ficha: PLANO §8.1 TA.13: "MTC-21 (1 a 13), MTC-22.11 a 22.13, `strip-present` com 24 torres (RNF-22.1, RNF-22.5), atualização de `specs/evidencias/E1.md` e `E4.md`". Depende de TA.1 a TA.11. Critérios: CA-21.2, CA-22.3, RNF-22.1.
> **Pré-condição de código:** TA.1, TA.5-Windows, TA.8 e TA.10-Windows ainda pendentes (PLANO §8). Esta é a rodada de fechamento: **repetir** MTC-21.1 a 21.12 com a cena sobre a barra. O texto completo de cada linha MTC-21.1..12 está em `MTC-T1.15.md` e vale igual aqui (SDD-21 §9); abaixo, as linhas são resumidas para não divergir da fonte.

## MTC-21 (SDD-21 §9, Windows 11, jogo visível)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-21.1 | Barra centralizada, 6 apps fixados | Ver `MTC-T1.15.md` MTC-21.1 (SDD-21 #1) | | | | |
| MTC-21.2 | Barra à esquerda | Ver `MTC-T1.15.md` (SDD-21 #2) | | | | |
| MTC-21.3 | Widgets do Windows ligado | `WindowsWidget` bloqueado (SDD-21 #3) | | | | |
| MTC-21.4 | Ocultação automática | Fallback `Band`, margem 2 px, nenhuma torre sobre a barra (SDD-21 #4) | | | | |
| MTC-21.5 | Ícones pequenos / barra baixa | `HeightDip` novo; torres centralizadas na barra (SDD-21 #5) | | | | |
| MTC-21.6 | Muitos apps (> 12) | `SEM POSIÇÃO` e retorno ao fechar (SDD-21 #6; DP-33.1) | | | | |
| MTC-21.7 **(2 monitores)** | Barra no monitor 2 / jogo no 1 sem barra | Layout correto / fallback (SDD-21 #7) | | | | |
| MTC-21.8 | Reiniciar o Explorer | Volta a ler a barra em ≤ 5 s (SDD-21 #8) | | | | |
| MTC-21.9 | Fixar/desafixar app | Zonas em ≤ 1 s; `SEM POSIÇÃO` e retorno (SDD-21 #9) | | | | |
| MTC-21.10 | Mudar escala 125% → 150% | Reposiciona em ≤ 1 s (SDD-21 #10) | | | | |
| MTC-21.11 | Barra para cima | Fallback; faixa na borda inferior da área de trabalho (SDD-21 #11) | | | | |
| MTC-21.12 | Abrir/fechar apps | Nenhuma torre sobre ícone; ≤ 2 atualizações/min em regime (SDD-21 #12) | | | | |
| MTC-21.13 | **ADR-034:** clicar num ícone, no Iniciar e na bandeja com torres visíveis | Torres voltam acima da barra em ≤ 150 ms (DP-33.4); o clique chegou ao Windows (SDD-21 #13) | | | | |

## MTC-22.11 a 22.13 (SDD-22 §9, Windows 11)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-22.11 | Clicar em ícones, Iniciar, pesquisa e bandeja com torres visíveis | O Windows recebe o clique; as torres voltam acima da barra em ≤ 150 ms (DP-33.4) (SDD-22 #11) | | | | |
| MTC-22.12 | Arrastar uma torre do Arsenal para a barra; soltar em zona livre, em ícone e acima da barra; ESC e clique direito | Constrói só na zona livre; laranja não constrói; ESC/clique direito cancelam (SDD-22 #12). Detalhe em `MTC-TA.8.md` | | | | |
| MTC-22.13 | Abrir 6 apps com a barra centralizada e fechá-los | Torres atingidas somem (`SEM POSIÇÃO`) e voltam ao fechar (DP-33.1) (SDD-22 #13) | | | | |

## Medição de fechamento

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| TA.13-strip | Cenário `strip-present` com **24 torres** (SDD-22 T-22.12; SDD-16 P-17) | CPU proc ≤ 1,2% · DWM Δ ≤ 0,8% · composição p95 ≤ 2 ms (SDD-16 P-17; SDD-22 RNF-22.1/22.5). *`measure-perf.ps1` hoje só implementa `e1-synthetic`, `hidden`, `idle-static`, `baseline-wpf` (REPORT T1.14); o cenário `strip-present` exige o bench correspondente na base* | | | | |
| TA.13-ev | Atualizar `specs/evidencias/E1.md` e `E4.md` | PLANO TA.13 | | | | |
