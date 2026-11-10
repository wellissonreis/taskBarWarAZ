# SDD-15: Interface e controles (HUD Skia, gestos, atalhos, bandeja e janelas WPF)

**Status:** Rascunho para revisão · **Etapa:** E5 (a bandeja e a hotkey mínimas entram na E1) · **Depende de:** SDD-04, 05, 08, 10, 11, 20, 22, 23, 24, 25, 27 (SDD-13 deixou de ser dependência: a CampaignWindow foi substituída) · **Bloqueia:** E6

> **Parcialmente substituída pelo novo design (ADR-018, ADR-021, ADR-028).** HUD, paleta, arraste, painel de torre, `StartWave`, `CampaignWindow` e `QuestionWindow` passam a SDD-22 (faixa), SDD-23 (widget HUD) e SDD-24 (painel e cards). **Mantidos:** bandeja, menu nativo (ADR-010, ampliado: clique direito no widget), atalhos globais (ajustados), `SettingsWindow` (abas ajustadas), `AboutWindow` e `Strings` pt-BR. O texto original é mantido como histórico.

## 1. Identificador e título

SDD-15: Layout da HUD, estados de tela, fluxo de construção, seleção e melhoria, atalhos globais, menus nativos, ícone de bandeja e janelas WPF.

## 2. Objetivo

Permitir jogar inteiramente pelo overlay com o mouse, e controlar o overlay sem ele (atalhos e bandeja), **sem nunca roubar o foco** (ADR-015) e com o mínimo de texto e de janelas.

## 3. Contexto técnico

- ADR-009: HUD em Skia. ADR-010: menus nativos. ADR-015: o jogo não abre janelas ativáveis sozinho.
- O teclado **não** chega ao overlay (`WS_EX_NOACTIVATE`). Os atalhos são globais (`RegisterHotKey`).
- A HUD é desenhada numa "caixa de projeto" de **216×176 DIP**, escalada uniformemente pelo fator de tamanho (S = 0,78; M = 1,0; L = 1,22) e pelo DPI (SDD-05). *(Histórico: substituída pelo widget de 420×48 DIP, SDD-23, sem tamanhos S/M/L, SDD-20 ADR-020.)*

## 4. Escopo e exclusões

**Escopo:** `HudModel`, `HudLayout`, `HudRenderer`, `DragController`, `SelectionController`, `TowerPanel`, `FeedbackToast`, `HotkeyService`, `TrayIcon`, `NativeMenu`, `SettingsWindow`, `CampaignWindow`, `ForegroundRestorer`, textos pt-BR e a seção de áudio (pós-MVP).

**Exclusões:** perguntas e escalabilidade (SDD-19), localização em outros idiomas e controle por gamepad.

## 5. Requisitos funcionais

### RF-15.1: Layout da HUD (caixa de projeto de 216×176 DIP; coordenadas x, y, w, h)

> **Substituído por SDD-23 RF-23.1 a RF-23.8 (ADR-018, ADR-020, ADR-028).** O HUD é o widget de 420×48 DIP (créditos e taxa, onda, selo de presença, nav de 4 botões: Arsenal, Fases, Missões, Migrar), sem alça (SDD-03 RF-03.3 substituído), sem barra de HP do núcleo na HUD (o núcleo está na faixa, SDD-22), sem botão Onda/Pausa/Velocidade e sem estados `NoRun`/`Result`. Os selos "PAUSADO" e "CHEFE NA PRÓXIMA ONDA" deixam de existir (retomada automática, SDD-25 RF-25.2; chefe com banner e card, SDD-26). Tabela abaixo é histórica.

> Os selos de pico e de pergunta são definidos em SDD-19.

| Elemento | Retângulo | Conteúdo |
|---|---|---|
| Alça (HandleWindow, SDD-03) | 4, 4, 28, 28 | desenhada pela própria HandleWindow: ícone de modo (olho ou mão) e ponto de estado (verde = onda, âmbar = Build, cinza = pausado) |
| Núcleo | 36, 6, 146, 22 | rótulo "NÚCLEO", barra de HP (cores de SDD-08 RF-08.4) e texto "87/120" |
| Menu | 186, 4, 26, 24 | "≡": abre o menu nativo (RF-15.8) |
| Linha de status | 8, 32, 200, 18 | "₵ 312" (esq.), "ONDA 17/60" (centro), "NV 5" (dir.) |
| Barra de XP | 8, 51, 200, 3 | progresso `xp/(level·45)` |
| Paleta | 8, 58, 64, 56 · 76, 58, 64, 56 · 144, 58, 64, 56 | Raio, Gelo e Canhão: ícone (sprite *front*), custo e, para os limitados, "2/3". **Desabilitado** (alfa 0,45 e custo em vermelho) se faltar ouro ou o limite foi atingido |
| Botão Onda | 8, 120, 96, 48 | "▶ ONDA" (Build), "EM CURSO" (Wave, desabilitado), "▶ RETOMAR" (pausado) |
| Pausa | 108, 120, 48, 48 | "⏸" / "▶" (ADR-031: não há botão de velocidade 2×) |

- **Fundo da HUD:** `#0A1120` com alfa 220. Borda de 1 DIP `#1E2B4A`. No modo Interactive, borda de acento `#45e0ee` (SDD-04, RF-04.7).
- **Estado `NoRun`:** as linhas da paleta e dos botões dão lugar a um botão "▶ JOGAR" (8, 58, 200, 110), com o subtítulo "Clássico · Campanha". Se houver sessão salva, o botão vira "▶ RETOMAR · Fase N, onda W" e aparece um link menor "Nova partida".
- **Estado `Result`:** a linha de status mostra "VITÓRIA ★★☆" ou "DERROTA · onda 23". A paleta dá lugar aos números (abates, HP final). Os botões ficam: [REPETIR] (8, 120, 64, 48), [PRÓXIMA] (76, 120, 64, 48; desabilitado se não se aplica) e [MAPA] (144, 120, 64, 48).
- **Selos sobre o campo (camada Overlay, SDD-08):**
  - "PAUSADO" ou "PAUSADO: AUSENTE", centralizado no campo, fonte de 18 DIP, fundo alfa 200;
  - "CHEFE NA PRÓXIMA ONDA" (Build antes de onda de chefe), pequeno, no topo do campo.

### RF-15.2: `HudModel` (struct imutável) e versão

> **Reaproveitado como padrão (SDD-23 RF-23.9).** O modelo imutável com `Version` passa a ser o `WidgetModel` (versão por **texto exibido**, créditos e taxa coalescidos em ≤ 2 redesenhos/s); o painel e os cards seguem o mesmo padrão (SDD-24). Os "estados de habilitação e o `RunState`" não existem mais.

- O modelo é construído a cada wake a partir da simulação e do Runtime. Contém os campos exibidos, os estados de habilitação e o `RunState`.
- O `HudModel.Version` só incrementa quando algum campo visível muda (comparação campo a campo). Isso aciona o redesenho do cache da HUD (SDD-08, RF-08.2).

### RF-15.3: Construção (arrastar e soltar)

> **Substituído por SDD-22 RF-22.3 e SDD-24 RF-24.5 (aba Arsenal) com SDD-27 RF-27.5 (ADR-020).** Não há paleta nem arraste: as torres ocupam os **slots = botões de apps da barra** (SDD-21). No modo interativo da faixa, clicar numa torre ou num slot vazio abre o **Arsenal focado** naquele `slotIndex` (`OpenArsenal(slotIndex)`), onde se constrói e melhora (`BuildTower(type, slot)`, `UpgradeTower(id)`). `DragController`, `PlacementValidator` e o modo "tocar e colocar" saem do escopo.

1. `WM_LBUTTONDOWN` sobre um tile habilitado: `DragController.Arm(type, ponto)` e `SetCapture`.
2. `WM_MOUSEMOVE`: se o deslocamento passar de **6 px físicos**, a máquina entra em `Dragging`. Cada movimento converte para mundo e valida (`PlacementValidator` e ouro) e gera o `DragView { type, x, y, range, valid, reason }`. Isso marca a tela como suja, e o fps de arraste vale (SDD-10).
3. O fantasma desenha o sprite com alfa 0,7, o anel de alcance (verde `#72e4b9` quando válido e vermelho `#ff6a7e` quando inválido) e o texto do motivo, 14 DIP acima do fantasma. Se for válido mas não alcançar a pista (`CoversRoad` falso), mostra o aviso âmbar "Não alcança a pista".
4. `WM_LBUTTONUP`: se o ponto for válido, posta `BuildTower(type, x, y)` e o resultado volta como feedback. Se for inválido, cancela e mostra o toast com o motivo. Em seguida, `ReleaseCapture`.
5. Botão direito ou `WM_CAPTURECHANGED` durante o arraste cancelam.
6. **Alternativa sem arraste ("tocar e colocar"):** clicar e soltar no tile sem passar do limiar deixa o tipo **armado** (o tile fica destacado). O próximo clique esquerdo no campo constrói ali. O botão direito ou o mesmo tile desarmam. O tipo armado expira em 10 s.

### RF-15.4: Seleção e painel da torre

> **Substituído por SDD-24 (aba Arsenal) e SDD-22 RF-22.3.** Não há `TowerPanel` flutuante, venda (SDD-27 DP-27.3), nível máximo 5 nem anel de alcance em hover; o Arsenal mostra nível, custo de melhoria, alvo e o selo "SEM SLOT" (SDD-21 RF-21.8). O hit-test das torres é o `SceneHitTester` de SDD-22 (`SceneTower`/`SceneEmptySlot`).

- Clicar numa torre (hit-test pelo retângulo do sprite em mundo, de trás para frente na ordem de desenho) seleciona. Clicar em chão vazio ou com o botão direito desseleciona.
- Ao passar o mouse sobre uma torre, o anel de alcance aparece (somente no Interactive).
- **TowerPanel** (168×64 DIP, desenhado pela HUD sobre o campo):
  - posição acima da torre se `towerY > 100` (metade da faixa), senão abaixo; limitado ao `FieldRect`;
  - linha 1: "Raio · LV3" e "DPS 44";
  - linha 2, botões:
    - [▲ 119₵] (desabilitado em LV5 ou sem ouro; o tooltip mostra a prévia "dano 32→43, alcance 157→166");
    - [VENDER +99];
    - [ALVO: Primeiro ▸] (cicla First → Last → Strong → Weak);
    - [×].
  - Durante o hover em [▲], o anel tracejado âmbar mostra o alcance após a melhoria.

### RF-15.5: Feedback (`FeedbackToast`)

> **Ajustado por SDD-24/SDD-27 (ADR-021).** O toast do campo é substituído pelo feedback dos cards e do painel (SDD-24). Do mapa abaixo ficam apenas as mensagens que ainda têm origem: `InsufficientGold` ("Faltam {n} ₵", valor formatado por SDD-27 RF-27.2), `AlreadyUsed` (SDD-26 RF-26.7) e `NotAvailable` (SDD-01 RF-01.6); saem `Road`, `Overlap`, `Core`, `Entry`, `OutOfField`, `LimitReached`, `MaxLevel`, `Paused`, `InvalidPhase`, `QuestionPending` e as de escala (balance, limit; SDD-19 removida). *O texto final dos feedbacks do painel é de SDD-24 (ver DP-24.3: clique em MELHORAR sem créditos); lacuna registrada no report.*

- Uma mensagem por vez, com 2,5 s de duração, desenhada na parte inferior do campo, centralizada, com fundo alfa 200.
- Mapa de mensagens (pt-BR, em `Strings`):
  - `InsufficientGold`: "Faltam {n} ₵";
  - `Road`: "Sobre a estrada";
  - `Overlap`: "Sobrepõe outra torre";
  - `Core`: "Perto demais do núcleo";
  - `Entry`: "Perto demais da entrada";
  - `OutOfField`: "Fora do campo";
  - `LimitReached`: "Limite de {n} {torre}";
  - `MaxLevel`: "Nível máximo";
  - `Paused`: "Jogo pausado";
  - `InvalidPhase`: "Indisponível agora";
  - `QuestionPending`: "Responda a pergunta do chefe";
  - `AlreadyUsed`: "Já usado nesta onda";
  - balance (escala sem saldo): "Saldo insuficiente para escalar";
  - limit (réplica já existente): "Limite de uma réplica".

### RF-15.6: Atalhos globais (`HotkeyService`, T-Game)

> **Ajustado (SDD-04, SDD-07, SDD-22, SDD-25; ADR-028).** Atalhos vigentes: **Ctrl+Alt+G** = modo interativo **da faixa** (Passivo ↔ Interativo, SDD-04/SDD-22 RF-22.3); **Ctrl+Alt+H** = visibilidade (`HiddenReason.User`); **Ctrl+Alt+P** = pausa (`PauseReason.User`; os motivos são `{ User, Hidden, Dialog }`, SDD-25 RF-25.2, então não há conversão de `Idle`). **`StartWave` (Ctrl+Alt+N) é removido** (ondas automáticas, ADR-019). `RegisterHotKey` usa a janela da faixa (`SceneWindow`, T-Game), pois a HandleWindow não existe mais. Um atalho do jogo conta como **interação de presença** (SDD-25 RF-25.1).

| Ação | Padrão | Comportamento |
|---|---|---|
| `ToggleInteractive` | Ctrl+Alt+G | SDD-04 |
| `ToggleVisibility` | Ctrl+Alt+H | adiciona ou remove `HiddenReason.User` (SDD-07) |
| `TogglePause` | Ctrl+Alt+P | adiciona ou remove `PauseReason.User`. Se o motivo for `Idle` ou `Hidden`, convertê-lo e retomar |
| ~~`StartWave`~~ | ~~Ctrl+Alt+N~~ | **Removido** (ADR-019; texto original: `StartWave`; se pausado, apenas retoma) |

- `RegisterHotKey(janelaDaFaixa, id, MOD_CONTROL|MOD_ALT|MOD_NOREPEAT, vk)` (antes: `handleWindow`). Se falhar (`ERROR_HOTKEY_ALREADY_REGISTERED`), registrar no log, marcar o atalho como "indisponível" nas Configurações e mostrar um toast **uma vez** por sessão: "Atalho Ctrl+Alt+G em uso por outro app".
- Com o overlay oculto, apenas `ToggleVisibility` age. Os demais são ignorados.
- O formato textual dos atalhos é `Ctrl+Alt+Shift+Win+<Tecla>`, interpretado por `HotkeyParser` (Runtime, puro, testado).

### RF-15.7: Ícone de bandeja (`TrayIcon`, T-UI)

- `Shell_NotifyIconW(NIM_ADD)` com uma janela oculta (HwndSource sem RootVisual) e `uCallbackMessage`. Ícone `.ico` embutido (16/20/24/32/48).
- **Tooltip:** "SideWar · Onda 17/60 · ₵312", atualizado no máximo a 1 Hz e só se mudou (créditos no formato compacto pt-BR de SDD-27 RF-27.2, por exemplo `₵48,2K`; sem atualização com o overlay oculto, que está Offline).
- Clique esquerdo alterna a visibilidade. Clique direito abre o menu nativo.
- Tratar `TaskbarCreated` (`RegisterWindowMessage("TaskbarCreated")`) para recriar o ícone quando o Explorer reinicia.

### RF-15.8: Menu nativo (alça, botão "≡" e bandeja)

> **Ajustado (ADR-010 mantida; ADR-028; SDD-23 DP-23.4).** A alça e o botão "≡" não existem mais. O menu nativo abre **pelo ícone da bandeja e por clique direito no widget** (SDD-23 RF-23.8), com os itens: **Modo interativo** (✓ se ativo), **Ocultar** (na bandeja: Mostrar), **Configurações…**, **Sobre o SideWar…**, **Sair** (mais Pausar/Retomar). Saem "Campanha…", "Novo jogo Clássico", "Tamanho ▸" e "Borda ▸" (sem S/M/L e com borda dada pela barra, ADR-020). O item "Monitor ▸" **escolhe a barra/monitor em que a faixa se acopla** (SDD-21, SDD-22; ADR-031). "Pausar / Retomar" aplica `PauseReason.User` e **para a simulação e a renda** (ADR-031). O clique direito no widget conta como interação de presença (SDD-23 RF-23.8) e **não** abre outro menu. A mecânica de exibição (`TrackPopupMenuEx`, `ForegroundRestorer`) não muda.

Itens (pt-BR), com estados marcados (`MF_CHECKED`) e desabilitados (`MF_GRAYED`) conforme o estado:

```
Modo interativo            (✓ se ativo)
Pausar / Retomar
Ocultar overlay            (bandeja: Mostrar overlay)
───
Monitor ▸ 1 (1920×1080) | 2 (2560×1440)
───
Configurações…
Sobre o SideWar…
Sair
```

- Exibição: `prev = GetForegroundWindow()`, depois `SetForegroundWindow(owner)` (exigido pelo Win32 para o menu fechar corretamente), `TrackPopupMenuEx(TPM_RETURNCMD|TPM_RIGHTBUTTON|TPM_NONOTIFY)` e `PostMessage(owner, WM_NULL)`.
- Se o comando escolhido não abrir janela WPF, `ForegroundRestorer.Restore(prev)`: `SetForegroundWindow(prev)` se `IsWindow(prev)`. Isso é permitido porque o processo é o foreground naquele momento.

### RF-15.9: Janelas WPF (T-UI; abertas **somente** por ação do usuário)

> **Ajustado (ADR-021).** Só `SettingsWindow` e `AboutWindow` continuam WPF. **`CampaignWindow` é substituída** pela aba Fases do painel (rotulada "Fases" no nav de SDD-23; SDD-24 RF-24.6 "Aba Campanha"; fases e estrelas de SDD-27 RF-27.9) e **`QuestionWindow` pelo card de pergunta** (SDD-24 RF-24.9, SDD-26 RF-26.5; a pergunta **não pausa**, `PauseReason.Question` não existe). Abas da `SettingsWindow` ajustadas: **Geral** (iniciar com o Windows; ocultar em fullscreen: mesmo monitor/qualquer/nunca); **Faixa** (monitor, `taskbar.useUia` de SDD-21 RF-21.7; opacidades, tamanhos e borda saem); **Presença e offline** (estado da auto-escala, espelho do toggle do Arsenal, SDD-25 RF-25.5; limiar de ausência de 5 min, card offline a partir de 10 min e teto de 12 h exibidos como constantes de SDD-25, sem edição; saem "pausar ao ocultar", "retomar automático" e "inatividade em minutos"); **Desempenho** (fps **presente**, padrão 30, e fps **ausente**, padrão 8, SDD-22 RF-22.10; economia na bateria; forçar economia); **Atalhos** (Ctrl+Alt+G, H e P; sem StartWave); **Acessibilidade** (reduzir movimento); **Diagnóstico**. > **Ajustado por ADR-031 (resolve a lacuna de faixas e limiares).** Editáveis: fps **presente** 20/30/60 (padrão 30); fps **ausente** 4/8 (padrão 8); auto-escala (ligada por padrão); ocultar em fullscreen; iniciar com o Windows; reduzir movimento; atalhos. **Não editáveis (só exibidos):** limiares de presença (5 min; faixas 15/60), teto offline de 12 h e multiplicadores.

Mesmos ajustes persistidos em SDD-14 RF-14.4.

| Janela | Abre por | Conteúdo | Pausa |
|---|---|---|---|
| `SettingsWindow` | menu "Configurações…" | Abas: **Geral** (iniciar com o Windows, ocultar em fullscreen: mesmo monitor/qualquer/nunca, pausar ao ocultar, retomar automático, inatividade em minutos); **Overlay** (tamanho, borda, ancoragem, margem, monitor, opacidades, alfa do fundo, peek); **Desempenho** (fps ativo 20/30/60, economia na bateria, forçar economia); **Atalhos** (caixas de captura de combinação, botão "Restaurar padrão", status "indisponível"); **Acessibilidade** (reduzir movimento); **Diagnóstico** (abrir pasta de logs, mostrar painel de diagnóstico) | não |
| `CampaignWindow` | "Campanha…", o botão "MAPA" da HUD ou "JOGAR" → Campanha | Lista das 10 fases (título, ondas, estrelas, cadeado), briefing da fase selecionada, botões [INICIAR FASE] e [MODO CLÁSSICO · 10 ONDAS] | sim (`PauseReason.Dialog`) se `InStage` |
| `QuestionWindow` | **somente** por clique no aviso do overlay (SDD-19) | Pergunta do chefe e alternativas (SDD-19) | sim (`PauseReason.Question`) |
| `AboutWindow` | "Sobre…" | versão, licenças (OFL e assets), pasta de dados | não |

- As janelas WPF são **fechadas** (não ocultadas) ao sair e recriadas ao abrir, para liberar a árvore visual.
- Ao fechar, `ForegroundRestorer.Restore(prev)` com o `prev` capturado no momento da abertura.
- O estilo usa um `ResourceDictionary` próprio escuro (`Theme.xaml`) com as cores da HUD e **sem** bibliotecas de terceiros.
- As alterações em `SettingsWindow` aplicam na hora (comando para a T-Game) e salvam com debounce (SDD-14).

### RF-15.10: "JOGAR" na HUD

> **Substituído por SDD-27 RF-27.9 (ADR-019).** Não há `NoRun`, "JOGAR", Clássico ou Campanha: a partida idle sempre existe e retoma sozinha (SDD-14 RF-14.8).

- Clicar em "▶ JOGAR" (sem sessão salva) abre o **menu nativo** com duas opções: "Clássico · 10 ondas" e "Campanha…". A primeira começa direto. A segunda abre a `CampaignWindow`.

### RF-15.11: Textos

- Todas as strings visíveis ficam em `SideWar.Runtime/Localization/Strings.cs` (pt-BR), usadas pela HUD e pelo WPF. **Ajuste:** também as do widget, painel, cards, banners e selos (SDD-22/23/24), os rótulos "Arsenal", "Fases", "Missões", "Migrar" (SDD-23 RF-23.7) e os números no formato pt-BR com vírgula decimal (ADR-029, SDD-27 RF-27.2). Inclui as strings **"DEFESA ESCALADA ×3"** (banner não clicável do bônus de pico, SDD-26 RF-26.3, SDD-22 RF-22.8) e **"×3 0:59"** (chip do widget, SDD-23 RF-23.3), ADR-032.
- Nenhum texto literal em renderizadores e janelas.

### RF-15.12: Áudio (pós-MVP, D-06): especificação preliminar

- Desligado por padrão. Eventos sonoros vindos de `SimEvent`: construir, tiro (com no máximo 1 a cada 120 ms; sem projéteis, o "tiro" acompanha o acerto instantâneo, ADR-027), abate, núcleo atingido, início de onda, chefe, vitória e derrota *(vitória/derrota: sem equivalente no idle; a conclusão de fase e a queda do núcleo os substituem)*.
- Receitas sintetizadas da referência (`audio.ts`), convertidas em buffers PCM 16-bit 22 kHz no início.
- Reprodução via `waveOut*` com 4 vozes e mixagem simples, ou `PlaySound(SND_MEMORY|SND_ASYNC)` (1 voz). A escolha é definida na etapa de áudio por ADR, com medição.

## 6. Requisitos não funcionais

- **RNF-15.1:** Do clique ao feedback visual: ≤ 50 ms (1 a 2 frames a 30 fps).
- **RNF-15.2:** Nenhuma ação do jogo exige teclado com o overlay focado. Todas as ações têm caminho por mouse no overlay e por atalho ou janela.
- **RNF-15.3:** Contraste do texto da HUD ≥ 4,5:1 sobre o fundo da HUD.
- **RNF-15.4:** Abrir a `SettingsWindow` leva ≤ 400 ms. A memória adicional com a janela aberta é medida e registrada; ao fechar, ela é liberada (≤ +5 MB de diferença após GC; meta).

## 7. Estratégia de implementação

1. E1: `TrayIcon` mínimo (Sair, Interagir) e `HotkeyService` com `ToggleInteractive` e `ToggleVisibility`.
2. E5: menu completo (bandeja e clique direito do widget) e `SettingsWindow`; o widget é de SDD-23, o painel e os cards de SDD-24. *(Histórico: `HudLayout`, `HudRenderer`, `DragController`, `SelectionController`, `TowerPanel` e `FeedbackToast` foram substituídos.)*
3. E6: **sem** `CampaignWindow` nem estados `NoRun`/`Result` (substituídos; ver SDD-24).
4. Testes de imagem da HUD (Linux) para cada estado.

## 8. Dependências

SDD-04 (modos e hit-test), SDD-05 (escala), SDD-08 (desenho), SDD-10 (pausa), SDD-11 (comandos), SDD-20 (tokens), SDD-22 (faixa), SDD-23 (widget), SDD-24 (painel e cards), SDD-25 (presença) e SDD-27 (formatação e comandos). SDD-13 (campanha) deixou de ser dependência.

## 9. Critérios de aceitação

> **Ajustes:** CA-15.1 (arraste), CA-15.2 (golden da HUD antiga) e CA-15.3 (partida Clássica só com o mouse) são **substituídos** por SDD-23 (CA-23.4, CA-23.5), SDD-22 (CA-22.4, CA-22.7) e SDD-24. CA-15.4 vale com os atalhos G, H e P (sem N); CA-15.5 vale para o menu da bandeja **e do clique direito no widget**; CA-15.6 não muda.

- **CA-15.1:** > **Substituído por SDD-23/SDD-24 (ADR-019/020).** Os testes da máquina de arraste (arm, limiar, válido, inválido, cancelamento por botão direito e por perda de captura, tocar e colocar com expiração) passam (Linux).
- **CA-15.2:** > **Substituído por SDD-23/SDD-24 (ADR-019/020).** As golden images da HUD nos estados NoRun, Build com ouro, Build sem ouro, Wave, Pausado e Result passam (SDD-08, CA-08.3).
- **CA-15.3:** > **Substituído por SDD-23/SDD-24 (ADR-019/020).** Partida Clássica completa jogada só com o mouse no overlay, sem tocar no teclado, com o VS Code em foco o tempo todo. O foco não muda (MTC-15.1).
- **CA-15.4:** Os 4 atalhos funcionam com o Chrome em foco. Um atalho em conflito gera o aviso e o status "indisponível" (MTC-15.2 e T-15.3 com `HotkeyParser`).
- **CA-15.5:** O menu da bandeja e o da alça abrem, executam e devolvem o foco ao aplicativo anterior (MTC-15.3).
- **CA-15.6:** Reiniciar o Explorer (`taskkill /f /im explorer.exe` e depois `start explorer`) recria o ícone da bandeja (MTC-15.4).
- **CA-15.7 (equivalente a CA-15.1 a CA-15.3):** O menu nativo abre pela bandeja e por clique direito no widget, com os itens de RF-15.8, e devolve o foco ao app anterior (SDD-23 CA-23.11).
- **CA-15.8 (equivalente a CA-15.4):** Ctrl+Alt+G (modo interativo da faixa), Ctrl+Alt+H (visibilidade) e Ctrl+Alt+P (pausa) funcionam com outro app em foco; não há Ctrl+Alt+N.
- **CA-15.9:** A `SettingsWindow` expõe as opções editáveis da ADR-031: fps presente 20/30/60 (padrão 30), fps ausente 4/8 (padrão 8), auto-escala, ocultar em fullscreen, iniciar com o Windows, reduzir movimento e atalhos; limiares de presença, teto offline de 12 h e multiplicadores só são exibidos.

## 10. Cenários de teste

> **Ajustes:** T-15.1 (arraste), T-15.2 (`HudModel`; vira o do `WidgetModel`, SDD-23 T-23.6), T-15.4 (golden da HUD; vira SDD-23 T-23.4) e T-15.5 (hit-test da torre; vira SDD-22 T-22.4) estão substituídos. T-15.3 (`HotkeyParser`) continua; MTC-15.1 passa a ser "jogar 30 min só com o mouse, sem mudar o foco" (SDD-23 MTC-23.2).

| ID | Cenário | Env |
|---|---|---|
| T-15.1 | > **Substituído por SDD-23/SDD-24 (ADR-019/020).** `DragController` (CA-15.1) | Linux |
| T-15.2 | > **Substituído por SDD-23/SDD-24 (ADR-019/020).** `HudModel.Version` só muda quando um campo visível muda | Linux |
| T-15.3 | `HotkeyParser` (válidos, inválidos, normalização) | Linux |
| T-15.4 | > **Substituído por SDD-23/SDD-24 (ADR-019/020).** Golden images da HUD | Linux + Windows |
| T-15.5 | > **Substituído por SDD-23/SDD-24 (ADR-019/020).** Hit-test da torre (sobreposição por ordem de desenho) | Linux |
| MTC-15.1..4 | Foco (MTC-15.1 substituído por SDD-23 MTC-23.2), atalhos, menus e Explorer | Manual |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| HUD sem UIA (leitores de tela) | Certa | Médio | Atalhos e janelas WPF cobrem as ações principais; registrar a limitação no README |
| `SetForegroundWindow` para o menu falhar por regras de foreground | Baixa | Baixo | O clique do usuário concede a permissão; fallback sem menu (log) |
| HUD apertada no tamanho S (168 DIP) | Média | Médio | Validar no playtest; a caixa de projeto escala de forma uniforme *(obsoleto: sem S/M/L; o risco do widget é o encaixe de 420 DIP, SDD-23 DP-23.1)* |
| Atalhos globais conflitarem com IDEs (Ctrl+Alt+…) | Média | Baixo | Configuráveis e com aviso; escolher padrões incomuns |
