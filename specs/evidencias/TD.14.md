# Evidência TD.14: faixa configurável, HUD legível/minimizável, esmaecer por foco

Validação ao vivo no Windows (espelho `C:\dev\sidewar-TD14`, build Release, tela 1920×1080 a 100%, faixa padrão 64 DIP, `%LOCALAPPDATA%\SideWar` copiado antes e restaurado depois; o App do usuário não estava rodando). Capturas em `specs/evidencias/TD.14/`:

| Captura | O que mostra |
|---|---|
| 05-restored.png | Faixa de 64 DIP, HUD legível: "Núcleo 20/20", "Créditos 220", "Onda 1 de 10", botão "Chamar próxima +N", habilidades Escalar/Firewall/Snapshot (as duas últimas com cadeado), "Construir", Menu e "–" |
| 02-tooltip-core.png / 03 / 09 | Tooltip com texto completo ("Integridade do núcleo: se chegar a 0 você perde a fase…") acima do HUD |
| 04-minimized.png | HUD recolhido ao chip (núcleo e onda) no canto da faixa junto à barra |
| 05-restored.png | Chip clicado: HUD completo de volta |
| 06-faded-other-app.png | Outro app (Bloco de Notas) ativado: faixa e HUD esmaecidos (~50%) |
| 07-restored-by-hover.png / 09 | Mouse sobre a faixa / clique no HUD: 100% de novo |

Goldens (Linux): `td-band-{wave,prep-range,unpositioned}-h{40,64,88}-100`, `td-ui-hud{prep,wave,min,tip}-{100,150}`, `td-ui-hud-band{40,88}`.
