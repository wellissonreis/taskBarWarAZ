# TD.30e — tipos de runa visíveis e tutorial de runas no 1º dano

Decisão do usuário (reverte a TD.30c, que ocultava o tipo até despertar): "A runa que o usuário vai upar NÃO pode ser oculta; ele tem que saber a árvore de runa que vai seguir."

## Entregue
- Todo nó da teia (modal e janela do HUD) mostra o glifo do seu ramo em qualquer estado: despertado (âmbar cheio), disponível (tracejado âmbar) e bloqueado (esmaecido, `Palette.Td.Dim` sobre `Palette.Td.Border`, contraste ≥ 3:1, testado). Os nós bloqueados cresceram (modal 12→22, HUD 8→17 DIP) para o glifo ser legível; a teia da modal ficou 688×318 para caber a legenda.
- Balão: sempre "Runa de <ramo>" + efeito do conteúdo + etiqueta (DESPERTADA / DISPONÍVEL / BLOQUEADA) + ação ("Efeito ativo", "Clique para despertar · 1 fragmento", "Sem fragmentos", "Desperte uma vizinha antes"). Removidos "Runa oculta" e "O tipo desta runa é revelado ao despertar".
- Legenda de ramos: modal com chips glifo + nome + n/total; janela do HUD com chips compactos (glifo + n/total, cabem em 268 DIP; a janela cresce 34 DIP). Passar o mouse num chip destaca (brilho + borda âmbar) os nós do ramo (`HoverBranch`, `TdUiItem.Selected` nos nós).
- Cartão "runas no 1º dano" (`TdUiController.RuneCard.cs`): dispara quando a integridade do núcleo cai pela 1ª vez; dica `runes-first-damage` em `td.tipsSeen` (uma vez por perfil, vale com `tutorialDone=true`); "Refazer tutorial" a esquece (`ITdOnboardingStore.ClearTip`, implementado no `TdUiPresenter`). Cantinho junto ao botão Runas (seta), sem foco e sem pausa, texto 14 DIP, botões "Abrir runas" (abre a janela e acende a legenda) e "Entendi"; fecha ao abrir a janela/modal ou após 20 s de exibição (relógio congela em pausa/boss key/outro cartão); espera a vez atrás de pergunta, aviso, fim de fase, janela de runas e modal; só aparece com a partida em jogo e o HUD aberto.
- Core intocado.

## Testes
- `TdRuneUiTests` (tipos visíveis em todos os estados, balão, legenda com contagens, destaque por ramo), `TdRuneTutorialTests` (1º dano, uma vez, refazer, boss key, pausa, fila, janela, 20 s), `TdUiGoldenTests` (contraste, glifo em todo estado, goldens).
- Goldens regenerados e revistos: runas modal 100/150, janela HUD 100/150, balões (disponível/despertada/bloqueada, modal e HUD), novos: `runesbranch`, `runewindowbranch`, `runecard`, `runewindowfromcard`.
