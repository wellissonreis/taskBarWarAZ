# Evidência TD.16b: faixa click-through, mapa só para adicionar torres, caixa por botão/atalho

Imagens em `specs/evidencias/TD.16b/` (goldens revisados): `td-strip-placing-100.png` (modo posicionar: borda e véu menta, fantasma), `td-ui-hudcrate-100.png` (botão "Coletar caixa" destacado), `td-ui-hudplacing-100.png` (Construir vira Cancelar), `td-ui-towers-100.png` (Menu > Torres com Vender), `td-ui-hudprep-100.png` (HUD com o botão "Caixa" desabilitado).

Goldens regenerados (HUD ganhou o botão de caixa e "Cancelar"; Menu ganhou "Torres"; passos do tutorial mudaram de texto): td-ui-hud{prep,wave}, td-ui-hud-band{40,88}, td-ui-menu, td-tut-{ability,build,core,crate,credits,help,hudcollapse,wave}. Novos: td-strip-placing, td-ui-hudcrate, td-ui-hudplacing, td-ui-towers. Nenhum StateHash/golden do Core mudou (tudo é UI/host).

Validação ao vivo: NÃO feita (o App do usuário roda em C:\dev\sidewar e o SideWar tem instância única; não foi fechado nem reaberto).
