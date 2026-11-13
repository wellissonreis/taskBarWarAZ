# MTC-T1.15: checklists da E1 (MTC-03, 04, 05, 08.1, 21, 22 parcial)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Ficha: PLANO T1.15 (`MTC-03`, `MTC-04`, `MTC-05`, `MTC-21`, `MTC-22` itens 1 a 2 e 8 a 9, `MTC-08.1`); evidência em `specs/evidencias/E1.md`.
> PLANO §8.2 (linha das fichas pendentes) re-escopa T1.15: os checklists passam a incluir a faixa sobre a barra e o modo construção (MTC-21.13, MTC-22.11 a 22.13). Esses itens estão em `MTC-TA.1.md`, `MTC-TA.8.md` e `MTC-TA.13.md`.
> Aprovação E1 exige "MTC-03/04/05/21 preenchidos com capturas, saída de `--dump-taskbar` do Windows 11 do usuário" (PLANO E1, Aprovação). A saída de `--dump-taskbar` deve ser gerada na máquina real do usuário (CA-21.9, CA-17.4 / RF-17.9); o desktop isolado **não** a substitui.
> Itens marcados **(2 monitores)** ou **(barra/escala)** exigem VM/RDP com monitores e escala configuráveis; ver `README.md`.

## MTC-03: janela de overlay

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-03.1 | Abrir o overlay sobre Chrome (normal e maximizado) e VS Code; olhar a barra de tarefas, Alt+Tab e Win+Tab | O overlay aparece sobre o Chrome e o VS Code, **não** aparece na barra de tarefas nem no Alt+Tab (SDD-03 CA-03.1, tabela MTC-03.1: "Taskbar e Alt+Tab (Win+Tab também)") | | | | |
| MTC-03.2 | Com o VS Code em foco digitando, clicar em torres e no widget (a faixa em Interativo) | O foreground não muda e o texto continua indo ao VS Code (SDD-03 CA-03.2, tabela MTC-03.2; versão automatizada T-03.2 com 100 cliques) | | | | |

## MTC-04: transparência e cliques

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-04.1 | Faixa em **Passive**: clicar sobre a faixa numa aba do Chrome que esteja embaixo | A aba troca (SDD-04 CA-04.2) | | | | |
| MTC-04.2 | Faixa em Passive: rolar a roda do mouse sobre a faixa com o VS Code embaixo | O VS Code rola (SDD-04 CA-04.2) | | | | |
| MTC-04.3a | Faixa em **Interactive** com `backgroundAlpha=150`: clicar na faixa | O clique **não** chega ao app de baixo (SDD-04 CA-04.3) | | | | |
| MTC-04.3b | Interactive com `backgroundAlpha=0`: clicar em chão vazio; depois clicar numa torre | Chão vazio: o clique chega ao app de baixo. Torre: seleciona a torre (SDD-04 CA-04.3) | | | | |
| MTC-04.4 | Em Interactive, ficar sem atividade sobre o overlay | Volta a Passive em 8 s ±0,5 s (SDD-04 CA-04.4; a linha de SDD-04 diz "T-04.3 ... mais MTC", sem ID próprio) | | | | |

## MTC-05: DPI, monitores e docking

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-05.1 **(2 monitores)** | Arrastar o overlay entre monitores com DPIs diferentes usando "Mover para o monitor N" | Faixa nítida, sem sair da `WorkArea`, widget em proporção correta; anexar capturas à evidência (SDD-05 CA-05.2) | | | | |
| MTC-05.2 | Com o jogo rodando, mudar a escala do Windows em Configurações > Tela | Reposiciona e redesenha em ≤ 1 s (SDD-05 CA-05.3) | | | | |
| MTC-05.3 **(2 monitores)** | Desconectar o monitor onde está o overlay; depois reconectar | Desconectar move a faixa para o primário. Reconectar **não** volta sozinho, a menos que `DeviceName` e bounds coincidam no próximo início (SDD-05 CA-05.4) | | | | |
| MTC-05.4 | Mover a barra de tarefas ou ligar a ocultação automática | Reposiciona a faixa em ≤ 1 s (SDD-05 CA-05.5) | | | | |

## MTC-08.1: renderização

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-08.1 | Quadro de teste sobre fundo **branco** e depois **preto**; repetir em 100/150/200% (tabela: "Bordas sobre fundos claro e escuro e nitidez em 100/150/200%") | 30 fps no overlay, transparência correta, **sem halo preto** nas bordas (alfa pré-multiplicado correto), verificado por captura nos dois fundos (SDD-08 CA-08.1) | | | | |

## MTC-21: leitura da barra (matriz SDD-21 §9, jogo visível, Windows 11)

Cada linha usa `MTC-21.<#>` (SDD-21). A linha 13 está em `MTC-TA.1.md` e `MTC-TA.13.md`.

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-21.1 | Barra **centralizada** (padrão), 6 apps fixados | 2 zonas livres (esquerda e direita do grupo central); realces do modo construção batem com a tela; `Alignment=Center` (SDD-21 #1) | | | | |
| MTC-21.2 | Barra **alinhada à esquerda** | `Alignment=Left`; só a zona livre da direita (entre o último ícone e a bandeja) (SDD-21 #2) | | | | |
| MTC-21.3 | Barra com **Widgets** ligado | O widget do Windows fica bloqueado (`WindowsWidget`) (SDD-21 #3) | | | | |
| MTC-21.4 | **Ocultação automática** da barra ligada | Fallback `Band` (DP-33.5); cena com margem de 2 px (SDD-05 RF-05.7); nenhuma torre desenhada sobre a barra (SDD-21 #4) | | | | |
| MTC-21.5 | **Ícones pequenos** / barra baixa | `HeightDip` reflete a nova altura; torres centralizadas na barra; zonas corretas (SDD-21 #5) | | | | |
| MTC-21.6 | **Muitos apps** (> 12 botões) | Zona central cresce; torres atingidas ficam `SEM POSIÇÃO` e voltam ao fechar os apps (DP-33.1) (SDD-21 #6) | | | | |
| MTC-21.7 **(2 monitores)** | Barra no monitor 2 e jogo no 2; jogo no 1 sem barra | Layout da barra do monitor certo / fallback quando o monitor do jogo não tem barra (SDD-21 #7; risco "UIA em DPI misto" cita MTC-21.7) | | | | |
| MTC-21.8 | **Reiniciar o Explorer** (Gerenciador de Tarefas) | Volta a ler a barra em ≤ 5 s (`TaskbarCreated`), sem reiniciar o jogo e sem exceção não tratada (SDD-21 #8; CA-21.7) | | | | |
| MTC-21.9 | **Fixar** e **desafixar** um app | Zonas atualizam em ≤ 1 s; nenhuma torre fica sobre ícone; `SEM POSIÇÃO` e retorno (RF-21.8) (SDD-21 #9; CA-21.4) | | | | |
| MTC-21.10 | Mudar a **escala** (125% → 150%) | Slots e faixa reposicionam em ≤ 1 s (SDD-21 #10) | | | | |
| MTC-21.11 | Mover a barra para **cima** (se disponível na build) | Fallback; faixa na borda inferior da área de trabalho (SDD-21 #11) | | | | |
| MTC-21.12 | Abrir/fechar apps (botões surgem e somem) | Nenhuma torre sobre ícone; sem atualizar mais de 2×/min em regime (SDD-21 #12) | | | | |
| MTC-21 / dump | Rodar `SideWar.exe --dump-taskbar` na máquina real do usuário (Windows 11) | JSON válido, sem nenhum campo `Name`; o classificador reproduz o `layout` a partir dos `nodes` (SDD-21 CA-21.9). Exigido como evidência da E1 (PLANO E1) | | | | |

## MTC-22 (itens 1, 2, 8, 9; matriz SDD-22 §9, Windows 11)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-22.1 | Iniciar o jogo com a barra embaixo (100%, 125%, 150%) | Cena de 104 DIP acima da barra e torres dentro dela, largura do monitor (ADR-034) (SDD-22 #1). *Parte Windows de `SceneWindow` sobre a barra é a ficha TA.5-Windows; ver `MTC-TA.5-Windows.md`* | | | | |
| MTC-22.2 | Clicar e rolar sobre a faixa em Passivo (Chrome e VS Code embaixo) | O clique e a rolagem chegam ao app de baixo (SDD-22 #2) | | | | |
| MTC-22.8 | Barra com ocultação automática | Faixa na borda inferior com margem de 2 px; a barra continua ativável (SDD-22 #8). Risco: a barra surgir por cima da faixa (ambas topmost), validar aqui (SDD-22 §11) | | | | |
| MTC-22.9 | Mudar a escala e o monitor | Nitidez mantida; faixa reposiciona em ≤ 1 s (SDD-22 #9) | | | | |
