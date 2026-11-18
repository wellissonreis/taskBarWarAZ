# TD.33d: dinheiro muito visível (SDD-33 peça E)

Só apresentação (Runtime/Rendering/App/Persistence); nada em `src/SideWar.Core`, no `StateHash` ou no log.

- Contador de créditos: item próprio do HUD (`TdUiKind.Money`), fonte 16 DIP, âmbar (`Palette.Td.Amber`), mono (tabular), alinhado à direita; cresce para a esquerda após 4 dígitos. O chip ficou só com integridade e onda (58 DIP); repouso = 220 DIP até 9.999 créditos.
- Contagem animada (`TdMoneyModel`): ease-out 0,4–0,6 s, queda imediata, valor real nunca atrasa.
- "+N" agregados (janela 0,4 s, máx. 3, sobe 12 DIP, some em 0,7 s) numa faixa transparente no topo da janela do chip (só enquanto há números; click-through).
- Renda "+X/s" (média de 300 ticks de jogo, só ganhos; vendas não contam) com `td.showIncome` (padrão desligado).
- Brilho âmbar no botão Torres (≈0,8 s) uma vez por cruzamento ↑ do custo (torre escolhida ou mais barata desbloqueada); marcos 1k/10k/100k.. com brilho de 1 s no contador, uma vez por corrida.
- `td.discreetMode` (padrão desligado): sem brilhos nem contagem; "+N" só de ganhos ≥ 50.
- Boss key: modelo para, nada é agendado; ao voltar mostra o valor real sem animar. Pausa/modal congelam o relógio da animação. Sem animação, nenhum redesenho extra; com animação, ≈30 fps.
- Goldens regenerados e revistos: `td-ui-hud*`, `td-tut-*` (chip novo) e novos `td-ui-hudmoney-100/150`.
- Testes: `TdMoneyTests` (T-33.18 a T-33.23), `SettingsTdTests` (opções novas e compatibilidade).
