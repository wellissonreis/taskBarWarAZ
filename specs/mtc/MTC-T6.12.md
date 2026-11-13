# MTC-T6.12: presença, offline e eventos (fechamento da E6)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Ficha: PLANO T6.12 (fechamento de CA-07.2, CA-07.3 e CA-07.5, MTC-07, cobertura e `specs/evidencias/E6.md`). Aprovação E6 exige "MTC-07.1..3 completos e a medição de P-19 (retorno de 12 h)" (PLANO E6). Em relação à E2 (`MTC-T2.7.md`), aqui a **gravação em disco** deve ser verificada.

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-07.1 | Win+L por 30 s e desbloquear | Overlay volta e o jogo retoma sozinho; com 30 s Offline não há card, só ganho direto; hash do estado antes do cálculo offline igual ao anterior (log); gravação em disco verificada (SDD-07 CA-07.2) | | | | |
| MTC-07.2 | Suspender e acordar | Overlay volta em ≤ 2 s após o desbloqueio, na posição correta; o **timestamp do arquivo de save < horário do sono** (SDD-07 CA-07.3) | | | | |
| MTC-07.3 | Logoff com a onda em andamento; logar de novo | Sessão restaurada no ponto do logoff, diferença de no máximo 1 tick (SDD-07 CA-07.5) | | | | |
| MTC-07.4 | Economia de bateria | Saver: ≤ 15 fps, sem animações ambientes, sem peek (SDD-07 RF-07.5). Ver `MTC-T2.7.md` | | | | |
| MTC-25.1 | Bloquear a tela, suspender, fechar e reabrir | O card correto aparece (SDD-25, tabela de MTC; CA-25.7: aparece em Offline ≥ 10 min, 9:59 não e 10:00 sim; COLETAR ×2 com acerto dobra e com erro coleta ×1; ignorado por 60 s recolhe para o badge "+X"; pendente sobrevive a save e restore; não ativa a janela). Roteiro detalhado em `MTC-T4.10.md` (T4.10b-1 a 11) | | | | |
| MTC-26.1 | Jogar 30 min presente e conferir picos, caixas e chefe | Picos, caixas de suprimentos e chefe aparecem conforme SDD-26 (SDD-26, tabela de MTC). Não há CA numérico na tabela; registrar observações | | | | |
| P-19 | `offline-retorno`: snapshot sintético de 12 h, até 2.000 ondas (só `OfflineCalculator.Compute`) | Tempo de cálculo ≤ 50 ms (SDD-16 P-19; SDD-25 RNF-25.2). *Cenário ainda sem suporte em `measure-perf.ps1`; ver `MTC-MED.md`* | | | | |
