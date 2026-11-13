# MTC-SDD-19-20: módulo Azure e sistema visual (itens sem ficha dona no PLANO)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. O PLANO não cita MTC-19.x nem MTC-20.x em nenhuma ficha; os itens vêm direto das tabelas de MTC de SDD-19 e SDD-20. Atribuir a uma ficha (ex.: acabamento de E4, DP-20.1) é decisão do Tech Lead.

## MTC-19 (SDD-19)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-19.1 | Com outro app em foco e uma pergunta pendente; clicar no selo; fechar a `QuestionWindow` | A pergunta pendente **nunca** abre janela sozinha; o clique no selo abre a `QuestionWindow`; fechar devolve o foco ao app anterior (SDD-19 CA-19.8, tabela MTC-19.1) | | | | |
| MTC-19.2 | Jogar uma fase com módulo ativo em HUD de tamanho S | Selos e debrief legíveis (SDD-19 tabela MTC-19.2; risco: debrief temporário de 8 s). *SDD-23 mudou a HUD; "tamanho S" pode estar obsoleto, anotar* | | | | |
| MTC-19.3 | Conferir o aviso de analogias e os links AZ-900 | Aparecem como texto, sem navegação e sem rede (SDD-19 tabela MTC-19.3) | | | | |

## MTC-20 (SDD-20)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-20.1-a | Comparar os 7 ícones e a tipografia, no Windows, lado a lado com o artefato de design, em 100% | Idênticos a um PNG de referência (≤ 0,5% de pixels com ΔE > 8) (SDD-20 CA-20.2, tabela MTC-20.1) | | | | |
| MTC-20.1-b | Idem em 150% e 200% | Idem; risco: escalas fracionárias (125%, 175%) podem borrar traços de 1,5 px; conferir também 125% e 175% (SDD-20 §11) | | | | |
| MTC-20.2 | Comparar o ritmo de `adBlink`, `adBob`, `adPulse` e `adRise` com o artefato | Mesmo ritmo. `adBlink` = 4 estados de 275 ms: opacidade 1, 0,725, 0,45, 0,725; **o cálculo é do autor da ficha e deve ser validado aqui** (SDD-20 nota 1; risco: `steps()` interpretado de outro modo, ajustar `Motion`) | | | | |
| MTC-20.3 | Legibilidade de Silkscreen 9–11 DIP em 100% no monitor de referência (D-05) | Sem pixels desiguais; se reprovar, ADR de "ajuste de tamanhos à grade" (SDD-20 DP-20.1; risco: tamanhos 9, 11, 13 e 15 DIP sem AA) | | | | |
