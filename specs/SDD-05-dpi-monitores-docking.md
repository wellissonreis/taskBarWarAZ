# SDD-05: DPI, múltiplos monitores e acoplamento (docking)

**Status:** Rascunho para revisão · **Etapa:** E1 · **Depende de:** SDD-03, SDD-21, SDD-22 · **Bloqueia:** SDD-08, 15, 21, 22, 23

## 1. Identificador e título

SDD-05: Geometria do overlay por monitor, DPI Per-Monitor V2, layout da faixa e reação a mudanças de topologia.

## 2. Objetivo

Posicionar a faixa de forma nítida e correta em qualquer monitor, resolução e escala (100% a 300%), acompanhar mudanças em tempo real (DPI, barra de tarefas, monitor desconectado) e manter as coordenadas do jogo independentes da tela.

## 3. Contexto técnico

- ADR-020 (substitui ADR-003): faixa de 96 DIP acima da barra de tarefas, na largura do monitor (SDD-21, SDD-22); sem HUD lateral e sem tamanhos S/M/L. O histórico de ADR-003 (1920×200 com HUD à esquerda) fica nos RFs substituídos abaixo.
- O processo é **Per-Monitor V2** via manifesto. Com isso, as coordenadas das APIs Win32 são físicas, e o WPF também usa PMv2.
- A área de trabalho de cada monitor vem de `GetMonitorInfoW` (`rcWork`). `SPI_GETWORKAREA` vale só para o primário e **não** deve ser usado.

## 4. Escopo e exclusões

**Escopo:** manifesto de DPI, `MonitorService`, `DockLayout`/`SceneLayout` (cálculo puro; geometria da faixa em SDD-22 RF-22.1), escala de render, reação a `WM_DPICHANGED`, `WM_DISPLAYCHANGE` e `WM_SETTINGCHANGE(SPI_SETWORKAREA)`, persistência do monitor escolhido e modo AppBar (pós-MVP).

**Exclusões:** faixa vertical lateral (D-02, obsoleto), layout da faixa, do widget e dos slots (SDD-21, SDD-22, SDD-23), desenho (SDD-08) e regras do jogo.

## 5. Requisitos funcionais

### RF-05.1: Manifesto (`src/SideWar.App/app.manifest`)

```xml
<application xmlns="urn:schemas-microsoft-com:asm.v3">
  <windowsSettings>
    <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
    <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
  </windowsSettings>
</application>
<compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
  <application><supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/></application> <!-- Windows 10/11 -->
</compatibility>
```

A GameThread também chama `SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)` antes de criar as janelas, como defesa.

### RF-05.2: Modelo de monitor (`MonitorInfo`, em Runtime)

```csharp
public readonly record struct MonitorInfo(
    string DeviceName,      // MONITORINFOEXW.szDevice, ex. "\\.\DISPLAY2"
    PixelRect Bounds,       // rcMonitor (físico)
    PixelRect WorkArea,     // rcWork (físico)
    uint Dpi,               // GetDpiForMonitor(MDT_EFFECTIVE_DPI)
    bool IsPrimary,
    nint Handle);           // HMONITOR (válido apenas na sessão)
```

`MonitorService.Enumerate()` usa `EnumDisplayMonitors` e `GetMonitorInfoW` (`MONITORINFOEXW`).

### RF-05.3: Escolha do monitor

- `settings.dock.monitor = { deviceName, boundsHint }` guarda o nome do dispositivo e o retângulo da última vez.
- Na resolução, a prioridade é:
  1. o mesmo `DeviceName` com bounds iguais;
  2. o mesmo `DeviceName`;
  3. o monitor cujos bounds têm maior interseção com `boundsHint`;
  4. o **primário**.
- O menu da bandeja (e o do widget, SDD-23) mostram "Mover para o monitor N" para cada monitor, com a numeração da enumeração ordenada por `Bounds.Left` e depois `Bounds.Top`.

### RF-05.4: Layout da faixa (`DockLayout.Compute`, função pura em Runtime)

> **Substituído por SDD-22 RF-22.1 (ADR-020).** A faixa tem 96 DIP de altura e a largura da área de trabalho do monitor (`SceneLayout.Compute`), colada acima da barra; não há `StripSize` S/M/L, `HudWidth`, `HandleSize`, mundo 1920×200 nem `anchor`. O algoritmo abaixo é histórico e **não** é implementado.

Entradas: `MonitorInfo`, `DockEdge {Bottom, Top}`, `StripSize {S, M, L}`, `margin` (DIP, padrão 0) e `anchor {Left, Center, Right}` (padrão Right).

Constantes (DIP, isto é, unidades a 96 DPI):

| Constante | S | M (padrão) | L |
|---|---|---|---|
| `TargetFieldHeight` | 136 | 176 | 216 |
| `HudWidth` | 168 | 216 | 264 |
| `HandleSize` | 24 | 28 | 32 |

- `WorldWidth = 1920` e `WorldHeight = 200` (unidades de mundo). `scaleDip = dpi / 96`.
- Algoritmo:

```
availWdip = WorkArea.Width / scaleDip − 2*margin
fieldWdipMax = availWdip − HudWidth
worldToDip = min(TargetFieldHeight / WorldHeight, fieldWdipMax / WorldWidth)
fieldW = WorldWidth * worldToDip ; fieldH = WorldHeight * worldToDip
stripW = HudWidth + fieldW ; stripH = max(fieldH, HudMinHeight=112)
x (DIP) conforme anchor dentro de WorkArea; y = Bottom ? WorkArea.Bottom − margin − stripH : WorkArea.Top + margin
converter para pixels físicos: round(v * scaleDip)
renderScale (px por unidade de mundo) = worldToDip * scaleDip
hudScale (px por DIP) = scaleDip
```

- Saída: `StripGeometry { PixelRect Window; PixelRect HudRect; PixelRect FieldRect; PixelRect HandleRect; float RenderScale; float HudScale; }`. A alça fica no canto superior esquerdo da HUD, com deslocamento de 4 DIP (borda inferior) ou no canto inferior esquerdo (borda superior).
- Se `stripH` exceder 40% da altura da WorkArea, reduzir `worldToDip` proporcionalmente (proteção para telas pequenas).
- `renderScale` é limitado a **3,25** px por unidade (resolução mestre do atlas, SDD-09). Acima disso, o layout reduz `worldToDip` até respeitar o limite.

### RF-05.5: Conversões de coordenadas (`CoordinateMapper`, puro)

> **Substituído por SDD-22 RF-22.2 (ADR-020, ADR-026).** A unidade da cena é o DIP (`ClientPx → Dip = px / scale`) e o mundo é 1D; não há `HudRect`/`FieldRect` nem `RenderScale` por unidade de mundo. As conversões do widget estão em SDD-23 RF-23.8.

- `ClientPx → HudDip`: `(p − HudRect.Origin) / HudScale`.
- `ClientPx → World`: `(p − FieldRect.Origin) / RenderScale`.
- E as inversas. Todas em `float`, sem alocação.

### RF-05.6: Reação a mudanças

| Mensagem (WndProc da OverlayWindow) | Ação |
|---|---|
| `WM_DPICHANGED` | **Ignorar** o retângulo sugerido; recalcular `DockLayout` com o DPI novo; `SetWindowPos` (NOACTIVATE); recriar a superfície (SDD-08); invalidar o cache de sprites escalados (SDD-09); marcar tudo como sujo |
| `WM_DISPLAYCHANGE` | Reenumerar os monitores; resolver o monitor (RF-05.3); recalcular e reaplicar |
| `WM_SETTINGCHANGE` com `wParam == SPI_SETWORKAREA` | Recalcular (a barra de tarefas mudou de tamanho, posição ou ocultação automática) |
| `WM_DEVICECHANGE` | Ignorar (coberto por `WM_DISPLAYCHANGE`) |

- Todas as reações passam por um debounce de 200 ms (deadline do host), para absorver rajadas.
- Enquanto oculto, as mudanças só marcam `layoutDirty`, e a recomputação acontece no próximo Show (SDD-07).

### RF-05.7: Ocultação automática da barra de tarefas

- Se `WorkArea == Bounds` na borda acoplada, a barra pode estar em ocultação automática (SDD-21 consome o mesmo `ABM_GETSTATE` e acrescenta os botões). Nesse caso, aplicar `margin` mínima de **2 px físicos** (SDD-22 RF-22.1 mantém esta regra) nessa borda, para não bloquear a faixa de ativação da barra.
- Detectar a borda da barra com `SHAppBarMessage(ABM_GETTASKBARPOS)` e `ABM_GETSTATE` (`ABS_AUTOHIDE`) na resolução de layout.

### RF-05.8: Modo AppBar (pós-MVP, `settings.dock.reserveSpace`, padrão `false`)

> **Refinado por SDD-22 RF-22.1 (ADR-020).** O AppBar passa a reservar os **96 DIP da faixa** (não mais a altura do HUD/campo antigo). Continua **pós-MVP**.

Quando ligado, registrar a OverlayWindow como AppBar:

1. `ABM_NEW` com mensagem de callback;
2. `ABM_QUERYPOS` / `ABM_SETPOS` com o retângulo da faixa (96 DIP, SDD-22 RF-22.1), o que faz janelas maximizadas encolherem;
3. tratar `ABN_POSCHANGED` recalculando;
4. ao desligar ou sair, chamar `ABM_REMOVE`.

Esse modo resolve a sobreposição da faixa com a barra de status ou o terminal do VS Code. Nessa etapa, implementar somente o cálculo puro e os testes. A integração fica para a etapa E5.

### RF-05.9: Mudança de tamanho e borda pela UI

> **Substituído por SDD-21 e SDD-22 (ADR-020).** Não há tamanho P/M/G nem ancoragem: a faixa segue a barra de tarefas (borda e ocultação automática vêm de SDD-21; a barra não inferior usa o fallback virtual, D-12). "Mover para o monitor N" (RF-05.3) continua.

- O menu da alça oferece "Tamanho: P/M/G", "Borda: inferior/superior" e "Ancoragem: esquerda/centro/direita".
- Cada mudança recalcula o layout, salva as configurações (debounce, SDD-14) e redesenha.

## 6. Requisitos não funcionais

- **RNF-05.1:** Nitidez: com 125%, 150%, 175% e 200%, os sprites são desenhados a partir do cache pré-escalado na escala exata, sem reamostragem por frame (SDD-09), e o texto é renderizado em pixels físicos.
- **RNF-05.2:** A recomputação após `WM_DPICHANGED` termina em ≤ 100 ms, incluindo o rebake do cache (meta, medida na E1).
- **RNF-05.3:** Nenhum pixel da faixa fora da `WorkArea`, salvo o modo AppBar.

## 7. Estratégia de implementação

1. Implementar `MonitorResolver` (e `SceneLayout`, SDD-22) em Runtime, com testes de tabela (Linux). `DockLayout` e `CoordinateMapper` ficam substituídos (RF-05.4/05.5).
2. Implementar `MonitorService` (Platform) e a integração com as mensagens de RF-05.6.
3. No Harness E1, desenhar um contorno de debug da janela da faixa e do widget (tecla de diagnóstico, SDD-16).

## 8. Dependências

SDD-03. Os consumidores SDD-08 (superfície) e SDD-09 (cache) reagem à invalidação.

## 9. Critérios de aceitação

- **CA-05.1:** Os testes de tabela do layout da faixa (`SceneLayout`, SDD-22 T-22.1) passam para as combinações: monitores 1366×768@100%, 1920×1080@100%/125%/150%, 2560×1440@150%, 3840×2160@150%/200%, 3440×1440@100% e 1280×800@150%; bordas Bottom e Top (sem tamanhos S/M/L). Valores esperados calculados e fixados no teste.
- **CA-05.2:** Arrastar o overlay entre monitores com DPIs diferentes, usando "Mover para o monitor N", mantém a faixa nítida, sem sair da `WorkArea` e com o widget em proporção correta (MTC-05.1, com capturas anexadas à evidência).
- **CA-05.3:** Mudar a escala do Windows em Configurações > Tela com o jogo rodando reposiciona e redesenha em ≤ 1 s (MTC-05.2).
- **CA-05.4:** Desconectar o monitor onde está o overlay move a faixa para o primário. Reconectar **não** volta automaticamente, a menos que `DeviceName` e bounds coincidam no próximo início (MTC-05.3).
- **CA-05.5:** Mover a barra de tarefas ou ligar a ocultação automática reposiciona a faixa em ≤ 1 s (MTC-05.4).

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-05.1 | `SceneLayout` (tabela CA-05.1; SDD-22 T-22.1) | Linux |
| T-05.2 | `MonitorResolver` (prioridades RF-05.3) | Linux |
| T-05.3 | ~~`CoordinateMapper`~~ substituído (RF-05.5); conversão px↔DIP testada em SDD-22 | Linux |
| T-05.4 | ~~Proteção de 40%~~ não se aplica (faixa fixa de 96 DIP) | n/a |
| MTC-05.1..4 | DPIs mistos, mudança de escala, desconexão, barra de tarefas | Manual (2 monitores) |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| `DeviceName` mudar entre reinícios (driver e docks USB-C) | Alta | Baixo | `boundsHint` como segundo critério |
| Faixa de 96 DIP ficar apertada em tela pequena | Média | Médio | Validar em playtest; o tamanho é fixo pelo design (ADR-020) |
| Monitor com DPI fracionário (175%) gerar tremor de 1 px | Média | Baixo | Arredondar a geometria uma vez por layout; desenhar com `SKMatrix` de escala única |
| Overlay sobre a barra de status ou o terminal do VS Code | Alta | Médio | Modo passivo, opacidade, peek e AppBar (RF-05.8) |
