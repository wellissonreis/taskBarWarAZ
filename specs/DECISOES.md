# Registro de decisões (ADR) e decisões pendentes

Status possíveis: **Aceita (proposta do Tech Lead)**, que vale até revisão do usuário; **Pendente**, que bloqueia apenas o que estiver indicado; **Substituída** (por outra ADR, com o histórico preservado); **Refinada** (continua valendo, com o acréscimo indicado).

Para alterar uma decisão aceita, é preciso registrar uma nova ADR com justificativa e, quando houver impacto em desempenho, com a medição que a motiva. O executor (Sonnet ou Haiku) **não** cria nem altera ADRs: quando encontrar uma lacuna, para e reporta.

---

## Decisões aceitas

### ADR-001: Superfície do overlay como janela Win32 *layered* com Skia raster e `UpdateLayeredWindow`

- **Contexto:** o overlay precisa de alfa por pixel, passagem de cliques e baixo custo. A stack obrigatória é WPF, SkiaSharp e Win32.
- **Opções:**
  - (A) janela WPF com `AllowsTransparency=True` e `SKElement`;
  - (B) janela Win32 `WS_EX_LAYERED`, Skia desenhando direto na memória de um DIB e `UpdateLayeredWindow(ULW_ALPHA)`;
  - (C) DirectComposition com swapchain de alfa pré-multiplicado e Skia GPU.
- **Decisão:** (B).
- **Justificativa:**
  - Em (A), a renderização passa por Skia, `WriteableBitmap`, composição WPF, readback e `UpdateLayeredWindow` interno: copia duas vezes e acorda a thread de render do WPF.
  - Em (B), a cópia é única e não há thread de render extra. O hit-test por alfa e a opacidade global (`SourceConstantAlpha`) não têm custo de render.
  - Em (C), o hit-test por alfa não existe, o que complica a passagem de cliques por região, e o backend Direct3D do SkiaSharp tem maturidade menor.
- **Contingência:** se o MVP técnico (etapa E1) medir CPU acima de 2× a meta de SDD-16 no cenário de onda pesada, abrir a ADR "Superfície DirectComposition" com medições. Não migrar sem aprovação.
- **WPF continua obrigatório** para o ciclo de vida da aplicação, o dispatcher e todas as janelas "cheias" (configurações, campanha, resultados, perguntas).

### ADR-002: Thread dedicada do jogo dona do HWND do overlay

- A **GameThread** cria a janela do overlay e o *handle*, roda o próprio laço `MsgWaitForMultipleObjectsEx` com um *waitable timer* de alta resolução e executa simulação e renderização.
- A thread de UI do WPF cuida das janelas WPF, do ícone de bandeja e dos menus.
- A comunicação entre as duas usa filas de comandos e o `Dispatcher.BeginInvoke`. Não existe relação owner/owned entre janelas de threads diferentes.
- **Justificativa:** pausas de layout ou GC do WPF não afetam os frames. A espera fica bloqueada sem consumo de CPU, e a posse das janelas é clara.

### ADR-003: Formato do overlay como faixa acoplada a uma borda (padrão: inferior, acima da barra de tarefas)

> **Status: Substituída por ADR-020 (geometria) e ADR-018 (design oficial).** Histórico preservado abaixo: o mundo de 1920×200 com HUD lateral, a escolha de bordas inferior/superior e o D-02 deixam de valer. O que continua: a faixa não reserva espaço por padrão (AppBar opcional, pós-MVP, agora reservando os 96 DIP da faixa, SDD-05 RF-05.8).

- O mundo do jogo é uma faixa de 1920×200 unidades (proporção 9,6:1) com HUD lateral à esquerda.
- No MVP, acopla nas bordas **inferior** e **superior**. Bordas laterais com faixa vertical ficam para depois do MVP (D-02).
- Por padrão, a faixa **não** reserva espaço (é overlay). O modo "reservar espaço" (AppBar) é opcional e vem depois do MVP (SDD-05).

### ADR-004: Simulação em passo fixo de 30 Hz, sem interpolação

- O passo `dt = 1/30 s` é constante. A velocidade 2× executa 2 passos por tick. O acumulador é limitado a 0,25 s e zerado ao retomar.
- O render nunca passa de 30 fps no padrão (configurável em 20/30/60). Com 60 fps o mesmo estado é desenhado duas vezes; não há interpolação no MVP.
- **Justificativa:** reduz a CPU da simulação pela metade em relação ao passo de 1/60 da referência e mantém o determinismo.

### ADR-005: Interop Win32 com `Microsoft.Windows.CsWin32` (gerador de código)

- As assinaturas são geradas a partir de `NativeMethods.txt`. P/Invoke manual só é permitido quando a API não existe nos metadados, e fica documentado no arquivo.

### ADR-006: Testes com xUnit v3 e asserts nativos; benchmarks com BenchmarkDotNet

- Nenhuma biblioteca de asserção de terceiros.

### ADR-007: Persistência em JSON com `System.Text.Json` e *source generation*

- Escrita atômica (arquivo temporário, flush e substituição) com backup `.bak`, em `%LOCALAPPDATA%\SideWar\`.

### ADR-008: Sem trimming e sem Native AOT

- O WPF não suporta nenhum dos dois. A publicação é self-contained com ReadyToRun (SDD-18).

### ADR-009: HUD do jogo desenhado em Skia dentro do overlay; WPF apenas para janelas completas

- A HUD em Skia é uma superfície única e barata, sem árvore visual WPF ativa durante o jogo.
- **Custo:** a HUD não tem automação de UI (UIA). Essa limitação está registrada em SDD-15.

### ADR-010: Menus da bandeja e do *handle* como menus nativos Win32 (`TrackPopupMenuEx`)

- Menus de contexto WPF sem janela-pai exigem truques de foco e são instáveis.

### ADR-011: Duas janelas no overlay: **OverlayWindow** (campo e HUD) e **HandleWindow** (alça pequena, sempre clicável)

> **Status: Substituída por ADR-028 (sem HandleWindow) e ADR-021 (conjunto de janelas layered).** O widget HUD (SDD-23) assume o papel da alça; o selo de ausente é janela própria click-through. Mantém-se apenas o princípio de que o modo Passivo (`WS_EX_TRANSPARENT`) vale só para a janela da faixa (SDD-04).

- No modo passivo, a OverlayWindow recebe `WS_EX_TRANSPARENT` (clique atravessa a janela inteira). A HandleWindow continua clicável para alternar o modo.
- A HandleWindow pertence à OverlayWindow (owned, na mesma thread) para ficar sempre acima dela.

### ADR-012: Build no WSL e execução e validação no Windows

- Projetos portáveis (`net10.0`) são compilados e testados no WSL.
- Projetos `net10.0-windows` são compilados no WSL com `EnableWindowsTargeting=true`. Executar e testar exige o SDK do Windows, rodando sobre um espelho do repositório em `C:\dev\sidewar` sincronizado por script (SDD-02).
- Nenhum resultado de Windows pode ser declarado a partir do Linux.

### ADR-013: Efeitos visuais fora da simulação

> **Refinada por ADR-027 (sem efeitos de impacto; só números de dano).** Não há projéteis voando nem efeitos de impacto; os números de dano flutuantes continuam fora da simulação, como aqui.

- A simulação emite eventos (`HitEvent`, `KillEvent` etc.) num buffer circular. A renderização cria e anima os efeitos.
- Os efeitos não entram no save nem no hash de determinismo.

### ADR-014: Nenhum acesso à rede

- A aplicação não faz chamadas de rede: sem telemetria, sem atualização automática no MVP e sem fontes remotas.
- Qualquer exceção exige uma nova ADR.

### ADR-015: O jogo nunca abre janela ativável por iniciativa própria

> **Status: Refinada por ADR-021 e ADR-022.** Continua proibido abrir janela **ativável** (WPF) por iniciativa própria. Janelas **não ativáveis** (`WS_EX_NOACTIVATE`: card, banner, caixa, selo) podem aparecer sozinhas **somente** para o card de retorno ("Bem-vindo de volta", D-11) e para os banners/caixas de eventos ao vivo (SDD-26). Nunca roubam o foco.

- Janelas WPF só abrem por ação explícita do usuário (clique na HUD, no handle, no menu ou na bandeja, ou um atalho).
- Eventos do jogo, como a pergunta antes de um chefe, aparecem como aviso dentro do overlay e pausam o que for necessário.

### ADR-016: Regras de construção durante a onda

> **Status: Substituída por ADR-019.** Não há fase Build nem comando de onda manual: as ondas são automáticas e construir/melhorar vale a qualquer momento (SDD-27 RF-27.5). D-07 obsoleto.

- Construir, melhorar e vender são permitidos nas fases **Build** e **Wave**, desde que o jogo não esteja pausado. Na referência, isso só valia em algumas missões.
- **Justificativa:** no overlay, o jogo é acompanhado de relance, e janelas de construção rígidas atrapalham.
- Revisável após playtest (D-07).

### ADR-017: Adaptação do balanceamento à faixa

> **Status: Substituída por ADR-019 e ADR-020.** O fator k = 0,56 e o caminho de ~2000 unidades deixam de existir: o mundo é 1D em DIP (SDD-22 RF-22.2) e o balanceamento idle é o de SDD-27. Revisão por JSON após playtest continua (D-10).

- Os tempos (intervalos, durações, tempo de travessia) são preservados. As distâncias (alcance, velocidade de projétil, raios) escalam por k = 0,56, que é a razão entre os comprimentos de caminho (~2000/3563).
- A perspectiva achatada é removida.
- O inimigo `armored` entra no mix do modo longo, para dar função ao Canhão. Na referência, ele só aparecia em missões específicas.
- Tudo é revisável por JSON após o playtest da etapa E5 (SDD-12).

### ADR-018: Design oficial (jogo idle na barra de tarefas) e tokens em SDD-20

- **Contexto:** o usuário aprovou um novo design: um jogo idle que vive sobre a barra de tarefas do Windows 11.
- **Decisão:** o design aprovado é o **oficial**. Tokens de cor, tipografia, ícones e animações ficam em SDD-20 (sistema visual "Azure Defense"); os layouts em SDD-22 (faixa), SDD-23 (widget) e SDD-24 (painel e cards).
- **Justificativa:** uma única fonte de verdade para o visual evita divergência entre fichas.
- **Substitui:** os valores visuais soltos nas fichas antigas (SDD-08, SDD-09, SDD-15) onde conflitarem. Onde SDD-20 e uma ficha antiga divergirem, SDD-20 vence.

### ADR-019: Gênero idle (ondas automáticas, níveis ilimitados, presença/ausência/offline)

- **Contexto:** o design aprovado é idle, não um tower defense de ondas manuais.
- **Decisão:** as ondas começam sozinhas (SDD-27 RF-27.7); a progressão é ilimitada em níveis, fases e Reservas (prestígio "Migrar"); o jogo tem três estados de presença: Presente, Ausente e Offline (SDD-25). Os modos **Clássico** e **ondas manuais** são removidos, assim como a fase Build e o comando `StartWave`.
- **Justificativa:** o jogo é acompanhado de relance, e o ganho vem do tempo, não da operação contínua.
- **Substitui:** ADR-016 (construção durante a onda) e o modo Clássico de 10 ondas (D-08); a economia de SDD-13 passa a ser a de SDD-27.

### ADR-020: Geometria: faixa de 96 DIP, widget HUD 420×48, slots = botões de apps da barra

- **Contexto:** a cena fica visualmente sobre a barra de tarefas, e as torres ocupam o lugar dos ícones de aplicativos.
- **Decisão:**
  - a faixa de batalha tem **96 DIP** de altura, colada **acima** da barra de tarefas, na **largura do monitor** (área de trabalho);
  - o **widget HUD** de 420×48 DIP ocupa a zona livre à esquerda da barra (fallback: dentro da faixa, canto inferior esquerdo);
  - os **slots de torre** são os botões de aplicativos da barra (lidos por UI Automation, ADR-023), com fallback virtual quando a leitura falha.
- **Justificativa:** reproduz o design aprovado sem tocar na barra do Windows.
- **Substitui:** ADR-003 (mundo 1920×200 com HUD lateral), ADR-017 (escala k) e SDD-05 RF-05.4/05.5/05.9 (tamanhos S/M/L).

### ADR-021: Painel, cards, banners, caixas e selo de ausente são janelas layered Skia `WS_EX_NOACTIVATE`; WPF só para Configurações e Sobre

- **Contexto:** o painel (4 abas), os cards, os banners de evento, as caixas de suprimentos e o selo de ausente precisam aparecer sem roubar o foco e com o visual do design.
- **Decisão:** todas essas superfícies são janelas layered desenhadas em Skia, com `WS_EX_NOACTIVATE`, na T-Game. O WPF continua obrigatório para o ciclo de vida, o dispatcher e as janelas **Configurações** e **Sobre**.
- **Justificativa:** consistência visual, nenhuma árvore WPF ativa durante o jogo e nenhuma troca de foreground. Estende ADR-001 e ADR-009.
- **Substitui/refina:** ADR-001 e ADR-009 (estendidas); a lista de janelas WPF de SDD-01 RF-01.3 (`CampaignWindow`, `ResultWindow`, `QuestionWindow`) deixa de valer; ADR-015 refinada.

### ADR-022: Offline analítico (oculto, fechado ou em sono = zero simulação; cálculo no retorno; teto de 12 h)

- **Contexto:** o jogo idle deve "render" enquanto o usuário está fora, sem custar CPU.
- **Decisão:** com o overlay oculto, o app fechado ou o computador em sono, **nada é simulado**. No retorno, o `OfflineCalculator` (puro, determinístico) calcula os ganhos a partir do snapshot, com **teto de 12 h** (SDD-25).
- **Justificativa:** consumo zero oculto (SDD-16, SDD-07 RNF-07.1) e resultado reprodutível.
- **Substitui:** a política de pausa de SDD-07 RF-07.2 (`Hidden` com `pauseWhenHidden` opcional) e a retomada manual de RF-07.6.

### ADR-023: UI Automation somente leitura numa thread MTA dedicada `SideWar.Shell`

- **Decisão:** a leitura da barra de tarefas usa UI Automation **somente leitura**, numa thread MTA dedicada chamada `SideWar.Shell`, criada **sob demanda**, disparada **por evento** (sem polling contínuo). Após **3 falhas seguidas**, o jogo cai para o fallback virtual (SDD-21).
- **Justificativa:** UIA é lento e pode bloquear; isolá-lo evita travar a T-Game. Nunca se modifica a barra do Windows.
- **Estende:** ADR-002 (acrescenta uma thread).

### ADR-024: Perguntas Azure e picos de demanda são núcleo (D-01 resolvido)

- **Decisão:** as perguntas Azure (chefes) e os picos de demanda fazem parte do jogo base (SDD-26). SDD-19 é **substituída**, exceto o **banco de perguntas** (RF-19.10) e o **ledger** (RF-19.7), que são reaproveitados.
- **Substitui:** a recomendação de D-01 (módulo opcional pós-MVP).

### ADR-025: Fontes Silkscreen + JetBrains Mono (OFL)

- **Decisão:** Silkscreen e JetBrains Mono (licença OFL) substituem Pixelify Sans e IBM Plex nas fichas de renderização. Os TTF são fornecidos pelo usuário (D-03).
- **Substitui:** as fontes citadas em SDD-09 e SDD-20 anteriores à revisão.

### ADR-026: O layout da faixa é parte do estado da simulação (determinismo)

- **Contexto:** comprimento da faixa, posições dos slots, do núcleo e do portal dependem do monitor e da barra, o que ameaçaria o `StateHash` (ADR-004).
- **Decisão:** o layout da faixa (comprimento, posições dos slots, núcleo, portal) **faz parte do estado da simulação**. Muda **somente** pelo comando `LayoutChanged`, aplicado **entre ticks**, e é gravado no save e no `StateHash`.
- **Justificativa:** a simulação continua determinística e reproduzível, sem ler o monitor.
- **Resolve:** DP-22.1.

### ADR-027: Acerto instantâneo

- **Decisão:** não há projéteis voando nem efeitos de impacto. O acerto é instantâneo; o único efeito visual é o número de dano flutuante.
- **Substitui/refina:** ADR-013 (efeitos fora da simulação) quanto a projéteis e efeitos de impacto; ADR-013 continua valendo para os números flutuantes.

### ADR-028: Sem HandleWindow; o selo de ausente é janela própria click-through

- **Decisão:** a HandleWindow deixa de existir. O widget HUD (SDD-23) cumpre o papel de ponto de interação. O selo de ausente é uma janela própria, click-through, com alfa 255 (SDD-22 RF-22.7).
- **Substitui:** ADR-011.

### ADR-029: Formato numérico pt-BR com vírgula decimal em todo lugar

- **Decisão:** todo número exibido usa vírgula decimal e sufixos compactos (`×2,0`, `×0,25`, `48,2K`), conforme SDD-27 RF-27.2 e SDD-20 RF-20.9.

### ADR-030: Padrões propostos nas decisões pendentes locais aceitos

- **Decisão:** todos os padrões propostos nas tabelas "Decisões pendentes" das fichas novas (DP-20.x a DP-27.x) ficam **aceitos** pelo Tech Lead, exceto os escalados ao usuário como D-10 a D-15.
- **Justificativa:** evita bloquear a implementação por detalhes; qualquer padrão pode ser revisto por nova ADR.

### ADR-031: Consolidação das fichas idle

- **Decisão (Tech Lead):** passada final de consistência nas fichas SDD-20 a SDD-27, com os itens abaixo.
  1. **Acerto instantâneo (ADR-027):** SDD-27 não tem projétil teleguiado nem velocidade de projétil (SDD-27 RF-27.4, DP-27.6).
  2. **Velocidade dos inimigos:** vale a de SDD-27 RF-27.6, em DIP/s; SDD-22 RF-22.2 deixa de preservar `traversalSeconds` e remete a SDD-27.
  3. **`LayoutChanged` que encurta a faixa:** inimigos com `X` além do novo ponto de spawn são limitados a ele (SDD-11; DP-11.1).
  4. **Ordem do dano** (SDD-11 RF-11.6, SDD-27 RF-27.4), em `double`: dano(nível) → super hit ×2 → Canhão contra runner ×0,5 → chilled ×1,25 → pico ×3 → menos armadura → `max(1, floor(resultado))`.
  5. **Início:** `NoRun` é transitório; a partida começa automaticamente na primeira execução; o restore após offline retoma sem pausa; `userPaused` sai do save e do restore (SDD-10, SDD-14).
  6. **Sem velocidade 2×** e sem o comando de velocidade (SDD-01, SDD-10, SDD-11, SDD-14, SDD-15).
  7. **Configurações editáveis** (SDD-15 `SettingsWindow`, SDD-14 `settings.json`): fps presente 20/30/60 (padrão 30); fps ausente 4/8 (padrão 8); auto-escala (ligada por padrão); ocultar em fullscreen; iniciar com o Windows; reduzir movimento; atalhos. **Não editáveis (só exibidos):** limiares de presença (5 min; faixas 15/60), teto offline de 12 h e multiplicadores.
  8. **Menu nativo (SDD-15):** "Monitor ▸" escolhe a barra/monitor em que a faixa se acopla (SDD-21/22); "Pausar/Retomar" aplica `PauseReason.User` e para a simulação e a renda.
  9. **Desempenho (SDD-16):** P-20 (painel aberto) ≤ +0,1% de CPU sobre o cenário presente; P-21 (todas as janelas visíveis: faixa, widget, painel, banner, caixa) ≤ +0,2% sobre o presente; ambas "não medido".
  10. **Widget (SDD-23):** badge "+X" de ganhos pendentes do card de retorno (SDD-25), junto aos créditos e clicável para reabrir o card; o rótulo acessível "Fases" abre a aba cujo título é "CAMPANHA", como no design.
  11. **Painel (SDD-24):** linhas de região na aba Migrar são selecionáveis (só as desbloqueadas) e definem a região da próxima migração; cabeçalho do painel = 52 DIP + filete de 1 DIP.
  12. **Distribuição (SDD-18):** `THIRD-PARTY-NOTICES` inclui Silkscreen e JetBrains Mono (OFL) e os assets do design (D-03), sem Pixelify Sans nem IBM Plex.
- **Substitui/refina:** ADR-027 (reforço), ADR-022 (presença e restore), ADR-026 (`LayoutChanged`) e as decisões pendentes DP-11.1 a DP-11.4, DP-24.9, DP-24.12, DP-24.18 e DP-27.6.

### ADR-032: Decisões da releitura

- **Decisão (Tech Lead):** itens aplicados após a releitura crítica das fichas SDD-20 a SDD-27.
  1. **Selo de presença e fundo do widget:** escalados ao usuário como D-16 (selo que não cabe em 420 DIP) e D-17 (fundo próprio do widget, inexistente no design); nenhuma das duas é decidida aqui.
  2. **COLETAR ×2:** novo SDD-26 RF-26.16. Pergunta do mesmo sorteio do tema da fase, 45 s, ledger próprio por relatório de retorno (id do relatório); não conta para estrelas nem missões; acerto dobra créditos e XP; erro, tempo esgotado ou card recolhido valem ×1. SDD-25 referencia RF-26.16.
  3. **Torres `SEM SLOT`:** contam no `n` do custo de construção, não atiram, não entram no bônus do Scale Set nem no DPS offline (SDD-27 e SDD-21).
  4. **Presente → Ausente com extras de pico na fila:** os extras terminam de nascer, sem bônus (SDD-26).
  5. **Caixa após `LayoutChanged` que encurta a faixa:** `x` limitado ao novo intervalo (SDD-26).
  6. **Bônus ×3 com o banner de chefe ocupando o slot:** chip "×3 0:59" ao lado do selo de presença no widget (SDD-23, SDD-26, SDD-22); strings "DEFESA ESCALADA ×3" em `Strings` (SDD-15 RF-15.11).
  7. **Número flutuante:** `top` fixo em 32 (SDD-22).
  8. **Widget no fallback:** o núcleo passa para `x = 428` no layout, via `LayoutChanged` (SDD-22, SDD-23).
  9. **Z-order:** um único `ZOrderCoordinator` em `Platform.Windows` é dono do z-order de faixa, widget, painel e cards (SDD-23, SDD-03 RF-03.9).
  10. **Badge "+X" de pendências:** pílula de 14 DIP, Silkscreen 9, `ambar`, que substitui a linha da taxa enquanto houver pendência (SDD-23).
  11. **Poll de segurança de 60 s da barra:** condicionado; só se a E1 mostrar que os botões XAML não emitem WinEvent, senão é removido (SDD-21, SDD-16).
  12. **Aceitos:** spawn no centro do portal (SDD-22) e estimativas de perdas offline pelas fórmulas (SDD-25).
  13. **Transversais:** SDD-24 passa a vírgula decimal (ADR-029); SDD-15 RF-15.8 sem os itens removidos do menu; SDD-27 RF-27.3 reescrito como "valor bruto antes do ×0,25".
- **Resolve:** DP-22.10, DP-23.2, DP-23.6 e DP-26.5; fixa a condição do poll de DP-21.1.
- **Refina:** ADR-023 (poll condicionado) e ADR-030.

### ADR-033: `IOverlaySurface` em Runtime sem SkiaSharp (quadro BGRA bruto)

- **Status:** decidido pelo Tech Lead (ficha T1.6), revisável pelo usuário.
- **Motivo:** SDD-01 RF-01.6 declarava `SKCanvas BeginFrame()` em `SideWar.Runtime`, o que contradiz RF-01.7 e o `ArchitectureTests` (Runtime não referencia SkiaSharp) e o grafo de projetos (Platform.Windows só referencia Runtime).
- **Decisão:** `IOverlaySurface` fica em `SideWar.Runtime` sem Skia: `PixelSize Size`, `float Scale`, `OverlayFrame BeginFrame()`, `void EndFrame(PixelRect? dirty)`, `void SetOpacity(byte constantAlpha)`. `OverlayFrame` é `readonly struct` com `nint Pixels`, `int Width`, `int Height`, `int RowBytes`, formato fixo BGRA8888 com alfa pré-multiplicado, válido só entre `BeginFrame` e `EndFrame`. `LayeredSurface` (Platform.Windows, RF-08.1) devolve os bits do DIB. O Skia fica em `SideWar.Rendering`: um adaptador (`SkiaFrameCanvas`) cria e cacheia `SKSurface.Create(SKImageInfo(w, h, Bgra8888, Premul), Pixels, RowBytes)` por buffer, sem alocar em regime.
- **Alternativas descartadas:** `IOverlaySurface` só em Platform.Windows (Runtime perde a porta); SkiaSharp em Runtime (altera ArchitectureTests e a regra de camadas).

---

## Decisões pendentes (dependem do usuário)

| ID | Questão | Recomendação do Tech Lead | Bloqueia |
|---|---|---|---|
| D-01 | ~~Manter o tema educativo Azure?~~ **Resolvido por ADR-024:** perguntas Azure e picos são núcleo | n/a | n/a |
| D-02 | ~~Bordas laterais?~~ **Obsoleto:** a faixa lateral não se aplica ao novo design (ADR-020) | n/a | n/a |
| D-03 | Direitos de uso dos assets e fontes. **Atualizado:** os assets são exportados do artefato de design (SDD-22 RF-22.9) e os TTF de Silkscreen e JetBrains Mono são fornecidos pelo usuário (ADR-025) | Aceitar a exportação do artefato e as fontes OFL fornecidas. **Resolvida em 2026-11-10:** o usuário detém todos os direitos sobre os assets do design e sobre as fontes; referência: artefato de design `https://claude.ai/artifact/4cgwgCrocRoYpYEcmzeTF5` | ~~SDD-09 (importação) e SDD-20~~ (não bloqueia mais) |
| D-04 | Canal de distribuição e certificado de assinatura | MVP em ZIP portátil self-contained; v1 em MSIX assinado, se houver certificado; senão, instalador Velopack ou Inno Setup | SDD-18 (após E7) |
| D-05 | Máquina de referência para as metas de desempenho | O usuário informa CPU, GPU, RAM, build do Windows 11, monitores e DPI | Aprovação das metas medidas (SDD-16) |
| D-06 | Áudio no desktop | Pós-MVP, desligado por padrão, com buffers PCM sintetizados | Seção de áudio de SDD-15 |
| D-07 | ~~Construir durante a onda~~ **Obsoleto:** não há fases de construção (ADR-019) | n/a | n/a |
| D-08 | Duração das fases. **Substituído** pelas fases idle de SDD-27 (RF-27.9); o modo Clássico foi removido | Manter os parâmetros de SDD-27 e rever após playtest (D-10) | SDD-27 |
| D-09 | Nome do produto, executável e pasta de dados | "SideWar Desktop", `SideWar.exe`, `%LOCALAPPDATA%\SideWar` | SDD-18 |
| D-10 | Calibração da economia idle (custos, ganhos, multiplicadores) | Valores iniciais de SDD-27, ajustados por JSON após playtest | Balanceamento final (SDD-27, SDD-12) |
| D-11 | O card "Bem-vindo de volta" aparecer sozinho (janela não ativável) ao retornar | Aparecer sozinho, sem ativar a janela (ADR-015 refinada) | SDD-25 RF-25.9 e SDD-24 |
| D-12 | Comportamento com a barra não inferior ou com ocultação automática | Fallback virtual já especificado (SDD-21 RF-21.7, SDD-22 RF-22.1) | SDD-21 e SDD-22 (aceite em barra não inferior) |
| D-13 | Sprite próprio do Scale Set (hoje placeholder) | Criar o sprite no design; enquanto isso, placeholder registrado no MANIFEST | SDD-22 RF-22.9 (assets) |
| D-14 | Títulos das fases 08 a 10 (provisórios) | Aceitar os provisórios de SDD-27 (DP-27.10) até o usuário definir | SDD-27 (conteúdo) |
| D-15 | Barra com Widgets/Copilot ocupando a zona esquerda | O widget HUD vai para a faixa (fallback de SDD-23 RF-23.2) | SDD-23 (posição do widget) |
| D-16 | Selo de presença do widget não cabe em 420 DIP | Selo só com "×2,0"/"×0,25", cor e borda indicando o estado, rótulo completo no nome acessível | SDD-23 (layout final do selo) | E5 (bloqueia SDD-23 layout final) |
| D-17 | Fundo próprio do widget (`bg-painel` alfa 230, borda, raio 4), inexistente no design | Aprovar | SDD-23, SDD-20 | E5 |
| D-18 | Recompensa das missões em renda relativa vs valor fixo do design (+12K) | Renda relativa | SDD-27 (DP-24.7), SDD-24 | E6 (missões) |
