# TD.30c — UI das runas (botão no HUD, hover, janela)

- Botão Runas (estrela) no chip; ponto âmbar só com fragmento + nó a despertar; sem popup/animação/abertura automática. Chip em repouso = 220 DIP (chip 108, 4 botões de 24).
- Janela de runas (288 DIP) ancorada no botão com seta; abre/fecha por clique no botão, Esc, X, boss key, botão direito na cena; não pausa, não toma foco. Link "Teia completa" abre a modal na aba Runas.
- Hover (modal e janela): nó cresce 20%, balão com nome/etiqueta/efeito/ação, sempre dentro da janela (flip via TdRuneTipLayout). Tipo oculto até despertar (item e balão não carregam o tipo).
- Teia real do Core (32 nós), arcos curvos, halo no último despertado.
- Goldens revistos: td-ui-runes*, td-ui-runewindow*, td-ui-runewindowtip*, td-ui-runestip*, HUD e td-tut-* (layout do chip).
- Limitações: clique fora da janela (no app de baixo) não é observável sem hook global de mouse; o clique em nó disponível envia AwakenRune e o Core sorteia entre os disponíveis (pode não ser o nó clicado).
