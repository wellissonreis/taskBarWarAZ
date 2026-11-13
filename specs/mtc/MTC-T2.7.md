# MTC-T2.7: visibilidade e ciclo de vida (MTC-06, MTC-07)

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Ficha: PLANO T2.7; evidência em `specs/evidencias/E2.md`, que exige "a matriz MTC-06 100% preenchida" (PLANO E2, Aprovação). MTC-07.1..3 ficam "parcial" na E2 (verifica-se o hash em memória e o efeito `Save`) e fecham na E6 (`MTC-T6.12.md`).

## MTC-06: matriz de fullscreen (SDD-06; overlay no mesmo monitor; Windows 11)

Meta global: 100% dos casos como esperado (CA-06.2); zero falsos positivos nos casos "maximizado" (RNF-06.3/06.4); F11 → oculto ≤ 500 ms e saída do F11 → visível ≤ 1 s (RNF-06.3).

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-06.1 | Chrome maximizado | visível (SDD-06 matriz #1) | | | | |
| MTC-06.2 | Chrome F11 | oculto (#2) | | | | |
| MTC-06.3 | Vídeo do YouTube em tela cheia (botão do player) | oculto (#3) | | | | |
| MTC-06.4 | Sair do F11 (Esc) | visível em ≤ 1 s (#4) | | | | |
| MTC-06.5 | VS Code maximizado | visível (#5) | | | | |
| MTC-06.6 | VS Code F11 e Zen Mode | oculto (#6) | | | | |
| MTC-06.7 | Barra em ocultação automática + Chrome maximizado | visível (#7) | | | | |
| MTC-06.8 | Abrir o menu Iniciar com o Chrome em F11 | continua oculto (Ignore) (#8) | | | | |
| MTC-06.9 | Win+Tab / Alt+Tab | sem mudança (Ignore) (#9) | | | | |
| MTC-06.10 | Clicar na área de trabalho | visível (#10) | | | | |
| MTC-06.11 | Jogo borderless (qualquer) ou janela de teste `TestFullscreenWindow` (popup do tamanho do monitor) | oculto (#11) | | | | |
| MTC-06.12 | PowerPoint F5 (se disponível) | oculto (#12) | | | | |
| MTC-06.13 **(2 monitores)** | Vídeo em tela cheia no monitor 2 e foco no monitor 1 | Conforme CA-06.4: com o overlay no monitor 1 (`SameMonitor`) ele continua visível; com o overlay no monitor 2 ele fica oculto **mesmo com o foco no monitor 1** (#13; CA-06.4) | | | | |

Para os itens 2 e 4, registrar o tempo medido contra RNF-06.3 (≤ 500 ms para ocultar; ≤ 1 s para reexibir). Item 6 do roteiro de T4.10 (vídeo em tela cheia no mesmo monitor) repete 06.3 na composição real; ver `MTC-T4.10.md`.

## MTC-07: ciclo de vida (SDD-07, tabela "MTC-07.1..4: Lock, sono, logoff e economia de bateria")

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-07.1 | Bloquear a tela (Win+L) por 30 s e desbloquear | O overlay volta, o jogo retoma sozinho; com 30 s Offline **não há card**, só ganho direto (card se ≥ 10 min, SDD-25); o hash do estado antes do cálculo offline é igual ao anterior (conferir no log). Na E2 verifica-se o hash em memória e a emissão do efeito `Save` (SDD-07 CA-07.2) | | | | |
| MTC-07.2 | Suspender (sono) e acordar | O overlay volta em ≤ 2 s após o desbloqueio, na posição correta; o save foi gravado antes da suspensão (timestamp do arquivo < horário do sono). Na E2 só o hash em memória e o efeito `Save` (CA-07.3). Roteiro T4.10 #7: "no sono, o save é síncrono antes de suspender" | | | | |
| MTC-07.3 | Logoff com a onda em andamento; logar de novo | Sessão restaurada no ponto do logoff (diferença de no máximo 1 tick) (CA-07.5). Na E2 só o hash em memória e o efeito `Save` | | | | |
| MTC-07.4 | Ligar a economia de bateria do Windows (ou forçar Saver pelas settings: `performance.forceSaver`) | Saver: fps ativo ≤ 15, animações ambientes desligadas, peek desligado, EcoQoS ligado (SDD-07 RF-07.5; T4.10 #8: "voltar ao Normal restaura"). *Nota: o REPORT T4.10 registra que não existe `EcoQosController` (o efeito só vai para o log); o item EcoQoS pode não ser verificável nesta base* | | | | |
| MTC-07 / CA-07.4 | Abrir uma segunda instância | Ela sai em ≤ 200 ms e a primeira mostra o overlay em modo Interactive (SDD-07 CA-07.4; T-07.4 é automatizado; roteiro T4.10 #5 cobre o manual) | | | | |
| MTC-07 / CA-07.7 | `measure-perf.ps1 -Scenario hidden` com 300 s (ver `README.md`) | CPU média do processo ≤ 0,1% com o overlay oculto por 5 min; registrar **medido** ou **reprovado** (SDD-07 CA-07.7; SDD-16 P-01: ≤ 0,1% · ≤ 1 wake/s · 0 frames). Medição de meta vale na máquina de referência (D-05); ver `MTC-MED.md` | | | | |
