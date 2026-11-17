# TD.24c — evidências

Goldens Linux revistos visualmente: `td-ui-defeat-100`, `td-ui-victory-100` e `td-ui-menu-100` (e variantes 150% geradas), além dos cartões de derrota do tutorial. O resultado tem botões grandes e a aba Jogo expõe reinício com confirmação. Sem execução interativa: a instância do usuário não foi manipulada.

O controlador só mostrava o resultado uma vez; ao fechar, o chip abria Construir, embora o Core estivesse em fase terminal. Agora o chip reabre o resultado. A troca de fase fecha a modal antes de substituir a partida, para descartar a fila de comandos anterior.
