# MTC-TA.8: modo construção (plataforma Windows)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Ficha: PLANO §8.1 TA.8 (CA-22.11, MTC-22.12, CA-15.8; depende de TA.7 e T5.3). Evidências automatizadas: T-22.13 (Linux).
> **Pré-condição de código:** TA.8 **não existe ainda**. O REPORT TA.7 lista o que a TA.8 precisa ligar (captura do arraste com `SetCapture`, ESC por `RegisterHotKey` temporário (DP-33.8), cursores `IDC_CROSS`/`IDC_NO`, cancelamentos, banner de pico oculto). O REPORT T4.10 confirma que o App atual não tem o modo construção real nem o ESC dinâmico.

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-22.12-a | Arrastar uma torre do Arsenal para a barra e soltar em **zona livre** | Constrói só na zona livre; clique válido emite um comando e sai do modo (SDD-22 MTC-22 #12; CA-22.11) | | | | |
| MTC-22.12-b | Idem, soltar **em cima de ícone** | Laranja não constrói; clique inválido não emite comando (SDD-22 #12; CA-22.11) | | | | |
| MTC-22.12-c | Idem, soltar **acima da barra** | Laranja não constrói (SDD-22 #12; CA-22.11: "realces laranja ... em toda a área acima da barra") | | | | |
| MTC-22.12-d | Durante o arraste, olhar os realces e o fantasma | Realces laranja nos trechos `Blocked` e acima da barra, verdes nos trechos livres; fantasma verde/laranja conforme o `PlacementValidator`; cursor cruz se válido, proibido se inválido (CA-22.11; REPORT TA.7: `IDC_CROSS` se `Ghost.Valid` senão `IDC_NO`) | | | | |
| MTC-22.12-e | Pressionar **ESC** durante o modo | Cancela (SDD-22 #12; CA-22.11). O ESC é registrado por `RegisterHotKey` temporário só durante o modo (PLANO TA.8; DP-33.8) | | | | |
| MTC-22.12-f | **Clique direito** durante o modo | Cancela (SDD-22 #12; CA-22.11) | | | | |
| MTC-22.12-g | Ocultar o overlay durante o modo; e esperar **30 s** sem movimento | Cancela (PLANO TA.8: "cancelamentos (clique direito, ocultar, 30 s)"; REPORT TA.7: timeout de 30 s sem movimento) | | | | |
| MTC-22.12-h | Clicar no painel durante o modo (fora do ícone) | Cancela o modo (PLANO §8: "o clique no painel cancelando o modo construção" aceito; REPORT TA.7: `Cancel(PanelClick)`) | | | | |
| MTC-22.12-i | Provocar banner de pico/chefe durante o modo; sair do modo | Banner de pico oculto durante o modo e restaurado ao sair (CA-22.11) | | | | |
| MTC-22.12-j | Entrar no modo com a cena em Passivo | O modo força Interativo (lembra o anterior) e a saída volta ao modo anterior (REPORT TA.7, `OverlayModeController.SetBuildMode`) | | | | |
| MTC-22.12-k | Com o Bloco de Notas/VS Code em foco digitando, arrastar do painel até a cena | O foreground não muda (SDD-22 T-22.5 / SDD-15; regra geral de SDD-03 CA-03.2 para as janelas do jogo) | | | | |
