# TD.7: faixa TD de 32 DIP acima da barra (fallback Band mínima, DP-28.11)

Data: 2026-11-16. Base: TA.1 **reprovou** torres dentro da barra (`specs/evidencias/TA.1.md`), então vale a decisão do usuário: hospedeiro `Band` MÍNIMO de 32 DIP logo acima da barra (`towersInBar = false`), trilho dos inimigos e torres dentro dessa faixa. Sem cena de 104 DIP.

## O que foi feito

- **Geometria (Runtime, puro):** `SceneLayout.ComputeTdBand` (janela de 32 DIP acima da área de trabalho, da esquerda até `TdBand.HudAreaDip` = 160 DIP antes da borda direita, reservados ao HUD da TD.8; sobe 2 px com a barra em ocultação automática), `BuildZoneMap.TdBand` / `BuildZoneOptions.TdBand` (uma zona livre entre núcleo e portal, sem UIA), `TdBand` (constantes e conversão unidade lógica → DIP), `TdHitTester` (caixa-bomba > torre posicionada > chip SEM POSIÇÃO), `TdRenderDriver` (render por evento) e `ITdFrameSink`.
- **LayoutChanged:** `SceneLayoutWatcher` ganhou `commandFactory` e o modo TD; o App cria um segundo watcher cujo sink é o `TdGameHost` (que agora implementa `IGameCommandSink`) e posta `Td.LayoutChanged` ao iniciar e a cada mudança de monitor, barra ou largura em DIP (mudança só de DPI muda a janela, não a faixa lógica).
- **Render (Skia, zero alocação em regime):** `TdBandRenderer`: trilho, núcleo (integridade), portal, inimigos por forma + cor de borda (solo cinza/círculo, voador ciano/losango elevado, blindado âmbar/quadrado, regenera verde/triângulo, drena magenta/cruz, chefe vermelho/grande), barra de vida (chefe e elite danificado), lentidão/paralisia, torres com pontos de nível e ponto de cor do ramo, alcance da selecionada como barra sobre o trilho, nós do balanceador, zonas, feixe de disparo, caixa-bomba com contagem e piscar, torre em risco marcada (contorno + seta), torres SEM POSIÇÃO como chips tracejados. Cores novas em `Palette.Td`.
- **App/Platform:** `gameMode = Td`: `DockController.TdMode` (janela = faixa TD, widget idle não é posicionado nem mostrado), `TdFrameSink` (DIB → `UpdateLayeredWindow` na superfície da cena), sink idle sem superfície (idle em NoRun), `SceneModeController.TdHitTest`. Clique na caixa-bomba → `TdGameHost.RequestCollectCrate`; clique em torre → seleciona (mostra o alcance; popover é da TD.8).
- **0 fps / 30 fps:** `TdRenderDriver` redesenha só quando muda a assinatura do estado (comandos, layout, contagem), há `MarkDirty` (DPI, seleção, Show) ou, em Onda com a simulação rodando, a cada tick novo (30 Hz). Pausa e oculto: nenhum frame. Teste `Driver_is_event_driven_zero_fps_in_preparo_and_paused_30_fps_in_wave`.
- **Z-order:** a janela de cena já é reafirmada pelo `ZOrderCoordinator` (foreground do shell, 100 ms) e a faixa não cobre a barra, então o problema de banda do TA.1 (Iniciar/Pesquisa) não a esconde.

## Decisões minhas (não cobertas) para CONFIRMAR

1. Largura da faixa = área de trabalho menos 160 DIP (`HudAreaDip`, valor provisório para o HUD da TD.8). Sem HUD ainda, os 160 DIP à direita ficam vazios.
2. Em modo TD o widget idle não é mostrado (o HUD compacto é a TD.8); o menu de bandeja continua com "Iniciar onda".
3. Quando uma caixa-bomba cai, a cena passa a **Interativo** (a cena é passante por padrão e o clique na caixa só chega no modo Interativo); o retorno automático ao Passivo vale como no resto. A TD.8 deve decidir o modo na UI.
4. Feixes de disparo são só visuais (o Core não expõe o alvo): o inimigo ligado pode divergir do atingido em empates.
5. Chips SEM POSIÇÃO ficam à esquerda da faixa (podem sobrepor torres próximas ao núcleo se houver muitas).
6. `HitKind` ganhou `TdTower`/`TdCrate` (id de torre/caixa do Core TD).

## Goldens (tests/SideWar.Rendering.Tests/Golden/td-strip-*-{100,150}.png)

4 cenas × 2 DPI (1200 × 32 DIP + barra sintética): `wave` (todos os traços + chefe, 6 torres com níveis/ramos/capstone), `prep-range` (Preparo, alcance da selecionada), `crate` (contagem piscando, torre em risco; só Linux por causa do texto Silkscreen, como as goldens existentes), `unpositioned`. O conteúdo de teste fixa créditos, custos, velocidades e ondas: os goldens **não dependem do balanceamento** do JSON (a TD.10 mexe nele). Revisão visual feita nos PNG.

## Verificação

- `scripts/wsl/build.sh Release`: 0 warnings, 0 erros.
- Linux: Runtime 718, Persistence 121 (+1 skip), Rendering 462 (inclui os 19+1 novos), todos passam. Core: 27 falhas, todas dos testes do WIP da TD.10 (conteúdo `td-*.json` e `TdMatchState` não commitados, fora do meu escopo); `scripts/wsl/test.sh` aborta nelas.
- Windows (espelho `C:\dev\sidewar-TD7`): `build.ps1` 0 avisos, 0 erros. `test.ps1` aborta no Core pelas mesmas 27 falhas; rodei o resto à mão: Runtime 718, Persistence 122, Rendering 408 (+54 ignorados, texto Linux), App.SmokeTests 36 (tudo passa).
- `Platform.Windows.Tests` (interativos, autorizados): 95 passam, **13 falham** (DockControllerTests ×4 esperam altura de cena 192 px a 192 DPI, valor anterior à TA.5; HotkeyTrayTests ×3 e outros dependem de hotkeys/janelas, com o App do usuário em execução; WidgetControllerTests, HotspotWindowTests, SceneModeTests.Peek). Não rodei a linha de base para provar que falham sem a minha mudança (`TdMode` é falso por padrão e não altera o caminho idle); tratar como pré-existentes até conferir.
- `git diff --check`: limpo.

## Captura real no Windows (Debug, `gameMode = Td`)

Settings de teste com `gameMode=Td` e auto-início de 5 s. Para ter torres sem a UI de construção (TD.8) apliquei um patch **só no espelho** (`TdComposition`: 2 `BuildTower` por variável de ambiente), fora do repositório. Capturas da faixa inferior de 150 px do monitor principal (1920 × 1080, 100% DPI): `specs/evidencias/TD.7/prep.png` (Preparo), `wave01.png` a `wave08.png` (Onda), `capture-log.txt`.

- A faixa fica acima da barra (núcleo à esquerda, portal na ponta direita da faixa, antes dos 160 DIP do HUD), 2 torres (Raio e Gelo) nas posições lógicas 300 e 560, inimigos marchando no trilho. O app seguiu vivo durante toda a captura.
- Restauração: a pasta `%LOCALAPPDATA%\SideWar` foi copiada antes e restaurada depois (não havia `settings.json` original; o de teste foi removido). O App do usuário estava rodando (PID 23624, `C:\dev\sidewar`, Debug) e foi reaberto no modo padrão (novo PID 23356). Os testes interativos e o App.SmokeTests rodaram antes, com o app do usuário ainda aberto, e podem ter gravado em `%LOCALAPPDATA%\SideWar\corrupt` (há `session*.json` quarentenados de 16:59 UTC); isso foi capturado como estado original no backup da captura.

## Não feito / pontos para a TD.8

Interação completa (clique para construir, popover, HUD), hover, modo construção e arte definitiva (TD.9). Não medi a taxa real de quadros no Windows (só o teste do driver e o 0 fps lógico).
