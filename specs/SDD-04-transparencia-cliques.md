# SDD-04: Transparência, passagem de cliques e modos de interação

**Status:** Rascunho para revisão · **Etapa:** E1 · **Depende de:** SDD-03, SDD-22 (contrato de alfa e opacidade), SDD-23 (widget) · **Bloqueia:** SDD-15, SDD-22, SDD-23

## 1. Identificador e título

SDD-04: Alfa por pixel, opacidade global, modos Passivo/Interativo e regiões clicáveis.

## 2. Objetivo

Garantir que o overlay **não atrapalhe o uso do computador**. No modo Passivo, todo clique atravessa a **faixa** e chega ao aplicativo de baixo. No modo Interativo, a faixa recebe cliques apenas onde há conteúdo desenhado (torres, SDD-22 RF-22.3; com ADR-034 as torres ficam dentro da barra e o resto da barra continua clicável para o Windows; antes também slots vazios). Os modos valem **somente para a faixa** (ADR-021): widget HUD, painel, cards, banners, caixas e hotspots são **sempre clicáveis** e nunca recebem `WS_EX_TRANSPARENT`; o selo de ausente é sempre click-through (SDD-03 RF-03.9). A troca entre os modos é instantânea, barata e previsível.

## 3. Contexto técnico

- Em janelas `WS_EX_LAYERED` atualizadas por `UpdateLayeredWindow(ULW_ALPHA)`, pixels com **alfa = 0** não recebem mouse: o hit-test atravessa para a janela de baixo. Pixels com alfa ≥ 1 recebem.
- `WS_EX_LAYERED | WS_EX_TRANSPARENT` faz a janela **inteira** ser ignorada no hit-test, inclusive em `WindowFromPoint`.
- `BLENDFUNCTION.SourceConstantAlpha` (0–255) aplica opacidade global sem redesenhar.
- `HTTRANSPARENT` em `WM_NCHITTEST` **não** atravessa para janelas de outros processos. Não usar.

## 4. Escopo e exclusões

**Escopo:** máquina de modos, aplicação de estilos, política de opacidade, esmaecimento sob o cursor ("peek"), retorno automático ao modo Passivo, contrato de alfa do renderizador (SDD-22) e janelas sempre clicáveis (widget, painel, cards, hotspots; antes, a HandleWindow, removida por ADR-028).

**Exclusões:** o conteúdo desenhado (SDD-08), os gestos de jogo (SDD-15) e o atalho global (SDD-15 define as teclas; aqui fica só o comando).

## 5. Requisitos funcionais

### RF-04.1: Modos

> **Substituído por SDD-22 RF-22.3 e RF-22.7 (ADR-021, ADR-028) na opacidade e na alça; o resto é refinado.** Os modos Passive/Interactive aplicam-se **só à janela da faixa**. Opacidade: **presente 255, ausente 128** (substitui o padrão passivo de 180 e o `settings.opacity.interactive`). Não existe HandleWindow: o indicador de modo está no widget (SDD-23) e a alternância é por Ctrl+Alt+G, menu e widget.

```
enum OverlayMode { Passive, Interactive }
```

| Aspecto | Passive (padrão ao iniciar) | Interactive |
|---|---|---|
| `WS_EX_TRANSPARENT` na OverlayWindow | **ligado** | desligado |
| Cliques no campo e na HUD | atravessam para o app de baixo | vão para o jogo onde alfa ≥ 1 |
| Widget, painel, cards, hotspots (substituem a HandleWindow) | sempre clicáveis | sempre clicáveis |
| Opacidade da faixa (`SourceConstantAlpha`) | presente 255, ausente 128 (SDD-22 RF-22.7); sem opção por modo | idem |
| Indicador | no widget (SDD-23); sem alça | idem, com a borda de modo de RF-04.7 |

### RF-04.2: Transições de modo (`OverlayModeController` em Runtime, com aplicação em Platform)

| De | Evento | Para |
|---|---|---|
| Passive | clique no controle de modo do widget (SDD-23) | Interactive |
| Passive | hotkey `ToggleInteractive` | Interactive |
| Passive | menu "Interagir" (bandeja ou widget) | Interactive |
| Interactive | clique no controle de modo do widget (SDD-23) | Passive |
| Interactive | hotkey `ToggleInteractive` | Passive |
| Interactive | `autoPassiveSeconds` (padrão 8 s) sem o cursor sobre o overlay **e** sem arraste ativo **e** fora do modo construção (ADR-034) | Passive |
| qualquer | entrada no **modo construção** (ADR-034, SDD-15 RF-15.3) | Interactive forçado (lembra o modo anterior) |
| Interactive forçado | saída do modo construção (posicionou ou cancelou) | o modo anterior |
| qualquer | overlay oculto (SDD-07) | Passive (ao reaparecer, sempre Passive) |

A aplicação de estilo:

```
SetWindowLongPtrW(overlay, GWL_EXSTYLE, ex ± WS_EX_TRANSPARENT)
SetWindowPos(overlay, null, 0,0,0,0, SWP_FRAMECHANGED|SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_NOOWNERZORDER)
surface.SetOpacity(alpha do modo)  →  um Present (UpdateLayeredWindow apenas com o novo blend; o bitmap é reaproveitado)
```

Ao sair do Interactive com arraste em andamento, o arraste é cancelado (`ReleaseCapture`).

### RF-04.3: Detecção de "cursor sobre o overlay" no modo Interactive

- `TrackMouseEvent(TME_LEAVE)` a cada `WM_MOUSEMOVE` que não esteja já rastreando.
- `WM_MOUSELEAVE` inicia a contagem `autoPassiveSeconds` (deadline no host). `WM_MOUSEMOVE` cancela a contagem.

### RF-04.4: Contrato de alfa do renderizador (obrigatório para SDD-08)

> **Substituído por SDD-22 RF-22.1/RF-22.3 (ADR-021).** A cena não tem fundo: pixels sem entidade têm alfa 0 (equivale a `backgroundAlpha = 0`, sem `settings.background`). A HUD lateral deixou de existir; o widget HUD (SDD-23) é janela própria, sempre clicável, com fundo no próprio pixel. Os itens abaixo ficam como histórico, salvo o último (sprites).

- O fundo da faixa usa a cor `settings.background` com alfa `settings.backgroundAlpha` (0–255, padrão **150**).
- Se `backgroundAlpha == 0`, o fundo fica totalmente transparente: no modo Interativo, clicar em chão vazio **atravessa**. A construção por arraste continua funcionando por causa da captura (SDD-15).
- A HUD (painel esquerdo) usa sempre fundo com alfa ≥ 200, para ser clicável e legível.
- **Vale:** sprites usam o alfa do PNG. Pixels de borda com alfa < 8 são forçados a 0 no pipeline de assets (SDD-09), para evitar "zonas fantasma" clicáveis.

### RF-04.5: Esmaecimento sob o cursor ("peek") no modo Passive

> **Refinado por SDD-22 RF-22.3 (DP-22.3).** O peek continua existindo **só na faixa**; widget, painel, cards, banners e caixas **não** participam do peek (alfa fixo 255). Valores e retângulo do peek: ver DP-22.3 (padrão aceito por ADR-030).

- Configuração `settings.peekOnHover` (padrão **ligado**).
- Enquanto visível em Passive, a T-Game consulta `GetCursorPos` a **8 Hz** (deadline do host; só nesse estado).
- Se o cursor estiver dentro do retângulo da faixa (exceto sobre as janelas sempre clicáveis) por ≥ 250 ms, a opacidade passa para `settings.opacity.peek` (padrão 60 ≈ 24%). Ao sair, a opacidade volta à passiva depois de 150 ms.
- A transição é instantânea (sem animação), para não gerar frames extras. Só chama `SetOpacity`, sem redesenhar.
- Com o overlay oculto ou em Interactive, o polling é desligado.

### RF-04.6: Regiões clicáveis no modo Interactive

> **ADR-034 (vigente):** a janela de cena passa a cobrir também a barra de tarefas (SDD-22 RF-22.1). A regra de clique **não muda**: em Passivo, `WS_EX_TRANSPARENT` faz todo clique (inclusive sobre torres e ícones da barra) chegar ao Windows; em Interativo, só pixels com alfa ≥ 1 recebem clique, e como fora das torres o alfa é 0, o clique sobre ícones, pesquisa, bandeja e espaço livre da barra **atravessa** para `Shell_TrayWnd`. O `HitTarget` da faixa passa a ser `SceneTower(towerId) | BuildOverlay | None` (sem `SceneEmptySlot`); `BuildOverlay` só existe no modo construção, quando o overlay (alfa ≥ 1 na janela inteira) recebe todos os cliques sobre a cena e a barra (SDD-22 RF-22.11). Clique em torre emite `OpenArsenal(towerId)`.
>
> **Histórico:** **Substituído por SDD-22 RF-22.3 (ADR-021).** O `HitTarget` da faixa é `SceneTower(slotIndex) | SceneEmptySlot(slotIndex) | None`; os alvos do widget são de SDD-23 RF-23.8 e os dos hotspots de SDD-22 RF-22.8. Os alvos `HudButton`, `PaletteTower`, `FieldTower`, `FieldGround` e `TowerPanelButton` abaixo não são mais usados.

- São determinadas **apenas** pelo alfa desenhado (sem regiões de janela `SetWindowRgn`).
- O `HitTester` em Runtime converte as coordenadas de cliente em físicas, depois em DIP e depois em coordenadas da HUD ou do mundo (SDD-05), e devolve um `HitTarget`: `HudButton(id) | PaletteTower(type) | FieldTower(id) | FieldGround(worldPoint) | TowerPanelButton(id) | None`.

### RF-04.7: Feedback de mudança de modo

- Ao entrar em Interactive, a borda de estado da faixa (SDD-22 RF-22.6) ganha o destaque de modo (1 DIP, cor de acento) durante o modo inteiro. Ao entrar em Passive, a borda some.
- Nenhum som e nenhuma janela.

## 6. Requisitos não funcionais

- **RNF-04.1:** A troca de modo leva ≤ 16 ms, do evento ao estilo e opacidade aplicados (meta).
- **RNF-04.2:** O "peek" não pode gerar redesenho do conteúdo. Só `SetOpacity` (verificado pelo contador de frames compostos: 0 durante o peek com o jogo parado).
- **RNF-04.3:** O polling de cursor custa ≤ 0,05% de CPU (meta; medido em SDD-16).

## 7. Estratégia de implementação

1. `OverlayModeController` (Runtime, puro): estado, deadlines e eventos de entrada; produz `ModeEffects { bool transparent; byte alpha; bool cancelDrag; }`. Totalmente testável no Linux.
2. `OverlayWindow.ApplyMode(ModeEffects)` em Platform.
3. `LayeredSurface.SetOpacity(byte)`: guarda o alfa e chama `UpdateLayeredWindowIndirect` com `ULW_ALPHA` reaproveitando o DIB atual (sem `prcDirty`).
4. Integrar os eventos `WM_MOUSELEAVE`, `WM_MOUSEMOVE`, o clique no controle de modo do widget (SDD-23) e a hotkey.

## 8. Dependências

SDD-03 (janelas e laço). SDD-05 para a conversão de coordenadas do `HitTester`. SDD-08 para respeitar o contrato de alfa.

## 9. Critérios de aceitação

- **CA-04.1:** Em Passive, `WindowFromPoint` em qualquer ponto da faixa (fora do widget e dos hotspots) retorna a janela de baixo, e não o overlay (T-04.1).
- **CA-04.2:** Em Passive, clicar sobre a faixa numa aba do Chrome que esteja embaixo troca a aba (MTC-04.1). Rolar a roda do mouse sobre a faixa rola o VS Code embaixo (MTC-04.2).
- **CA-04.3:** Em Interactive com `backgroundAlpha=150`, clicar na faixa **não** chega ao app de baixo. Com `backgroundAlpha=0`, clicar em chão vazio chega ao app de baixo, e clicar numa torre seleciona a torre (T-04.2 e MTC-04.3).
- **CA-04.4:** Sem atividade sobre o overlay, o modo Interactive volta a Passive em 8 s ±0,5 s (T-04.3, teste puro com FakeClock, mais MTC).
- **CA-04.5:** O "peek" reduz a opacidade em ≤ 300 ms após o cursor entrar e não incrementa o contador de frames compostos (T-04.4).

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-04.1 | Alternar o modo 50 vezes e verificar `WindowFromPoint` e `GWL_EXSTYLE` a cada troca | WindowsInteractive |
| T-04.2 | Renderizar um quadro de teste com metade alfa 0 e metade alfa 150; `SendInput` de clique nas duas metades; a janela-alvo auxiliar recebe apenas o clique da metade alfa 0 | WindowsInteractive |
| T-04.3 | `OverlayModeController`: tabela RF-04.2 completa com FakeClock | Linux |
| T-04.4 | `PeekController` com posições de cursor simuladas | Linux |
| MTC-04.1..3 | Chrome, VS Code e torre com fundo transparente | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Trocar `WS_EX_TRANSPARENT` em tempo de execução não ter efeito sem `SWP_FRAMECHANGED` | Baixa | Alto | Sempre chamar `SetWindowPos` com `FRAMECHANGED`; T-04.1 |
| Usuário não achar o controle de modo | Média | Médio | Hotkey (Ctrl+Alt+G) e bandeja como alternativas; o widget tem posição fixa (SDD-23) |
| Peek com 8 Hz gerar wakes em notebook com bateria | Baixa | Baixo | Desligar o peek no perfil de baixo consumo (SDD-07) |
| Halos semitransparentes nos sprites criarem áreas clicáveis inesperadas | Média | Baixo | Limiar de alfa no pipeline (RF-04.4) |
