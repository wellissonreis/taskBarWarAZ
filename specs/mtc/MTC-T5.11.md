# MTC-T5.11: UI jogável (MTC-22, 23, 24 e pendências Windows de T5.2, T5.3, T5.8, T5.9)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Ficha: PLANO T5.11 (playtest, MTC-22/23/24, `specs/evidencias/E5.md`; CA-22.3, CA-23.2, CA-24.2). MTC-22.1, 22.2, 22.8, 22.9 estão em `MTC-T1.15.md`; MTC-22.11 a 22.13 em `MTC-TA.13.md`; MTC-23.1/23.3 (geometria do widget) em `MTC-TA.10-Windows.md`. Aqui ficam as linhas restantes. PLANO §8.2 re-escopa a T5.11 para incluir MTC-21.13 e MTC-22.11 a 22.13.
> As seções "NÃO medido" dos REPORTs T5.2, T5.3, T5.8 e T5.9 são a base dos itens de pendência abaixo.

## MTC-22 (SDD-22 §9, Windows 11)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-22.3 | Ctrl+Alt+G, clicar em torre e em espaço livre da barra | Arsenal abre focado na torre; clicar entre as torres atravessa para a barra; sem mudança de foreground (SDD-22 #3; CA-22.4) | | | | |
| MTC-22.4 | Ficar 5 min sem input | Cena a 50%, selo central com o tempo, borda cinza; presente → ausente em ≤ 16 ms; 8 ± 1 Presents/s (SDD-22 #4; CA-22.6) | | | | |
| MTC-22.5 | Voltar a usar o PC | Cena a 100%, borda âmbar, sem o selo (SDD-22 #5) | | | | |
| MTC-22.6 | Mostrar banner de pico e de chefe (modo de teste) | Banner de 36 de altura no topo, centralizado, anel pulsando, clicável com a cena em Passivo (SDD-22 #6) | | | | |
| MTC-22.7 | Mostrar a caixa de suprimentos | 52×44 com o sprite piscando, clicável (SDD-22 #7) | | | | |
| MTC-22.10 | Chrome em F11 | Cena, selo e hotspots ocultos; 0 frames e 0 wakes; reexibir em ≤ 1 s com o conteúdo correto (SDD-22 #10; CA-22.9) | | | | |

## MTC-23: widget (SDD-23, tabela de MTC)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-23.2 | Com o VS Code (ou Bloco de Notas) em foco digitando, clicar nos 4 botões e no resto do widget (100 cliques) | `GetForegroundWindow()` não muda e o texto continua indo ao app (SDD-23 CA-23.2). Pendente de T5.2 ("SendInput/mouse físico ... MTC-23.2/23.5"). Em SDD-15, MTC-15.1 passou a ser "jogar 30 min só com o mouse, sem mudar o foco" (= MTC-23.2) | | | | |
| MTC-23.2-b | Jogar 30 min só com o mouse, sem tocar no teclado, com o VS Code em foco o tempo todo | O foco não muda (SDD-15, nota de CA-15.3/MTC-15.1 substituído por SDD-23 MTC-23.2) | | | | |
| MTC-23.4 | Clicar na barra e no Menu Iniciar com o widget visível; observar cintilação e ordem | O widget volta a ficar acima da barra em ≤ 150 ms; sem cintilação (SDD-23 CA-23.7; risco "`Shell_TrayWnd` reassumir o topo") | | | | |
| MTC-23.5 | Clicar com o botão direito no widget; escolher itens | Menu nativo com Modo interativo, Pausar/Retomar, Ocultar, Monitor, Configurações, Sobre, Sair; o foreground não muda ao final e o nav não é acionado; "Modo interativo" alterna o modo da faixa; o foco volta ao app anterior (SDD-23 CA-23.11; tabela MTC-23.5) | | | | |
| T5.2-cursor | Passar o mouse sobre botões e áreas do widget (mouse físico) | Cursor mão real e hover (REPORT T5.2: "NÃO: cursor mão real"; SDD-23 RF-23.8/23.9) | | | | |
| T5.2-dpi | Mudar o DPI/monitor com o widget visível | `WM_DPICHANGED` tratado, widget redesenhado (REPORT T5.2: "NÃO medido: WM_DPICHANGED") | | | | |

## MTC-24: painel e cards (SDD-24, tabela de MTC)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-24.1 | Comparar painel e cards lado a lado com o artefato (100/150/200%); medir alturas de linha com os TTF reais | Painel 400×548 (janela 406×554), `left = faixa.Left + W − 532`, base = `stripTop − 8`; card de pergunta 520×242 e de retorno 472×612, centralizados; sombra (+6,+6) DIP (SDD-24 CA-24.3) | | | | |
| MTC-24.2 | Com o VS Code (Bloco de Notas) em foco digitando, clicar no painel (cabeçalho, linhas, botões, sombra) e nos cards (100 cliques) | Foreground não muda e o texto continua no app (SDD-24 CA-24.2). REPORT T5.3: "NÃO medido: 100 cliques reais via SendInput, cliques reais na sombra (T-24.13)" | | | | |
| MTC-24.3-a | Roda do mouse sobre o painel com a opção "rolar janelas inativas" **ligada** | O painel rola; barra arrastável (SDD-24 tabela MTC-24.3; RF-24.4) | | | | |
| MTC-24.3-b | Idem com a opção **desligada** | A barra arrastável continua funcional; o painel nunca recebe a roda (SDD-24 §11 risco; RF-24.4) | | | | |
| MTC-24.4 | Retorno de 3 h; deixar 60 s; reabrir pelo badge | O card aparece sozinho sem tirar o foco; após 60 s recolhe; o badge reabre (SDD-24 tabela; SDD-25 CA-25.7). Roteiro idêntico: `MTC-T4.10.md` T4.10b-1, -6, -7 | | | | |
| MTC-24.5 | Tela de 1366×768 a 125% e 150% | Painel e cards cabem e ficam clicáveis (SDD-24 tabela; DP-24.12: clamp ao topo da área de trabalho) | | | | |
| T5.3-z | Clicar em `Shell_TrayWnd` com o painel aberto | Z-order do painel contra a barra (REPORT T5.3: "NÃO medido: z-order contra `Shell_TrayWnd` (T-24.14)") | | | | |

## MTC-15: atalhos, menus e Explorer (SDD-15 tabela "MTC-15.1..4"; PLANO não nomeia a ficha dona, T5.8 é a origem)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-15.1 | Ver MTC-23.2-b acima | Substituído por SDD-23 MTC-23.2 (SDD-15) | | | | |
| MTC-15.2 | Com o Chrome em foco, usar os 4 atalhos; registrar um conflito de atalho de propósito | Os 4 atalhos funcionam. Conflito gera o aviso e o status "indisponível" (SDD-15 CA-15.4). REPORT T5.9: "status 'indisponível' dos atalhos (depende da plataforma)" não medido | | | | |
| MTC-15.3 | Abrir o menu da bandeja e o da alça; executar itens | Abrem, executam e devolvem o foco ao aplicativo anterior (SDD-15 CA-15.5). REPORT T5.8: foco devolvido de fato não verificado | | | | |
| MTC-15.4 | `taskkill /f /im explorer.exe` e `start explorer` | O ícone da bandeja é recriado (SDD-15 CA-15.6) | | | | |

## T5.9: Configurações e Sobre (itens "NÃO medido" do REPORT T5.9)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| T5.9-1 | Abrir Configurações e Sobre pelo menu da bandeja; conferir aparência e usabilidade; mudar o DPI | Abas Geral, Desempenho, Atalhos, Acessibilidade, Diagnóstico; Sobre com versão, pasta de dados, atribuição dos assets e licenças OFL de Silkscreen e JetBrains Mono (REPORT T5.9, "O que foi feito"; "NÃO medido: aparência/usabilidade visual, DPI") | | | | |
| T5.9-2 | Digitar atalhos no campo de texto; salvar com atalho inválido/duplicado | Formato `Ctrl/Alt/Shift/Win` + tecla; inválido ou duplicado bloqueia o salvar (REPORT T5.9) | | | | |
| T5.9-3 | Fechar Configurações/Sobre | Foco devolvido ao app anterior; uma instância por vez (REPORT T5.9, "O que o App precisa ligar"; T4.10) | | | | |
| T5.9-4 | Salvar settings (fps, reducedMotion, auto-escala, peek, UIA, saver) | Aplicados na T-Game sem reiniciar (REPORT T4.10) | | | | |
