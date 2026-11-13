# SDD-17: Testes automatizados, benchmarks e checklists manuais

**Status:** Rascunho para revisão · **Etapa:** transversal · **Depende de:** SDD-01, SDD-02, SDD-21, SDD-22, SDD-23, SDD-25 (fixtures, goldens e relógio falso das fichas idle) · **Bloqueia:** a aprovação de todas as etapas

## 1. Identificador e título

SDD-17: Estratégia de testes (pirâmide), projetos, convenções, golden images, testes Windows interativos, benchmarks e checklists manuais (MTC).

## 2. Objetivo

Maximizar o que é verificável no WSL (lógica, regras, renderização) e isolar o que exige o desktop do Windows. Cada critério de aceitação das fichas precisa de um teste automatizado ou de um item de checklist manual com evidência.

## 3. Contexto técnico

- ADR-006 (xUnit v3, asserts nativos, BenchmarkDotNet) e ADR-012 (WSL e Windows).
- A referência tinha testes de navegador dependentes de servidor externo e de ganchos globais (ANALISE L12). Não reproduzir esse padrão.
- **Atualização (ADR-018 a ADR-027):** as fichas SDD-20 a SDD-27 acrescentam fixtures da barra de tarefas, golden images da faixa/widget/painel/cards, o `OfflineCalculator` determinístico, a presença com `FakeClock` e novos helpers Windows (RF-17.3, RF-17.4, RF-17.9 e RF-17.10).

## 4. Escopo e exclusões

**Escopo:** projetos de teste, traits, fixtures, fakes (`FakeClock`, `FakeWindowProbe`, `FakeFileSystem`, `FakeSurface`), golden images, helpers Windows (`TestFocusTarget`, `TestFullscreenWindow`), benchmarks e checklists MTC.

**Exclusões:** CI em nuvem e testes de UI do WPF com automação (fora do MVP; as janelas WPF são verificadas por MTC).

## 5. Requisitos funcionais

### RF-17.1: Pirâmide e projetos

| Nível | Projeto | O que cobre | Env |
|---|---|---|---|
| Unitário | Core.Tests | combate, conteúdo, economia, determinismo, arquitetura | Linux + Windows |
| Unitário | Runtime.Tests | `RenderPolicy`, `FixedStepper`, `AppStateMachine`, `FullscreenClassifier` (fixtures), `HotkeyParser`, `SavePolicy`, `OverlayModeController`; **idle:** `TaskbarClassifier` (fixtures `--dump-taskbar`), `SlotMap`, `TaskbarWatcher`, `SceneLayout`/`SceneGeometry`, `WidgetLayout`/`WidgetGeometry`, `PresenceTracker`, `AutoScaler`, `OfflineCalculator`, `EventScheduler` *(histórico/substituídos: `DockLayout`, `DragController`, `IdlePolicy`)* | Linux + Windows |
| Unitário/integração | Persistence.Tests | escrita atômica (FS falso e real em pasta temporária), migração, corrupção, ida e volta | Linux + Windows |
| Integração | Rendering.Tests | golden images, alocação por frame, atlas e cache | Linux + Windows |
| Integração SO | Platform.Windows.Tests | estilos, foco, click-through, DPI (dentro do possível), WinEvent com helpers, handles | **WindowsInteractive** |
| Smoke | App.SmokeTests | inicia o exe real com `--bench idle-static --seconds 10` e com uma pasta de dados temporária; verifica exit 0, log sem `ERROR` e handles liberados | **WindowsInteractive** |
| Desempenho | Benchmarks | P-13, render, serialização | Windows (oficial), Linux (indicativo) |
| Manual | `specs/mtc/MTC-*.md` | itens MTC das fichas | Windows, por pessoa |

### RF-17.2: Convenções

- Nome do teste: `Metodo_Condicao_Resultado`. Um arquivo de teste por classe testada.
- O ID do cenário da ficha vai no atributo: `[Fact(DisplayName = "T-11.3 Cannon splash…")]`, para rastreabilidade.
- Traits: `[Trait("Env","WindowsInteractive")]` e `[Trait("Speed","Slow")]` (> 1 s).
- Proibido `Thread.Sleep` em testes Linux: usar `FakeClock`. Em testes Windows, esperas por condição com timeout (`WaitUntil(cond, 2s)`).

### RF-17.3: Golden images (Rendering.Tests)

- Pasta `tests/SideWar.Rendering.Tests/Golden/<cena>.png`.
- A cena é renderizada num `SKSurface` raster de tamanho fixo (faixa M a 150%) com o `PlaceholderAtlas` **e** com o atlas real (se presente). Fontes: as embutidas (SDD-09). Se faltarem, a cena com texto é ignorada com aviso.
- Comparação: percentual de pixels com ΔE76 > 8. Falha se > 0,5%. Em caso de falha, gravar `<cena>.actual.png` e `<cena>.diff.png` em `artifacts/`.
- Atualização das goldens apenas com `SIDEWAR_UPDATE_GOLDEN=1` e revisão visual registrada no PR.
- Cenas com texto também têm uma variante "sem texto" (texto desabilitado por flag) para a comparação estrita entre SOs.
- **Goldens novas (idle):** a **faixa** (ADR-034: composição de 1920×152 DIP, cena + faixa da barra transparente; antes 96 DIP) (SDD-22 CA-22.7: presente, ausente com selo, interativa, com banner/caixa/chefe, modo construção, hospedeiro `Band`, torres `SEM POSIÇÃO`), o **widget** (SDD-23 CA-23.4: estados presente/ausente, hover, ativo, badge) e o **painel e os cards** (SDD-24: as 4 abas, card "Bem-vindo de volta", card de pergunta). Tamanho da cena por escala (100/125/150/200%), não mais "faixa M a 150%"; os sprites vêm do atlas exportado do design (SDD-22 RF-22.9) e, se faltarem, do `PlaceholderAtlas`. As fontes são Silkscreen e JetBrains Mono (ADR-025); sem os TTF, valem só as variantes sem texto (cálculo de largura do selo: SDD-23 T-23.1b).

### RF-17.4: Helpers Windows (`tests/helpers/`)

- `TestFocusTarget` (WPF, net10.0-windows): janela com TextBox, que expõe por named pipe "texto atual" e "estou em foco?".
- `TestFullscreenWindow` (Win32 mínimo): cria uma janela popup do tamanho do monitor N, ou maximizada com caption, conforme os argumentos. Comandos por stdin: `fullscreen`, `maximize`, `minimize` e `close`.
- `SendInputHelper`: cliques e movimentos em coordenadas físicas.
- **Novos helpers Windows (idle):** `TaskbarFixtureRecorder` (chama `SideWar.exe --dump-taskbar --name <nome>`, SDD-21 RF-21.9, e confere o JSON gerado: sem `Name`, só `ClassName`/`AutomationId`/`rectPx`); `TestTaskbarProbe` (porta `IUiaGateway` falsa/real para provar que toda chamada UIA ocorre na thread `SideWar.Shell`, SDD-21 CA-21.8); `WindowSetInspector` (lê `GWL_EXSTYLE`, owner, z-order e hit-test/`WindowFromPoint` das janelas do conjunto de SDD-03 RF-03.9: faixa, widget, painel, cards, hotspots, selo); `SessionControlHelper` (bloqueio de sessão e simulação de suspensão/relógio para o retorno offline, SDD-25 MTC-25.1) e um `FakeUtcClock`/`FakeClock` com `UtcNow` ajustável.

### RF-17.5: Benchmarks (`bench/SideWar.Benchmarks`)

| Benchmark | Parâmetros | Métrica principal |
|---|---|---|
| `SimTickBench.WaveHeavy` | 44 inimigos, 16 torres LV5, 200 projéteis | média, p99 e alocado (deve ser 0 B) |
| `SimTickBench.WaveMedium` | 20 / 10 / 60 | idem |
| `RenderBench.ComposeWaveHeavy` | faixa M a 1,0; 1,5 e 2,0 | média e alocado |
| `RenderBench.HudRedraw` | — | média |
| `SaveBench.SerializeSession` | `wave-heavy` *(→ `presente-onda`, com os campos novos de SDD-14 RF-14.6)* | média, bytes e alocado |
| `OfflineBench.Compute12h` | snapshot de 12 h, 2.000 ondas (SDD-25 RNF-25.2; SDD-16 P-19) | média, p99 e alocado; meta ≤ 50 ms (não comprovada) |
| `PresenceBench.Tick` | `PresenceTracker` com `FakeClock` | média e alocado (0 B) |
| `FullscreenBench.Classify` | fixture | média |
| `PathBench.PointAt` | — | média |

- Configuração: `[MemoryDiagnoser]`, `[SimpleJob(RuntimeMoniker.Net10_0)]`, `--filter` pelos scripts.
- Resultados copiados para `specs/evidencias/bench/` na aprovação.
- Os cenários `wave-heavy`/`wave-medium` (44 inimigos, 16 torres LV5, ~200 projéteis) são **históricos**: a simulação idle é 1D e sem projéteis (ADR-027); a carga de referência vem de SDD-16 P-17 (24 inimigos, 12 torres).

### RF-17.9: Fixtures da barra de tarefas e testes do cálculo offline (idle)

- **Fixtures `--dump-taskbar`** (SDD-21 RF-21.9): arquivos `tests/SideWar.Runtime.Tests/Fixtures/windows/taskbar-<nome>.json`, capturados pelo usuário no Windows 11 dele: `taskbar-centro`, `taskbar-esquerda`, `taskbar-com-widgets`, `taskbar-muitos-apps` (> 12 botões), `taskbar-pequena`, `taskbar-secundaria` (se houver 2º monitor) e `taskbar-autohide`. Alimentam o `TaskbarClassifier` e o `BuildZoneMap` (ADR-034; antes `SlotMap`) (SDD-21 T-21.2, T-21.3) **no Linux**, sem UIA. Nunca contêm `Name` (privacidade, SDD-14 RF-14.11).
- **`OfflineCalculator` determinístico** (SDD-25 RF-25.6/25.7, RNF-25.1): tabela de casos (snapshot → `OfflineReport`), comparada **campo a campo**, entre Linux e Windows, usando só `+ − × ÷` e `PowTable`; cobre teto de 12 h, relógio atrasado (`elapsed ≤ 0`, com log), 2.000 ondas, parada em chefe, auto-escala a cada 15 min e carry fracionário. Entra no `DeterminismTests` (RF-17.7) com o hash do relatório.
- **Presença com `FakeClock`** (SDD-25 T-25.1, T-25.9): `PresenceTracker` com transições Presente → Ausente em 5 min, `Show` por ação do usuário vs automático, `Hide`/`Show` com 3 h de UTC simulado; sem `Thread.Sleep` e sem `GetLastInputInfo`.
- **Ledger, eventos e economia** (SDD-26 T-26.x, SDD-27 T-27.x): replay e hash com 5 execuções em Linux e Windows.

### RF-17.10: Golden images e testes de janela das fichas idle

- **Linux (Rendering.Tests):** goldens da faixa (SDD-22 T-22.9), do widget (SDD-23 T-23.4), do painel e dos cards (SDD-24) e dos banners/caixa/selo de ausente, em 100/125/150/200%, variante com e sem texto (RF-17.3).
- **WindowsInteractive:** estilos e owner do widget (SDD-23 T-23.1, T-23.2: 100 cliques sem mudar o foreground), cena Interativa (SDD-22 T-22.5), hotspots (T-22.6), z-order após clique em `Shell_TrayWnd` (SDD-23 T-23.7), cards e badge sem ativação (SDD-25 T-25.8) e as consultas UIA (SDD-21 T-21.6 a T-21.8).

### RF-17.6: Checklists manuais (MTC)

- Um arquivo por ficha em `specs/mtc/MTC-<ficha>.md`, com tabela: `ID | passos | esperado | resultado (✔/✘) | evidência (captura ou log) | data | build`.
- Criado na etapa da ficha a partir dos itens MTC-xx.y listados nela.
- O executor (Sonnet) prepara os checklists. **A execução é feita pelo usuário ou por uma sessão com acesso ao desktop**, e o resultado nunca é presumido.

### RF-17.7: Teste de determinismo entre SOs

- O `DeterminismTests` grava `artifacts/determinism/<os>.json` com os hashes finais das 3 partidas scriptadas (SDD-11, CA-11.2).
- O script `scripts/wsl/compare-determinism.sh` compara o arquivo do Linux com o do Windows (copiado do espelho) e falha em caso de divergência.

### RF-17.8: Cobertura

- `dotnet test --collect "XPlat Code Coverage"` (coletor do SDK).
- Metas: Core ≥ 90%, Runtime ≥ 85%, Persistence ≥ 85%. Rendering e Platform não têm meta.
- Relatório no formato cobertura XML em `artifacts/coverage/`. Nenhuma ferramenta extra de relatório é exigida.

## 6. Requisitos não funcionais

- **RNF-17.1:** A suíte Linux completa termina em ≤ 60 s (meta).
- **RNF-17.2:** Zero testes intermitentes: um teste que falhar de forma intermitente 2 vezes é colocado em quarentena (`Skip` com issue) e corrigido antes da aprovação da etapa.
- **RNF-17.3:** Os testes não dependem de rede, do relógio real (exceto Windows interativo) nem de dados do usuário.

## 7. Estratégia de implementação

1. E0: projetos de teste vazios com um teste de arquitetura.
2. Cada etapa adiciona os testes T-xx.y das fichas que implementa, mais o MTC correspondente.
3. E1: helpers Windows e smoke.
4. E3: benchmarks da simulação; E4: de render; E6: de save.

## 8. Dependências

SDD-01 e SDD-02. Todas as fichas fornecem os cenários.

## 9. Critérios de aceitação

- **CA-17.1:** Toda etapa aprovada tem: TRX do Linux, TRX do Windows (incluindo os interativos, quando aplicável), MTC preenchido e benchmark (quando aplicável) em `specs/evidencias/E<n>.md`.
- **CA-17.2:** Cada CA das fichas implementadas aponta para pelo menos um teste T-xx.y ou item MTC (matriz de rastreabilidade em `specs/evidencias/E<n>.md`).
- **CA-17.3:** Coberturas de RF-17.8 atingidas na E3 (Core) e na E6 (Runtime e Persistence); `PresenceTracker`, `AutoScaler` e `OfflineCalculator` ≥ 90% (SDD-25 RNF-25.5).
- **CA-17.4 (idle):** as fixtures de RF-17.9 existem (as capturas dependem do usuário no Windows) e o `TaskbarClassifier` as lê no Linux; o `OfflineCalculator` produz saídas idênticas no Linux e no Windows; as goldens de RF-17.10 passam; os helpers novos de RF-17.4 estão disponíveis para os testes WindowsInteractive.

## 10. Cenários de teste

Os definidos nas fichas SDD-01 a SDD-19 e **SDD-20 a SDD-27** (T-21.x a T-27.x e os MTC-21 a MTC-26; SDD-24 pelo nome, ainda sem numeração de testes nesta data).

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Golden images frágeis entre SOs | Alta | Baixo | Variante sem texto; tolerância ΔE |
| Testes interativos falharem por causa de outras janelas do usuário | Média | Médio | Rodar com a área de trabalho limpa; os helpers posicionam as janelas em coordenadas conhecidas; documentado no MTC |
| SkiaSharp nativo ausente no Linux | Baixa | Médio | `SkiaSharp.NativeAssets.Linux.NoDependencies` nos projetos de teste e benchmark |
| Fixtures da barra dependem de capturas do usuário no Windows 11 dele | Alta | Médio | Testes com nós sintéticos (SDD-21 T-21.1) enquanto as capturas não chegam; fixtures marcadas como pendentes |
| TTF Silkscreen/JetBrains Mono ausentes (D-03) quebrarem as goldens com texto | Média | Baixo | Variantes sem texto; testes com texto ignorados com aviso |
