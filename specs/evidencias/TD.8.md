# Evidência TD.8: HUD, colocação, inspetor e fluxo da partida

Jogado de verdade no Windows (1920×1080, 100%, gameMode padrão = Td) com mouse injetado por script (SetCursorPos/mouse_event), app do espelho `C:\dev\sidewar-TD8` (Debug). Capturas recortadas só em torno do HUD/faixa e do popover (`specs/evidencias/TD.8/*.png`).

Verificado jogando a fase 1:
- HUD na área de 160 DIP (INT, $, ONDA n/N, INICIAR/CHAMAR +bônus, 3 habilidades com recarga, TORRE, menu).
- Construir: botão TORRE (ou clique num ponto livre da faixa) → fantasma + popover de torres (custo, desbloqueio, fraqueza) → construir Raio e Gelo (créditos descontados); ESC (hotkey temporário) e botão direito cancelam sem comando (`40`/`41`/`42`); funciona durante a onda.
- Inspetor (`26`): melhorar (custo) `31`, mira (5 modos), vender 70% (`71`).
- Iniciar onda, Chamar próxima (bônus), habilidade Escalar (ON → recarga, `29`).
- Menu, preview da próxima onda (`44`), Mapa de fases e árvore de selos (`45`/`46`).
- Fim de partida: card de Derrota (`50`) com Repetir (nova partida na fase) e Mapa.
Não observado ao vivo: Vitória (coberta por golden), caixa-bomba (não caiu nas ondas jogadas), ramo A/B e reposicionar SEM POSIÇÃO (cobertos por teste de controlador).
%LOCALAPPDATA%\SideWar copiado antes e restaurado depois; app do usuário reaberto (modo padrão).
