# Evidência TD.16: ausência por clique fora, portal no cantinho, HUD acima da faixa

Validação ao vivo no Windows (espelho `C:\dev\sidewar-TD16`, Release, 1920×1080 a 100%, faixa 64 DIP). `%LOCALAPPDATA%\SideWar` copiado antes e restaurado depois; o App do usuário estava fechado e NÃO foi reaberto. Capturas recortadas (canto inferior direito) em `specs/evidencias/TD.16/`:

| Captura | O que mostra |
|---|---|
| 01-portal-right-hud-above-present | Portal na borda direita (junto ao relógio), inimigos nascendo dali; HUD flutuando acima da faixa, alinhado à direita, faixa livre; 100% (mouse sobre o jogo) |
| 02-click-away-faded | Clique fora das janelas do SideWar (outro app): faixa e HUD esmaecidos |
| 06-start-other-app-faded | Início com outro app em foco: esmaecido |
| 07-clicked-game-present | Clique no HUD do jogo (janela no-activate, foreground não mudou): 100% |
| 08-clicked-back-faded | Clique de volta no outro app (sem ForegroundChanged): esmaece (~50%) |
| 09-hover-present-again | Mouse sobre a faixa: volta a 100% |

Nota: a validação usou o VS Code como "outro app" (Bloco de Notas abriu atrás); mesmo mecanismo (clique fora das janelas do processo).
