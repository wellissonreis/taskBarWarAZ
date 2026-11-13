# MTC-TA.1: spike de z-order e clique sobre a barra (decide `towersInBar`)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até o usuário executar; nunca presumir. Ambiente: desktop isolado (ver `README.md`). Ficha: PLANO §8.1 TA.1; evidência final em `specs/evidencias/TA.1.md`.
> **Pré-condição de código:** o spike TA.1 (janela layered topmost sobre a barra, com 3 torres de teste nas zonas livres) é descartável e **ainda não existe** nesta base (PLANO §8.1: TA.1 é ficha pendente). Este checklist só pode ser executado depois que a ficha TA.1 gerar o build do spike.

**Decisão que este checklist alimenta (SDD-21 DP-33.4 (c)):** se a torre ficar coberta por mais de 150 ms ou cintilar de forma visível em MTC-21.13, desligar `towersInBar` por padrão e usar o hospedeiro `Band`. Sem o spike, `towersInBar` fica `false` (PLANO §8, nota de ambiente). Recomendação final deve ser registrada em `specs/evidencias/TA.1.md`.

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência (captura ou log) | data | build |
|---|---|---|---|---|---|---|
| MTC-21.13 / TA.1-a | Com as 3 torres de teste visíveis nas zonas livres, clicar **num ícone da barra** | Torres voltam acima da barra em ≤ 150 ms; o clique chegou ao Windows (SDD-21 MTC-21 #13; DP-33.4) | | | | |
| MTC-21.13 / TA.1-b | Idem, clicar no **botão Iniciar** | Idem (SDD-21 MTC-21 #13) | | | | |
| MTC-21.13 / TA.1-c | Idem, clicar na **bandeja** | Idem (SDD-21 MTC-21 #13) | | | | |
| MTC-22.11 / TA.1-d | Idem, clicar na **pesquisa** | O Windows recebe o clique; torres voltam acima da barra em ≤ 150 ms (SDD-22 MTC-22 #11; DP-33.4) | | | | |
| TA.1-e | Passar o mouse/clicar nas **miniaturas** dos apps da barra | Medir o reaparecimento das torres após o clique (PLANO TA.1: "medir o reaparecimento após clicar em ícone, Iniciar, pesquisa, bandeja e miniaturas"). Meta numérica: ≤ 150 ms (DP-33.4) | | | | |
| TA.1-f | Clicar na barra repetidas vezes (rajada) e observar a cena | Sem cintilação visível das torres (critério de reprovação de DP-33.4 (c)); anotar tempo de reaparecimento e quantidade de `SetWindowPos` (T-22.14, SDD-22 §10) | | | | |
| TA.1-g | Cena em **Passivo**: clicar sobre torres de teste e sobre ícones da barra sob a janela | Todo clique atravessa para a janela de baixo, pois em Passivo a janela inteira é `WS_EX_TRANSPARENT` (SDD-21 DP-33.4 (b); SDD-22 CA-22.3) | | | | |
| TA.1-h | Cena em **Interativo**: clicar numa torre de teste, em espaço livre da barra e num ícone | Só pixels de torre recebem clique; o resto (alfa 0) atravessa para os ícones (SDD-21 DP-33.4 (b); SDD-22 CA-22.4) | | | | |
| TA.1-i | Registrar a **recomendação** para `towersInBar` padrão | Texto em `specs/evidencias/TA.1.md`: ligar (`Taskbar`) ou manter `false` (`Band`), com base nas linhas acima (PLANO TA.1) | | | | |

Observação (T-22.14, automatizado, WindowsInteractive): a rajada de cliques em `Shell_TrayWnd` com tempo até reaparecer e contagem de `SetWindowPos` é um teste automatizado; os resultados dele complementam, e não substituem, as linhas manuais acima.
