# MTC-T4.10: raiz de composição do App e card "Bem-vindo de volta"

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Fonte dos passos: seção "Roteiro de verificação manual (MTC, para quando houver desktop isolado)" dos REPORTs `T4.10.md` (commit `db34268`, integrado como `cbd3fbd`) e `T4.10b.md` (commit `48e8ac8`). Esses roteiros não têm IDs MTC no SDD; os IDs abaixo (`T4.10-n`, `T4.10b-n`) são do preparador e só numeram os passos dos REPORTs. Os "esperados" são os escritos nos REPORTs, com a referência SDD quando o REPORT a cita.
> **Pré-condição:** build do App (`SideWar.exe`) com a composição. Itens que o REPORT T4.10 lista como "de fora" (painel, hotspots, TA.8, TA.5-Windows, TA.10-Windows, EcoQoS, WM_QUERYENDSESSION, recuperação de crash, log em arquivo) **não** estão cobertos aqui. O card "Bem-vindo de volta" (SDD-25) ficou na T4.10b, que já está na main (HEAD `3ccb2f8` contém `cbd3fbd`; conferir que o build testado inclui T4.10b).

## T4.10: composição

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| T4.10-1 | Apagar `%LOCALAPPDATA%\SideWar` e abrir o App; observar; clicar na faixa; contar wakes no Process Explorer | Faixa (Band) e widget aparecem sem roubar foco; bandeja aparece; o jogo anda; a primeira presença sem interação é **Ausente (8 fps)**; clicar na faixa leva a **Presente (30 fps)**; ~30 wakes/s em Presente e ~8 em Ausente (REPORT T4.10 #1; SDD-10 CA-10.3) | | | | |
| T4.10-2 | Ctrl+Alt+G, Ctrl+Alt+H, Ctrl+Alt+P; com o overlay oculto por 60 s, olhar o working set | G alterna Interativo/Passivo; H oculta/mostra; P pausa. Oculto: 0 frames e `session.json` gravado. Depois de 60 s oculto o working set cai (`ReleaseCaches`) (T4.10 #2; SDD-07 RF-07.3 passo 7) | | | | |
| T4.10-3 | Menu da bandeja e do widget: Pausar, Ocultar, Monitor ▸ (2º monitor), Configurações… (mudar fps/reducedMotion), Sobre…, Sair | Cada item age; Configurações aplica sem reiniciar; foco volta ao app anterior ao fechar a janela (T4.10 #3; T5.8/T5.9; SDD-15 CA-15.5) | | | | |
| T4.10-4 | Sair pelo menu; depois reabrir | O processo termina em ≤ 3 s, `session.json`/`profile.json` atualizados, nenhum processo sobra; ao reabrir a partida continua (mesma onda/créditos) (T4.10 #4) | | | | |
| T4.10-5 | Abrir uma segunda instância (inclusive com a primeira oculta pelo usuário) | A segunda sai em ≤ 200 ms; a primeira mostra o overlay em Interativo (T4.10 #5; SDD-07 CA-07.4) | | | | |
| T4.10-6 | Vídeo em tela cheia no mesmo monitor; depois sair | Tudo oculta; ao sair a faixa volta em ≤ 1 s com o conteúdo correto (T4.10 #6; SDD-06) | | | | |
| T4.10-7 | Win+L (bloquear/desbloquear) e sono | Oculta e volta sozinho; no sono o save é síncrono antes de suspender (T4.10 #7; SDD-07) | | | | |
| T4.10-8 | Ligar a economia de bateria; depois voltar ao Normal | ≤ 15 fps, animações desligadas, sem peek; voltar ao Normal restaura (T4.10 #8; SDD-07 RF-07.5) | | | | |
| T4.10-9 | Mudar alinhamento/auto-hide/DPI da barra; reiniciar o Explorer (`taskkill /f /im explorer.exe` e depois `start explorer`) | A faixa se reposiciona (debounce); após reiniciar o Explorer o hook é reinstalado (`ReinstallCount`) e o layout reconsultado; o ícone da bandeja é recriado (T4.10 #9; SDD-15 CA-15.6, MTC-15.4) | | | | |
| T4.10-10 | Provocar atualizações seguidas do tooltip da bandeja | Aplicadas no máximo 1×/s e a última aparece depois do deadline (T4.10 #10) | | | | |
| T4.10-11 | Logoff com o jogo rodando; logar de novo | Sessão restaurada (T4.10 #11; SDD-07 CA-07.5 / MTC-07.3) | | | | |

## T4.10b: card "Bem-vindo de volta" (SDD-25 CA-25.7, MTC-25.1)

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| T4.10b-1 | Jogar até ter torres, ocultar (Ctrl+Alt+H) e adiantar o relógio do sistema ou editar `savedUtc` do `session.json` em 3 h; mostrar | O card aparece acima da faixa, sem roubar foco (a janela ativa não muda) e sem pausar o jogo; saldo e onda já refletem o offline no primeiro frame (T4.10b #1). *O fato de o card aparecer sozinho sem tirar o foco também é MTC-24.4 (SDD-24)* | | | | |
| T4.10b-2 | Mover o app para outro monitor/DPI | O card fica centralizado sobre a faixa, 8 DIP acima; reposiciona e redesenha nítido (T4.10b #2; SDD-24 RF-24.2) | | | | |
| T4.10b-3 | Clicar COLETAR +X | Saldo sobe pelo valor do card, o card fecha, o badge não aparece (T4.10b #3) | | | | |
| T4.10b-4 | COLETAR ×2: acertar; repetir com erro; repetir esperando 45 s; repetir minimizando/fechando | Pergunta com cabeçalho `COLETAR ×2 · FASE nn` e timer de 45 s. Acerto: saldo ×2 e o resultado fica 2 s. Erro, 45 s, minimizar/fechar: todos valem ×1 (T4.10b #4; SDD-25 RF-25.9) | | | | |
| T4.10b-5 | Reiniciar o app com o relatório aberto (`pending` no save) e clicar ×2 de novo | Vale ×1, sem sortear outra pergunta (T4.10b #5) | | | | |
| T4.10b-6 | Deixar o card 60 s sem clicar; repetir com um clique de fundo antes dos 60 s; repetir só passando o mouse | Ao fim de 60 s o card fecha e o badge `+X` aparece no widget. Clique de fundo reinicia a contagem; passar o mouse não (T4.10b #6; SDD-25 CA-25.7) | | | | |
| T4.10b-7 | Clicar no badge `+X` | O card reabre sem foco e o badge some; coletar remove o pendente (T4.10b #7) | | | | |
| T4.10b-8 | Com o card aberto (e depois recolhido): Sair e reabrir | O card/badge voltam iguais, com a contagem de 60 s reiniciada (T4.10b #8; SDD-25 CA-25.7: "o pendente sobrevive a save e restore") | | | | |
| T4.10b-9 | Com o app em Ausente (8 fps), clicar no card | Vai a Presente (30 fps) (T4.10b #9) | | | | |
| T4.10b-10 | Com o card aberto, ocultar (conferir 0 wakes no Process Explorer) e mostrar | O card volta com 60 s novos (T4.10b #10) | | | | |
| T4.10b-11 | Ocultar e mostrar rápido (retorno < 10 min) | Sem card; o ganho vai direto ao saldo (T4.10b #11; SDD-25 CA-25.7: 9:59 não, 10:00 sim) | | | | |
| MTC-25.1 | Bloquear a tela, suspender, fechar e reabrir | O card correto aparece (SDD-25, tabela de MTC) | | | | |

Nota do REPORT T4.10b: `Apply` roda em todo `RestoreContext` com tempo > 0, o que reinicia a onda no retorno de ocultações curtas (fullscreen, Win+L). Se isso for observado na execução, é comportamento documentado do REPORT, mas deve ser anotado para o Tech Lead.
