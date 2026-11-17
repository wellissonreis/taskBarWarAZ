# TD.24a — modal central e pausa na UI

Implementação sobre TD.22 (`781412c`). Nenhum arquivo do Core ou Core.Tests alterado por esta ficha.

## Comportamento

- Chip no canto, acima da faixa, com integridade/onda/créditos e ações rápidas Iniciar onda e Coletar caixa. A faixa segue click-through fora do posicionamento; não há inspetor nem coleta por clique no mapa. Ctrl+Alt+C preservado.
- Janela própria ativável/topmost, 720×480 DIP, DPI do monitor da barra, centralizada na área útil e limitada a 90% dela. Scrim só no monitor da barra; clique fora, X e Esc fecham. Foco anterior restaurado. Abas Construir, Runas (Em breve), Torres, Jogo e Como jogar; teclas 1–5, Tab/setas e Enter.
- Contador de modais: só a transição 0→aberta envia Pause; só o último fechar envia Resume. Trocar abas/inspetor não alterna pausa. Host deixa de agendar ticks pausados; log atribui Seq crescente aos comandos no mesmo Frame.
- **Escolha de posicionamento autorizada pelo briefing:** escolher o tipo oculta a modal e envia Resume. A onda corre durante o posicionamento, preservando seu risco e evitando congelar inimigos para posicionar. Clique válido constrói e volta à aba Construir (Pause); Esc/direito/15 segundos cancelam. Reposicionar retorna à lista de Torres.
- Inspetor (mira/venda), vitória, derrota e pergunta-bônus usam a mesma janela central. Pergunta usa relógio de parede; erro, expiração e fechar registram AnswerBonus(false), sem penalidade. O resultado permanece pausado durante a breve confirmação visual; fechar/terminar essa confirmação retoma. Uma pergunta coexistindo com outra aba mantém a pausa até o último fechar.
- Aba Jogo: faixa 40/64/88 (ao reiniciar), auto-início, som, volume, perguntas e esmaecimento; salva nas configurações existentes. Ações de tempo não são enviadas enquanto aberta; habilidades prontas oferecem Usar e fechar (Resume antes de UseAbility). Compras/vendas/mira funcionam pausadas.
- Modal aberta mantém presença e opacidade normal. Ocultar a cena cancela modal/posicionamento; a pausa de fullscreen/ocultação continua sob o AppStateMachine.
- Tutorial inicial: chip → abrir modal → fechar; frases curtas. Como jogar atualizado para escolher torre antes do ponto. Testes das expectativas antigas de HUD expandido/popover foram substituídos por integrações do fluxo central.

## Goldens

Novos: `td-ui-{runes,help,defeat}-{100,150}.png`. Atualizados os goldens `td-ui-*`/`td-tut-*` afetados pela troca de HUD/popover, pausa e TD.22; goldens idle não alterados.

Capturas sintéticas da modal em [TD.24a/](TD.24a/): Construir, Runas, Torres, Jogo, Como jogar, vitória, derrota e pergunta em 100% e 150%. Revisão visual: dimensão 720×480/1080×720, abas e estado selecionado, créditos, botões de construção/venda, configuração e V/F; sem captura da área de trabalho do usuário.

## Verificação

Resultados finais e SHA-256 registrados no REPORT da ficha. Build Release, suites Linux e Windows no espelho exclusivo `/mnt/c/dev/sidewar-TD24a`, e `git diff --check`.

**Sem validação ao vivo:** nenhuma instância do App aberta, fechada ou manipulada. Testes interativos de Platform.Windows/App.SmokeTests não executados. Foco Win32, scrim e click-through reais ainda requerem validação manual autorizada; nesta ficha houve compilação Windows e testes puros/visuais.
