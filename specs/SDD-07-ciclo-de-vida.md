# SDD-07: Ciclo de vida: ocultar e retornar, inatividade, energia, sessão, instância única e encerramento seguro

**Status:** Rascunho para revisão · **Etapa:** E2 · **Depende de:** SDD-03, 05, 06, 10 (motivos de pausa), SDD-25 (presença e offline), SDD-26 (eventos ao vivo), SDD-27 (economia idle) · **Bloqueia:** SDD-14 (gatilhos de save), SDD-18, SDD-25

## 1. Identificador e título

SDD-07: Máquina de estados da aplicação, motivos de ocultação e pausa, perfis de energia e sequências de início e término.

## 2. Objetivo

- Suspender toda atividade quando o jogo não está sendo visto: ocultar = **Offline** (SDD-25), sem simulação e sem pausa; o ganho é calculado analiticamente no retorno (ADR-022, teto de 12 h).
- Em inatividade, passar a **Ausente** (modo gerenciado, a simulação continua; SDD-25) e reduzir a atividade em economia de energia.
- Preservar o estado exato ao ocultar e recuperar o contexto correto ao retornar.
- Encerrar sempre com segurança: save consistente e liberação de todos os recursos nativos.

## 3. Contexto técnico

- Broadcasts como `WM_SETTINGCHANGE`, `WM_DISPLAYCHANGE`, `WM_QUERYENDSESSION` e `WM_ENDSESSION` só chegam a janelas **top-level**. Janelas message-only (`HWND_MESSAGE`) **não** os recebem. A OverlayWindow (top-level, mesmo oculta) recebe esses broadcasts.
- `RegisterPowerSettingNotification` e `WTSRegisterSessionNotification` aceitam um HWND: usar a OverlayWindow.
- EcoQoS: `SetProcessInformation(ProcessPowerThrottling, EXECUTION_SPEED)` coloca o processo em "modo de eficiência" de agendamento no Windows 11.
- WPF: `Application.SessionEnding` existe na T-UI e funciona como redundância.

## 4. Escopo e exclusões

**Escopo:** `AppStateMachine`, `HiddenReasons`, `PauseReasons` (contrato com SDD-10), `PowerProfile`, `PresenceTracker` (SDD-25; antes `IdlePolicy`), sequências de Show e Hide, retomada após sono, bloqueio, monitor desligado, instância única, início com o Windows (opção), encerramento normal, fim de sessão e crash.

**Exclusões:** formato dos saves (SDD-14) e detecção de fullscreen (SDD-06).

## 5. Requisitos funcionais

### RF-07.1: Motivos de ocultação (`[Flags] enum HiddenReason`)

| Flag | Origem | Entra | Sai |
|---|---|---|---|
| `User` | Hotkey `ToggleVisibility`, menu "Ocultar" ou bandeja | ação do usuário | ação do usuário |
| `Fullscreen` | SDD-06 | `FullscreenEntered` | `FullscreenExited` |
| `SessionLocked` | `WM_WTSSESSION_CHANGE`: `WTS_SESSION_LOCK` (0x7) | lock | `WTS_SESSION_UNLOCK` (0x8) |
| `RemoteDisconnected` | `WTS_CONSOLE_DISCONNECT` / `WTS_REMOTE_DISCONNECT` | desconexão | `*_CONNECT` |
| `DisplayOff` | `GUID_CONSOLE_DISPLAY_STATE` (0 = off, 1 = on, 2 = dimmed) | 0 | 1 ou 2 |
| `Suspended` | `WM_POWERBROADCAST`: `PBT_APMSUSPEND` | suspensão | `PBT_APMRESUMEAUTOMATIC` |
| `Starting` | início | criação | primeiro frame pronto |

- **Visível ⇔ `HiddenReason == None`.**
- **Ocultar = Offline** (SDD-25 RF-25.1): qualquer `HiddenReason` coloca o jogo em estado Offline; nada é simulado enquanto oculto e o `OfflineCalculator` roda no retorno (RF-07.6).
- Toda mudança passa pela `AppStateMachine` (Runtime, pura), que emite efeitos: `Show`, `Hide`, `Pause(reason)`, `Resume(reason)`, `Save(trigger)`, `ReleaseCaches`, `RestoreContext` e `SetEcoQos(on|off)`.

### RF-07.2: Motivos de pausa (contrato com SDD-10)

> **Substituído por SDD-25 RF-25.2 (ADR-019, ADR-022).** `PauseReason` fica reduzido a `{ User, Hidden, Dialog }`: `Idle` é removido (a ausência não pausa; é o modo gerenciado) e `Question` não é mais usado (a pergunta não pausa, SDD-26). `Hidden` pausa **sempre** (a opção `pauseWhenHidden` deixa de existir; o ganho vem do cálculo offline). O texto abaixo é histórico onde conflitar.

- `[Flags] enum PauseReason { User, Hidden, Dialog }` (SDD-25 RF-25.2; antes `Idle` e `Question` também existiam). A simulação avança apenas quando `PauseReasons == None`.
- `Hidden` é aplicado sempre que `HiddenReason != None` (sem opção; antes dependia de `pauseWhenHidden`).
- ~~`Idle`~~: removido (RF-07.4).
- `Dialog`: janela WPF modal do jogo aberta (Configurações com a opção "pausar ao abrir"; o painel e os cards do jogo **não** pausam, ADR-021).

### RF-07.3: Sequência de Hide (efeito `Hide`)

1. Cancelar o arraste e entrar no modo Passive (SDD-04).
2. Aplicar `PauseReason.Hidden` (sempre) e passar a presença para **Offline** (SDD-25).
3. `ShowWindow(SW_HIDE)` na alça e no overlay.
4. Disparar `Save(trigger: Hidden)` **sempre**, gravando o `OfflineSnapshot` com `savedUtc` (SDD-25 RF-25.6 e RF-25.11; SDD-14).
5. Desarmar todos os deadlines de render e simulação. Manter apenas o polling de fullscreen se o motivo for `Fullscreen` (SDD-06).
6. Se o motivo incluir `User`, `SessionLocked`, `DisplayOff` ou `Suspended`, ligar o EcoQoS.
7. Agendar `ReleaseCaches` para daqui a **60 s** se ainda estiver oculto. O efeito libera o DIB e a superfície (SDD-08) e o cache de sprites escalados (SDD-09), e solicita `GC.Collect(2, GCCollectionMode.Optimized, blocking:false, compacting:true)`, uma vez.

### RF-07.4: Inatividade do usuário (`IdlePolicy`, puro)

> **Substituído por SDD-25 RF-25.1 (ADR-019).** A `IdlePolicy` com `GetLastInputInfo`, `idleMinutes` e `pauseWhenIdle` é substituída pelo `PresenceTracker`: **Ausente** = overlay visível e sem interação **com o jogo** há mais de 5 min (atividade em outros aplicativos não conta). Ausente é **modo gerenciado, sem pausa**: a simulação segue a 30 Hz, ganhos ×0,25, render a 8 fps (SDD-22 RF-22.10) e EcoQoS ligado (SDD-25 RF-25.4, DP-25.6). Não há selo "PAUSADO: AUSENTE" nem retomada manual; o selo de ausente é o de SDD-22 RF-22.7. O texto abaixo é histórico e **não** é implementado.

- Fonte: `GetLastInputInfo` (tick do último input do sistema), lido no máximo a **cada 5 s** e apenas enquanto o overlay está visível.
- `settings.behavior.idleMinutes` (padrão 5, faixa 1–60, 0 = desligado). Quando `agora − lastInput ≥ idleMinutes`:
  - se o jogo está em onda e `settings.behavior.pauseWhenIdle` (padrão true), aplicar `PauseReason.Idle` e mostrar na HUD o selo "PAUSADO: AUSENTE";
  - render em modo de evento (0 fps ambiente) e EcoQoS ligado.
- Ao detectar input de novo (o tick muda), remover o modo de render ocioso. **Não** retomar a onda automaticamente: `PauseReason.Idle` vira `PauseReason.User`, e o usuário retoma com o botão ou a hotkey. Assim o jogo não continua enquanto o usuário ainda não olhou.

### RF-07.5: Perfis de energia (`PowerProfile { Normal, Saver }`)

- **Saver** vale se qualquer condição for verdadeira:
  - `GUID_POWER_SAVING_STATUS == 1` (economia de bateria);
  - `GUID_ENERGY_SAVER_STATUS ≥ 1` (Windows 11 22H2+, quando o GUID existir; se não existir, ignorar);
  - na bateria (`GUID_ACDC_POWER_SOURCE == 1`) **e** `settings.performance.saverOnBattery` (padrão true);
  - `settings.performance.forceSaver`.
- Efeitos do Saver (aplicados pela `RenderPolicy`, SDD-10):
  - fps ativo máximo de 15;
  - animações ambientes desligadas;
  - peek (SDD-04) desligado;
  - EcoQoS ligado.

### RF-07.6: Sequência de Show e recuperação de contexto (efeito `RestoreContext` + `Show`)

> **Substituído por SDD-25 RF-25.2 e RF-25.10 (ADR-019, ADR-022) nos passos 3 e 6.** Retomada **automática** (sem "PAUSADO", sem converter `Hidden` em `User`) e `OfflineCalculator` antes do frame de reexibição; ao retornar com Offline ≥ 10 min, aparece o card "Bem-vindo de volta" (SDD-25 RF-25.9, SDD-24; D-11) sem ativar a janela. Os passos 1, 2, 4, 5 e 7 continuam.

1. Reenumerar os monitores, resolver o monitor e recomputar o layout (SDD-05). Se nada mudou, reaproveitar.
2. Se a superfície foi liberada ou a escala mudou, recriar o DIB e a superfície e refazer o bake do cache de sprites e da camada estática.
3. Zerar o acumulador da simulação e o `lastFrameTime` (sem catch-up, SDD-10) **e** executar `OfflineCalculator.Compute` com o `OfflineSnapshot` e `nowUtc`, aplicando o resultado e criando o card quando couber (SDD-25 RF-25.10). Nada foi simulado enquanto oculto.
4. Renderizar **um frame completo antes** do `ShowWindow` (sem conteúdo velho na tela).
5. `ShowWindow(SW_SHOWNOACTIVATE)`, reafirmar o topmost (SDD-03) e entrar no modo Passive.
6. Remover `PauseReason.Hidden` e **retomar automaticamente** (jogo idle). Não converter em `User` e não mostrar "PAUSADO". `settings.behavior.autoResume` deixa de existir. A presença volta a Presente se o `Show` foi por ação do usuário, ou segue o `PresenceTracker` (SDD-25 RF-25.1). `PauseReason.User` e `Dialog` continuam sendo respeitados.
7. Desligar o EcoQoS se o perfil não for Saver.

### RF-07.7: Sono e retomada

- `PBT_APMSUSPEND`: `HiddenReason.Suspended`, save **síncrono** com timeout de 1 s (`AtomicFileWriter` direto na T-Game, sem esperar a T-IO).
- `PBT_APMRESUMEAUTOMATIC`: remover `Suspended`; agendar a reenumeração completa (topologia, DPI e barra de tarefas) para daqui a 1 s, porque os monitores podem acordar com atraso.

### RF-07.8: Instância única

- `Mutex` nomeado `Local\SideWar.Desktop.SingleInstance` com `initiallyOwned: true`.
- Se a aquisição falhar, abrir `EventWaitHandle` `Local\SideWar.Desktop.Activate`, chamar `Set()` e sair com código 0 em ≤ 200 ms.
- A primeira instância espera o evento numa `RegisteredWaitHandle` (ThreadPool) e posta `ShowAndInteract` para a T-Game: remove `HiddenReason.User` e entra no modo Interactive.

### RF-07.9: Início com o Windows (opção, padrão desligado)

- Sem MSIX: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valor `SideWar` = `"<caminho exe>" --startup`.
- Com `--startup`, o app inicia com `HiddenReason.None` e retoma a sessão salva (idle); o tempo desde o último save é tratado como Offline (SDD-25).
- Com MSIX: `StartupTask` no manifesto (SDD-18).

### RF-07.10: Encerramento normal (menu "Sair", hotkey opcional)

Ordem obrigatória, implementada em `ShutdownCoordinator` (App):

1. Marcar `ShuttingDown` e rejeitar novos comandos (`CommandStatus.ShuttingDown`).
2. T-Game: pausar (`PauseReason.User`), serializar a sessão (incluindo o `OfflineSnapshot` com `savedUtc`, SDD-25 RF-25.11), o perfil e as configurações pendentes e gravar **sincronamente** com timeout total de 2 s.
3. T-Game:
   - `UnhookWinEvent` (todos);
   - `UnregisterHotKey` (todos);
   - `SHAppBarMessage(ABM_REMOVE)`;
   - `UnregisterPowerSettingNotification`;
   - `WTSUnRegisterSessionNotification`;
   - destruir as janelas;
   - liberar o DIB, o DC e a superfície Skia;
   - descartar o atlas;
   - fechar o timer;
   - sair do laço (RF-03.7).
4. T-UI: fechar as janelas WPF, remover o ícone da bandeja (`NIM_DELETE`), liberar o mutex e chamar `Application.Shutdown(0)`.
5. Log final `"shutdown ok in <ms>"` com flush.

O tempo total deve ser ≤ 3 s. Se for excedido, `Environment.Exit(0)` depois de registrar.

### RF-07.11: Fim de sessão do Windows (logoff, desligamento, atualização)

- `WM_QUERYENDSESSION` (OverlayWindow): retornar TRUE imediatamente. Executar o passo 2 de RF-07.10 de forma síncrona, com timeout de 1,5 s, e chamar `ShutdownBlockReasonCreate(overlay, "Salvando o jogo…")` durante a gravação e `ShutdownBlockReasonDestroy` depois dela.
- `WM_ENDSESSION` com `wParam == TRUE`: executar os passos 3 a 5 sem novas gravações.
- `Application.SessionEnding` (T-UI): se a T-Game ainda não salvou, postar o pedido de save e aguardar até 1,5 s.

### RF-07.12: Falha inesperada (crash)

- Handlers: `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` (apenas log), `Application.DispatcherUnhandledException` (log e diálogo opcional) e o try/catch da GameThread (RF-03.8).
- Em exceção fatal:
  1. escrever `logs/crash-<yyyyMMdd-HHmmss>.txt` com a stack;
  2. tentar um **save de emergência** em `session.crash.json`, **sem** sobrescrever `session.json` (SDD-14 decide no próximo início);
  3. remover o ícone da bandeja;
  4. sair com código 1.
- No próximo início, se `session.crash.json` existir e for mais novo que `session.json`, a HUD oferece "Recuperar partida interrompida?" com as opções Sim e Descartar.

## 6. Requisitos não funcionais

- **RNF-07.1:** Oculto por qualquer motivo: 0 frames renderizados, wakes ≤ 1/s (somente o polling de fullscreen quando aplicável) e CPU média ≤ 0,1% em 5 min (meta; SDD-16).
- **RNF-07.2:** Do retorno do fullscreen até o overlay visível: ≤ 1 s, com o conteúdo correto no primeiro frame.
- **RNF-07.3:** O encerramento normal leva ≤ 3 s, e `GetGuiResources` antes de sair indica que todos os objetos GDI do processo, exceto os do WPF, foram liberados (verificação no smoke test).
- **RNF-07.4:** Durante Hide, o estado **não** muda (nada é simulado); o hash de antes do Hide é igual ao de imediatamente antes de `OfflineCalculator.Compute` no Show. Após o cálculo, o estado muda de forma determinística (SDD-25 RNF-25.1).

## 7. Estratégia de implementação

1. `AppStateMachine`, `PresenceTracker` (SDD-25; substitui `IdlePolicy`), `PowerProfileResolver` e `ResumePolicy` em Runtime, com testes no Linux.
2. `SessionPowerWatcher` (Platform): registros e tradução das mensagens em sinais.
3. `ShutdownCoordinator`, `SingleInstance` e `CrashHandler` (App).
4. `EcoQosController` (Platform).
5. Smoke test Windows: iniciar, ocultar, mostrar e sair, conferindo handles e logs.

## 8. Dependências

SDD-03, SDD-05, SDD-06, SDD-10 (`PauseReasons`) e SDD-14 (gravação). Os gatilhos de save são definidos aqui; o formato fica em SDD-14.

## 9. Critérios de aceitação

- **CA-07.1:** A tabela de transições da `AppStateMachine` (todas as combinações de 2 motivos simultâneos) passa nos testes (Linux).
- **CA-07.2:** Bloquear a tela (Win+L) por 30 s e desbloquear: o overlay volta, o jogo retoma sozinho (com 30 s Offline não há card, só ganho direto; card se ≥ 10 min, SDD-25), e o hash do estado antes do cálculo offline é igual ao anterior (MTC-07.1 + log). A parte que depende de gravação em disco é verificada na E6; na E2 verifica-se o hash em memória e a emissão do efeito `Save`.
- **CA-07.3:** Suspender (sono) e acordar: o overlay volta em ≤ 2 s após o desbloqueio, na posição correta, com o save gravado antes da suspensão (timestamp do arquivo < horário do sono) (MTC-07.2). A parte que depende de gravação em disco é verificada na E6; na E2 verifica-se o hash em memória e a emissão do efeito `Save`.
- **CA-07.4:** Uma segunda instância sai em ≤ 200 ms, e a primeira mostra o overlay em modo Interactive (T-07.4).
- **CA-07.5:** Logoff com a onda em andamento: no próximo login, a sessão é restaurada no ponto do logoff (diferença de no máximo 1 tick) (MTC-07.3). A parte que depende de gravação em disco é verificada na E6; na E2 verifica-se o hash em memória e a emissão do efeito `Save`.
- **CA-07.6:** Um crash forçado (`--debug-crash` em build Debug) gera `crash-*.txt` e `session.crash.json`, e o próximo início oferece a recuperação (T-07.6).
- **CA-07.7:** Com o overlay oculto por 5 min, a CPU média do processo fica ≤ 0,1%, medida por `measure-perf.ps1 -Scenario hidden` (SDD-16). O resultado é registrado como **medido** ou **reprovado**.

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-07.1 | `AppStateMachine`: combinações de motivos, ordem de efeitos | Linux |
| T-07.2 | `PresenceTracker` com FakeClock (SDD-25 T-25.x; substitui `IdlePolicy`) | Linux |
| T-07.3 | `ResumePolicy`: retomada automática sempre; `OfflineCalculator` antes do frame (SDD-25) | Linux |
| T-07.4 | Segunda instância | WindowsInteractive |
| T-07.5 | Hide/Show 100× sem vazamento (handles e memória estáveis) | WindowsInteractive |
| T-07.6 | Crash simulado e recuperação | WindowsInteractive |
| MTC-07.1..4 | Lock, sono, logoff e economia de bateria | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| `GUID_ENERGY_SAVER_STATUS` indisponível no CsWin32 ou no build | Média | Baixo | Declarar o GUID manualmente; se a API falhar, ignorar |
| Timeout do Windows no desligamento matar o processo durante a gravação | Baixa | Alto | Escrita atômica (o arquivo antigo fica íntegro) e `ShutdownBlockReasonCreate` |
| EcoQoS piorar a latência ao reexibir | Baixa | Baixo | Desligar o EcoQoS **antes** do frame de reexibição |
| Retomada automática com perda de progresso sem o jogador ver | Baixa | Baixo | O offline é analítico e sem quedas (SDD-25 DP-25.5); o card de retorno mostra o resumo |
