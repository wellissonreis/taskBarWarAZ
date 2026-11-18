# SDD-32: Fim de corrida gracioso e legado (cartão discreto, Azure Advisor, recomeço automático e runas iniciais)

**Status:** PROPOSTA com **decisão de rumo do usuário** (ficha SDD-32, 2026-11-17: "fim gracioso + legado"). Decisões de arquitetura registradas na §13 · **Etapa:** ciclo "TD" · **Depende de:** SDD-28 (RF-28.14 HUD, RF-28.21 patente e recorde, RF-28.24 derrota que ensina), SDD-29 (runas/teia, corrida contínua §20), SDD-14 (persistência e migração), SDD-10/11 (núcleo determinístico, ADR-026) e das fichas **TD.30a a TD.30c** (executadas antes) · **Relaciona-se com:** SDD-31 (o Azure Advisor aponta torres novas **sem depender delas**, §5) · **Não edita** SDD-28, SDD-29, SDD-31, `DECISOES.md` nem código: só especifica. Fichas de execução: **TD.32a e TD.32b** (§11), entre TD.30c e TD.31a.

> Todo número aqui é **valor inicial ajustável** e **não medido** (README §2.3); a aritmética do §2 é conta direta sobre o JSON, não medição. Base de código descrita: HEAD `ca25fe1`. O working tree tem alterações locais de outra ficha em `TdMatchState.Bonus.cs`, `TdSanityBot.cs`, `TdUiController*.cs`, `TdUiModel.cs`, `TdUiRenderer.cs` e testes: o estado delas **não** é tratado aqui, e a TD.32 só começa depois de commitadas (§11.3).

## 1. Objetivo

Visão do usuário que governa tudo (memória `visao-jogar-no-trabalho`): **jogar enquanto trabalha; discreto, infinito, cantinho, sem roubar foco**. A corrida contínua (TD.28a/b) é "infinita" em intenção, mas **toda corrida longa termina em Derrota** (§2). Em vez de esconder isso, a decisão do usuário é transformar o fim em parte do jogo:

1. **Fim gracioso:** a Derrota vira um **cartão discreto no cantinho** (sem modal, sem foco), que resume a corrida, dá o recorde, a patente, o que mais vazou e **uma dica "Azure Advisor"**, e oferece recomeçar (manual, ou **automático** por opção).
2. **Legado:** cada corrida rende **fragmentos de legado**; a corrida seguinte **começa com algumas runas já despertas**. É um **degrau suave** (as primeiras ondas ficam menos punitivas), **não** um poder que compra vitória nem pula fases.

O que **não** muda: a simulação em si (não há "modo difícil" novo), o balanceamento das fases, a economia de créditos, as runas existentes (32 nós, 8 efeitos), nem a regra "só a derrota encerra" da corrida contínua.

## 2. Por que toda corrida longa termina (ancorado no código)

Fatos verificados no HEAD (aritmética, **não medido**):

| Item | Onde | Crescimento |
|---|---|---|
| HP dos inimigos por onda | `td-stages.json` `rules.hpWaveGrowth` = 1,05; `TdMatchState.Tick.cs` `ScaledHp` (multiplicação iterativa) | **exponencial** no número da onda **da fase** (×1,05^(w−1): ≈ ×2,0 na onda 15 da fase 6); reinicia a cada fase, mas os fatores de fase e de volta acumulam |
| HP por volta completa | `TdMatchState.Tick.cs` `CycleHpGrowth` = 1,25 | ×1,25 **por volta**, composto sobre o anterior |
| HP por fase | `hpStageStep` = 0,05 | +5% por fase (linear, pequeno) |
| Renda do jogador | `td-runes.json` `creditSources`: `waveBonusBase` 120 + `waveBonusPerWave` 10 por onda | **linear** (onda 30 ≈ 420 por onda) |
| Custo de torre | `towerCostTable`, 48 degraus, ~×1,3 por torre a mais | **exponencial**, teto técnico de 48 torres |
| Espaço | faixa `TdBand` finita, snap de 44 DIP; `maxEnemiesOnField` 44 | **finito** |
| Runas | teia fixa de 32 nós | **finita** (todas despertas, acaba o crescimento) |

Nota: o fator por onda reinicia a cada fase; o que cresce sem teto é o produto com `hpStageStep` (+5% por fase) e `CycleHpGrowth` (×1,25 por volta). O que importa aqui: **o HP dos inimigos cresce multiplicativamente (por onda, por fase e por volta) e tudo que o jogador tem cresce linear ou é finito**. A corrida infinita termina; a pergunta de design é **como terminar bem**, não como evitar.

## 3. Estado atual do código (o que já existe e será reaproveitado)

| Item | Hoje | Consequência |
|---|---|---|
| Fim da corrida | `TdGameHost` chama `_observer.OnMatchEnded(Match, Match.Result)` **uma vez** (`_endReported`); `TdProfileRecorder` (App) é o observador que grava o perfil | Ponto único e já idempotente em memória para **creditar legado** (§6.4) |
| Cartão de fim | `TdUiController.BuildEnd` (`TdUiMode.End`, `_endCorner`, `EndCardOpen`): popover de 260 DIP no cantinho, **já não modal** (`ModalOpen` exclui `EndCardOpen`); Derrota mostra 1 linha de motivo + 1 dica (RF-28.24) e botões "Reiniciar" e "Mapa" | O cartão **evolui**, não nasce; mantém não-modal |
| Vazamentos | `TdMatchState.Leaks` (`TdLeakStat(EnemyId, IsBoss, Flying, Count, Damage)`, **fora do hash**), `DefeatAnalysis()`/`TdDefeat.Analyze` | Base do "o que mais vazou" e do Advisor |
| Patente e recorde | `TdRank` (limiares 0/5.000/12.000/22.000/29.000/34.000 sobre a soma dos recordes por fase), `BestScore` por fase no perfil | O cartão mostra a patente atual; **o recorde de onda é novo** (§6.3) |
| Runas | `AwakenRune` (comando) consome 1 fragmento e sorteia **1 nó da fronteira** com `_rng.NextUInt()`; `_awakened` (máscara de 32 bits) e `Fragments` **entram no hash** | Runas iniciais = despertar N nós **na criação**, mesma mecânica (§7) |
| Opções de partida | `TdMatchOptions(ContinuousWaves, PersistentRun, StageClearHeal)`; `Default` = corrida contínua, `Classic` = modo antigo; o hash só inclui as opções **fora do Classic**; `TdReplayScript` carrega `Options` | Campo novo de opção entra **sem mudar hash antigo** (§7.3) |
| Perfil TD | `TdProfileData` schema `sidewar.td-profile` **v3**; migrações encadeadas em `TdProfileMigrations` (v1→v2→v3); `InfiniteRuns` (onda máxima **por seed**, pós-MVP, não ligado) | Perfil **v4** com campos novos e migração v3→v4 (§6.2) |
| Configurações | `TdSettings` (`AutoStartEnabled`, `FadeWhenUnfocused`, `BandHeightDip`…) em `AppSettings.cs`/`SettingsSerializer.cs`; padrão de chave `td.*` | Opção `td.autoRestart` segue o padrão (§4.4) |
| Boss key / pausa | `BossHidden` e `ModalOpen` no `TdUiController`; relógio injetável `_now()`; host com `_app.SimulationRunning` | O contador do auto-recomeço respeita ambos (§4.3) |

## 4. Fim gracioso

### RF-32.1: Cartão de fim de corrida (Derrota)

O cartão **substitui o conteúdo da Derrota** em `BuildEnd` (a Vitória clássica e o fim de fase do modo clássico **não mudam**). Regras:

- **Não modal, sem foco, sem som obrigatório:** continua `TdUiMode.End` com `_endCorner`; nenhuma janela rouba foco (as janelas do SideWar são no-activate, TD.16); não bloqueia o clique da faixa nem o trabalho do usuário (SDD-28 RF-28.14, TD.16b).
- **Texto legível: ≥ 14 DIP em toda linha** (lição do feedback da TD.30b sobre fonte pequena das perguntas). Hoje os rótulos do cartão têm 14 a 16 DIP; a TD.32b **não pode reduzir** nenhum abaixo de 14 para caber (se não couber, remove linha, §RF-32.1 ordem de corte).
- **Conteúdo, nesta ordem** (cada item é uma linha de ≥ 14 DIP; textos ASCII como o cartão atual, a fonte do HUD não tem acento):
  1. **Título:** "CORRIDA ENCERRADA".
  2. **Onda alcançada:** "Onda 37 (fase 3)" (onda **absoluta** da corrida, §6.1, e a fase corrente); ao lado "NOVO RECORDE" (cor de destaque) ou "Recorde 52".
  3. **Patente:** a patente atual e a barra de progresso existentes (`AddRankBar`, RF-28.21); sem texto novo.
  4. **Legado:** "+2 legado" (RF-32.7) e, se a corrida seguinte ganhar runa nova, "Proxima corrida: 1 runa".
  5. **O que mais vazou:** até **3 tipos**, por **contagem** (`TdLeakStat.Count`), ex.: "Vazou: Runner x7, Zero-day x1". Sem vazamento registrado: a linha some (a derrota pode ser por dano de chefe ou caixa, RF-28.24; o motivo existente de `DefeatTexts` continua como linha única quando houver).
  6. **Azure Advisor:** 1 linha, §5.
  7. **Contador de recomeço** (só com a opção ligada, RF-32.3): "Recomecando em 6 s".
  8. **Botões:** **"Recomecar"** (padrão) e **"Ver runas"** (RF-32.4). O botão "Mapa" atual sai do cartão de Derrota da corrida contínua (continua no menu do HUD).
- **Ordem de corte** se a altura do cartão exceder o espaço do cantinho (a faixa mínima é 32 DIP, o cartão fica acima dela como o popover atual): corta primeiro a linha 3 (barra de patente vira só o nome), depois a 5 (vazamento), nunca a 1, 2, 4, 6 e 8. O teste de layout (T-32.9) fixa isso.
- **Tamanho:** largura 260 DIP como hoje (`Stack(260f)`); altura calculada pelo `Stack`, sem rolagem.

**Critérios:** CA-32.1, CA-32.9.

### RF-32.2: O que mais vazou (por tipo de inimigo)

- Fonte: `TdMatchState.Leaks` (já existe, **fora do `StateHash`**): lista de `TdLeakStat` só dos tipos que vazaram, em ordem do conteúdo.
- Ordenação para exibição: **maior `Count` primeiro**; empate por **maior `Damage`**, depois por **ordem do conteúdo** (determinística, sem `Rng`). Mostra até 3 tipos; chefes entram com o mesmo formato (o chefe que passou também é motivo da linha existente de `DefeatTexts`).
- Nome do inimigo vem do conteúdo (`Name`), com o `Id` como reserva. Nada de texto novo longo.
- **Sem custo de simulação:** só lê contadores já mantidos; **não** toca no hash nem no passo.

**Critérios:** CA-32.2.

### RF-32.3: Recomeço automático (opção, desligada por padrão)

- **Configuração:** `td.autoRestart` (booleano, **padrão `false`**) em Configurações, rótulo **"Recomecar automaticamente"** (junto de "Auto-início"). Chave ausente no JSON (saves antigos) = `false`.
- **Contagem:** **8 s** (`td.autoRestartSeconds` fixo em constante `TdSettings.AutoRestartSeconds = 8`; só vira setting se a DP-32.7 decidir). Linha discreta no cartão (≥ 14 DIP): "Recomecando em N s", **sem som, sem animação chamativa**.
- **Cancelável:** o botão "Recomecar" age na hora; **"Ver runas" cancela** a contagem (o jogador está olhando), assim como qualquer interação com o cartão que não seja "Recomecar"; também cancela abrir Configurações/Menu. Cancelada, **não reinicia sozinha** até a próxima corrida terminar (o cartão fica até o jogador agir). Não há botão "Cancelar" separado (menos botões, DP-32.7).
- **Relógio injetável:** usa o `_now()` do `TdUiController` (nada de `DateTime.Now` direto), como a pergunta-bônus (TD.30b).
- **Respeita boss key e pausa:** a contagem **só avança com o jogo "rodando para o usuário"**: `!BossHidden`, **sem modal aberto** (`!ModalOpen`) e a janela do jogo visível. Com a boss key (Ctrl+Alt+H) ativa, a contagem **congela** (não reinicia nem dispara escondido); ao mostrar de novo, **continua de onde parou**. Pausa do usuário também congela.
- **Ao zerar:** equivale ao botão "Recomecar" (mesmo caminho, `TdGameHost.Restart(next)`), com as runas de legado da corrida seguinte (§7). **Não** rouba foco: nenhuma janela é ativada.
- **Só em Derrota da corrida contínua** (`Options.PersistentRun`); o modo clássico e a Vitória não têm auto-recomeço.

**Critérios:** CA-32.3, CA-32.4.

### RF-32.4: Botões "Recomecar" e "Ver runas"

- **"Recomecar":** inicia nova corrida (`TdGameHost.Restart`) na **fase 1** (corrida contínua recomeça do início, como o "Reiniciar" atual), com `Options.Default` + `LegacyRunes` do perfil (§7). É o botão padrão (destacado).
- **"Ver runas":** abre a **aba Runas** do painel (TD.29b) em modo **somente leitura** da corrida encerrada (a teia como terminou; `AwakenRune` já é rejeitado fora de `Running`, então não há nada a gastar) e mostra o **contador de legado** (RF-32.8). Fechar a aba volta ao cartão. Abrir a aba **cancela** o auto-recomeço.
- Nenhum dos dois é modal além do que o painel de runas já é hoje (TD.29b); o cartão continua no cantinho.

**Critérios:** CA-32.5.

## 5. Azure Advisor (dica de contra-ataque)

### RF-32.5: Dica "Azure Advisor"

- **Texto:** uma linha de ≥ 14 DIP, formato `Advisor: {Torre} contra {traco/inimigo}.`, ex.: "Advisor: Gelo contra rapidos." Flavor Azure só no rótulo "Advisor" (RF-28.17: nada de jogo de ensino).
- **Regra (função pura no Core, `TdAdvisor.Suggest`, sem `Rng`, sem hash):**
  1. Somar, por **traço** de inimigo, o `Damage` dos `Leaks` (traços do `TdEnemyDef.Traits`; chefe conta como o traço `boss` **e** pelos seus outros traços). Empate: ordem fixa do validador (`TdContentValidator.Traits`).
  2. Pegar o **traço que mais vazou**.
  3. Candidatas = torres cujo `strong` (ou `counters` do inimigo, que `TdDefeat` já usa) **contém** esse traço **e** que estão **liberadas** no perfil (`unlockStage`, SDD-31 DP-31.12). Escolher a de **menor custo base**; empate por menor `Id`.
  4. **Reserva:** se não houver candidata liberada **ou o conteúdo ainda não tiver torre para o traço** (as torres do SDD-31 ainda não existem), sugerir **entre as 5 atuais** de `td-towers.json` as que contra-atacam o traço (os `counters` do inimigo que mais vazou, como `TdDefeat` faz hoje); se nenhuma servir, **não mostrar dica** (a linha some; melhor sem dica do que dica errada).
- **Ligação com o SDD-31 sem depender dele:** o Advisor lê **só o conteúdo carregado** (`Towers`, `strong`, `unlockStage`). Enquanto `td-towers.json` tiver 5 torres, só sugere essas. Quando a TD.31a a 31c adicionarem torres com `strong` no traço e `unlockStage` cumprido, o Advisor **passa a sugeri-las sem mudança de código** (a ideia 6 do SDD-31 §13 é **absorvida** por esta ficha; a TD.31 não precisa implementá-la). Nenhum id de torre Azure fica hard-coded na TD.32.
- **O Advisor não reabre `DefeatTexts`:** o motivo principal (RF-28.24) continua sendo a linha de motivo; o Advisor é uma linha **a mais**, focada em "o que construir".

**Critérios:** CA-32.6.

## 6. Legado: dados, fórmula e persistência

### 6.1 Onda alcançada (definição)

**Onda alcançada** `W` = número **absoluto** de ondas **completadas** na corrida: soma das ondas das fases já concluídas (`td-stages.json` `waves` de cada fase, repetindo o ciclo de 6 fases nas voltas) **mais** `WaveNumber − 1` da fase em curso (a onda em que o núcleo caiu **não conta como completada**). Calculada no Core por propriedade derivada `TdMatchState.WavesReached` (lê `StagesCleared`, `Cycle`, `WaveNumber` e o conteúdo; **fora do hash**, sem estado novo). Para fases 1 a 6 numa volta: 10, 22, 34, 48, 62, 77.

### RF-32.6: Fórmula dos fragmentos de legado

Proposta: **`L = min(6, floor(W / 5))`** (ex.: onda 12 → 2; onda 30 ou mais → 6, o teto).

Justificativa:
- **Degrau suave no começo:** a maior parte das corridas de quem está aprendendo termina entre as ondas 5 e 25; é onde o legado ajuda e é "sentido".
- **Teto baixo (6) por corrida → sem grind:** a partir da onda 30 não há ganho extra, então **jogar por horas não rende legado a mais** (o infinito rende **recorde e patente**, não poder). Isso também limita o risco de inflação de runas (§10).
- **Sem renda para quem morre cedo demais:** `W < 5` rende 0 (não premia morrer de propósito para "farmar" legado: com teto de 6 e mínimo de 5 ondas, o melhor retorno por tempo é jogar a corrida inteira).
- **Simples de explicar e de testar** (inteiros puros, sem `Math.Pow`, ADR-026).

Os parâmetros (`divisor` 5, `teto` 6) ficam em constantes `TdLegacy` (ou campo de JSON, DP-32.4), com validação no teste: `L(W)` monotônica não decrescente e `0 ≤ L ≤ 6`.

### RF-32.7: Runas iniciais da corrida seguinte

Proposta: **`N = min(3, floor(legadoAcumulado / 6))`** runas já despertas ao **criar** a corrida (acumulado vitalício, **não consumido**).

- Com corridas típicas de ~15 ondas (3 de legado cada): 2 corridas → N=1; 4 → N=2; 6 → N=3 (acumulado 18). Depois disso o legado "enche" (contador mostra "MAX").
- **Modelo mais simples escolhido** (derivado, não gasto): sem tela de gasto, sem estado de "saldo", sem decisão do jogador. A alternativa "gastar" fica em DP-32.1.
- **Efeito pequeno por construção:** 3 de 32 nós, cada nó vale +4% alcance, +5% Raio/Canhão, +3 créditos por onda, +0,3 s de Gelo, +1 de vida máxima, −4% recarga ou +0,5 s de Firewall (`td-runes.json`). O ganho é **menor que os primeiros 3 fragmentos de uma corrida** (1 fragmento por onda, `fragments.perWave`): **acelera as 3 primeiras ondas**, não a corrida.
- **Quais runas:** sorteadas **na criação** pelo `Rng` da corrida, entre a **fronteira** (mesma regra de `DoAwakenRune`: anel 0 ou vizinhas de despertas), **sem custo de fragmento** (§7.1).

### 6.2 Onde persiste

**`src/SideWar.Persistence/Profile/TdProfileData.cs` (perfil TD existente), schema v3 → v4** (migração `V3ToV4` em `TdProfileMigrations`, encadeada como as anteriores, SDD-14 RF-14.10). Campos novos, todos com padrão seguro:

| Campo (JSON) | Tipo | Padrão | Uso |
|---|---|---|---|
| `legacyTotal` | int | 0 | legado acumulado vitalício (saturado em 99 999 para não estourar) |
| `bestRunWaves` | int | 0 | recorde pessoal de onda alcançada (`W`) |
| `legacyLastRun` | string | `""` | id da última corrida creditada (idempotência, §6.4; forma final na DP-32.2) |
| `runsPlayed` | int | 0 | corridas encerradas e creditadas (estatística; opcional, DP-32.3) |

- **Compatibilidade com saves antigos:** `V3ToV4` apenas faz `root["legacyTotal"] ??= 0` etc. (campos ausentes = padrão); perfil v3 abre sem perda, e o primeiro salvamento grava v4. **Compatibilidade para trás** (v4 aberto por build antigo) **não é garantida**: o armazenamento versionado do SDD-14 já rejeita schema maior que o conhecido; o risco é registrado (§10) e a DP-32.6 trata de manter o v3 legível.
- **Sem mexer em selos, estrelas, árvore nem `InfiniteRuns`** (que é por seed e segue pós-MVP, TD.11). O legado é um eixo **separado** dos selos (RF-28.13).
- **Configuração** (`td.autoRestart`) fica em `AppSettings`/`SettingsStore` (preferência do aparelho, como `td.tutorialDone`), **não** no perfil.
- Leitura/escrita pelo `TdProfileStore` existente; falha de gravação segue a política atual de `TdProfileRecorder` (`SaveFailures`, a partida continua).

### 6.3 Recorde pessoal

`bestRunWaves = max(bestRunWaves, W)` ao creditar. O cartão mostra "NOVO RECORDE" quando `W > bestRunWaves` **anterior** (ler o valor antes de atualizar) e "Recorde N" caso contrário. O recorde **não** substitui a pontuação por fase (RF-28.21): são dois eixos (fase vencida × corrida longa).

### 6.4 Quando é creditado: uma vez, idempotente

- **Quando:** ao **fim da corrida**, no `TdProfileRecorder.OnMatchEnded(match, result)` (já chamado **uma vez** por partida pelo host via `_endReported`), **só quando** `match.Phase == Derrota` e `match.Options.PersistentRun`. Vitória clássica e modo clássico **não** creditam legado.
- **Idempotência:** o crédito usa um **id de corrida** (recomendação: `"{SeedState:x}-{SeedStream:x}"` da partida; forma final na DP-32.2) comparado a `legacyLastRun`; se igual, **não credita de novo** (reabrir o app, reentrada do evento, teste que chama duas vezes). Ordem: calcular `L`, somar a `legacyTotal` (saturando), atualizar `bestRunWaves`, gravar `legacyLastRun`, **gravar o perfil uma vez**.
- **Fecho do app no meio da corrida:** recomendação (DP-32.5): **também creditar ao sair** (`Shutdown` do host) pelo mesmo caminho idempotente, porque na visão "jogar enquanto trabalha" a corrida pode ser longa e fechada pelo usuário no fim do expediente; sem isso, o jogador perderia o legado de uma corrida de horas.
- **O Core não persiste nada:** só expõe `WavesReached`, `Leaks` e a fórmula pura `TdLegacy.Fragments(W)`; quem grava é o App (separação Core/Persistence mantida).

## 7. Legado na simulação: determinismo, hash e log

### 7.1 Como entra na partida

**Recomendação (DP-32.1/DP-32.8):** acrescentar ao fim de `TdMatchOptions` um parâmetro com padrão: `int LegacyRunes = 0`. No **`TdMatchState.Create`** (depois de montar o estado e **antes do primeiro tick**), se `LegacyRunes > 0` e `!IsClassic`, executa `N` vezes o mesmo sorteio do `DoAwakenRune` **sem consumir fragmento**: a fronteira, `_rng.NextUInt() % count`, `_awakened |= bit`, `RebuildRuneEffects()`. Sem evento `RuneAwakened` (a UI lê `IsAwakened`); `Fragments` fica em 0.

- **Determinismo (ADR-026):** a escolha vem do `Rng` semeado pela corrida; a ordem de consumo é **fixa** (N sorteios no `Create`, depois `BeginWave` e `AwakenRune` como hoje). Mesma seed + mesmo `LegacyRunes` = mesmas runas, nos dois SOs.
- **Quem lê o legado:** o **App/host** (`TdProfileRecorder` + `TdComposition`) calcula `N = TdLegacy.StartingRunes(legacyTotal)` e **passa em `TdMatchOptions`**. O Core **não** lê perfil (separação mantida).

### 7.2 `StateHash`

- `_awakened` e `Fragments` **já estão no hash**; as runas iniciais mudam `_awakened` e o estado do `Rng`, logo **o hash cobre o efeito**.
- Acrescentar a `Hash.cs`, **só quando `LegacyRunes > 0`**: `h.AddInt32(_options.LegacyRunes)` dentro do bloco das opções (o bloco já só existe fora do Classic). Com `LegacyRunes = 0` o hash é **idêntico** ao atual: **goldens e replays TD existentes não mudam**; só corridas com legado têm hash novo.
- `WavesReached`, `Leaks`, recorde, legado acumulado e o Advisor **ficam fora do hash** (só leitura/contadores, como `Leaks` e `CrateTowersLost`).

### 7.3 Replay e log

- `TdReplayScript` já carrega `TdMatchOptions? Options`; `LegacyRunes` viaja nele. Logs antigos (campo ausente) = `0` (a TD.32a verifica o serializador de replay/log e acrescenta o campo **com padrão 0**, sem quebrar arquivos existentes).
- O replay reproduz a criação **idêntica** (mesma seed, mesmas opções) e portanto as mesmas runas iniciais; **não há comando novo no log** (o despertar inicial é consequência das opções, como `ContinuousWaves`).
- **Opções de partida são parte do input do replay** (ADR-026): documentado em CA-32.8.

### 7.4 O que o legado não faz

- **Não é pay-to-win:** não há compra, moeda real, loja nem atalho; só tempo jogado, com **teto** (6 por corrida, 3 runas iniciais, 18 de acumulado útil).
- **Não pula fases:** a corrida sempre começa na fase 1, onda 1; nenhum fragmento vira estrela, selo, torre liberada, crédito, integridade extra além do efeito de runa já existente, nem avanço de `unlockStage`.
- **Não muda o conteúdo:** inimigos, ondas, custos e `ContentHash` iguais.
- **Não substitui selos/estrelas/patente** (RF-28.13, RF-28.21): é um eixo à parte.
- **Não afeta o modo clássico** nem a pontuação de fase (a pontuação lê integridade/tempo/créditos; runa de integridade já é efeito existente, ver risco §10).

## 8. UI do legado

### RF-32.8: Onde o jogador vê o legado

- **No cartão de fim:** a linha "**+2 legado**" (RF-32.1, item 4), cor de destaque discreta; se subir de degrau de runa inicial, "Proxima corrida: 1 runa".
- **Na aba Runas (TD.29b):** um **contador discreto** (≥ 14 DIP) "Legado 11 · proxima: +1 runa em 1" ou "Legado MAX" (lê `legacyTotal` do perfil via o host). Sem modal, sem animação. Visível também durante a corrida (informa por que há runas despertas no início), mas **sem ocupar** o HUD compacto.
- **Na criação da corrida:** nenhum aviso intrusivo; a teia já aparece com as runas despertas. Opcional (DP-32.7): aviso de cantinho de 4 s "Legado: 2 runas despertas" no padrão `NoticeOpen`.
- **Tooltips** em todo item (padrão TD.14).

## 9. Requisitos testáveis (resumo RF-32.x → critérios)

| RF | Resumo | Critério |
|---|---|---|
| RF-32.1 | Cartão de Derrota discreto, não modal, ≥ 14 DIP, conteúdo e ordem fixos | CA-32.1, CA-32.9 |
| RF-32.2 | "O que mais vazou": até 3 tipos por contagem, de `Leaks` | CA-32.2 |
| RF-32.3 | `td.autoRestart` (padrão desligado), 8 s, cancelável, respeita boss key/pausa/modal, relógio injetável | CA-32.3, CA-32.4 |
| RF-32.4 | Botões "Recomecar" e "Ver runas" | CA-32.5 |
| RF-32.5 | Azure Advisor: traço que mais vazou, torre liberada, reserva nas 5 atuais | CA-32.6 |
| RF-32.6 | `L = min(6, floor(W/5))` | CA-32.7 |
| RF-32.7 | `N = min(3, floor(acumulado/6))` runas iniciais, sorteadas na criação, sem custo | CA-32.8 |
| RF-32.8 | Crédito uma vez e idempotente; persistência v4 com migração; UI do legado | CA-32.10, CA-32.11, CA-32.12 |

## 10. Riscos

| Risco | Efeito | Mitigação |
|---|---|---|
| **Grind** | jogador morre de propósito ou joga horas só por legado | teto 6 por corrida; mínimo 5 ondas; 3 runas iniciais no máximo (18 acumulado); legado "MAX" |
| **Inflação de runas** | as 32 runas ficam triviais; `waveCredits` e `rangeBp` somados ao SDD-31 (CDN 260, Cofre) | só 3 runas iniciais (≈ 9% da teia), todas sorteadas na fronteira; monitorar com bot (§12); o teto vive em constante ajustável |
| **Determinismo** | runas iniciais divergem entre SOs ou invalidam replays | sorteio pelo `Rng` da corrida na criação, ordem fixa; `LegacyRunes` entra no hash **só quando > 0**; replay carrega a opção; testes T-32.3 a T-32.5 |
| **Idempotência** | crédito duplo ao reabrir o app ou reentrar no evento | id de corrida + `legacyLastRun`; teste explícito (T-32.7) |
| **Persistência v4** | build antigo rejeita perfil novo; perda de dado na migração | migração só **acrescenta** campos; campos ausentes = padrão; backup do `VersionedDocumentStore`; DP-32.6 |
| **Auto-recomeço rouba atenção** | corrida recomeça sem o usuário perceber, ou reinicia escondida pela boss key | desligado por padrão; congela com boss key/pausa/modal; sem foco/som |
| **Cartão fica grande** | cobre a barra no cantinho | ordem de corte (RF-32.1), largura fixa 260 DIP, teste de layout |
| **Conflito com outras fichas** | `TdUiController`, `TdUiRenderer`, `TdMatchState*` são mexidos por TD.30b/30c e TD.31 | ordem estrita (§11.3), uma ficha por vez |
| **Vazamento mal atribuído no Advisor** | dica errada irrita | regra por traço, só torre liberada, sem dica quando não há candidata |
| **Corrida muito curta pós-legado** | 3 runas não bastam; ou fáceis demais | números em constantes ajustáveis; medir com o bot (§12) |

## 11. Divisão em fichas

### 11.1 TD.32a (Core + Persistence): legado e opções de partida

Entrega, sem UI nova:
- `TdLegacy` (Core): `Fragments(W)`, `StartingRunes(total)`, constantes (divisor 5, teto 6, `k` 6, máx. 3).
- `TdMatchOptions.LegacyRunes` (padrão 0), aplicação no `Create` (§7.1), `WavesReached`, `Hash.cs` (campo condicional), `TdAdvisor.Suggest` (função pura, RF-32.5).
- `TdProfileData` v4 + migração `V3ToV4`, `TdProfileSerializer`, `TdProfileStore`; `TdProfileRecorder.OnMatchEnded` credita legado (idempotente, RF-32.8) e expõe `LegacyTotal`/`BestRunWaves`/`StartingRunes`; `TdComposition` passa `LegacyRunes` ao criar a corrida; serializador de replay com o campo opcional.
- **Arquivos:**
  - `src/SideWar.Core/Td/TdTypes.cs` (`TdMatchOptions`), `TdMatchState.cs` (aplicação no `Create`), `TdMatchState.Runes.cs` (reuso do sorteio), `TdMatchState.Hash.cs`, `TdMatchState.Result.cs` ou `TdMatchState.Bonus.cs` (`WavesReached`; `Bonus.cs` tem alteração local de outra ficha, evitar se possível), novo `src/SideWar.Core/Td/TdLegacy.cs`, novo `src/SideWar.Core/Td/TdAdvisor.cs`, `src/SideWar.Core/Td/TdReplay.cs` (campo de opções, se serializado).
  - `src/SideWar.Persistence/Profile/TdProfileData.cs`, `TdProfileMigrations.cs`, `TdProfileSerializer.cs`, `TdProfileStore.cs`, `TdProfileRules.cs` (se tocar).
  - `src/SideWar.App/Composition/TdProfileRecorder.cs`, `TdComposition.cs`.
  - Testes: `tests/SideWar.Core.Tests/TdLegacyTests.cs` (novo; T-32.1 a T-32.6), `TdAdvisorTests.cs` (novo; T-32.8), `tests/SideWar.Persistence.Tests` (T-32.7, migração v3→v4) e atualização de goldens **só se** algum hash mudar (com `LegacyRunes = 0` nenhum muda).
- **Ambiente:** 🟢 WSL puro.

### 11.2 TD.32b (UI): cartão de fim, auto-recomeço e textos

- Cartão de Derrota reescrito (`BuildEnd`, RF-32.1), "o que mais vazou" (RF-32.2), linha do Advisor, linha "+N legado", contador de recomeço (RF-32.3), botões "Recomecar"/"Ver runas" (RF-32.4), contador de legado na aba Runas (RF-32.8), opção em Configurações "Recomecar automaticamente".
- **Arquivos:**
  - `src/SideWar.Runtime/Td/TdUiController.cs` (`BuildEnd`, contagem com `_now()`, boss key/modal), `TdUiController.Quiz.cs` só se o relógio compartilhar código (evitar), `TdUiModel.cs` (ação `AutoRestartCancel`/`ViewRunes`, se faltar), `Strings.cs` (textos).
  - `src/SideWar.Runtime/Td/TdGameHost.cs` (reinício com `LegacyRunes`; saída com crédito, DP-32.5).
  - `src/SideWar.Rendering/Scene/TdUiRenderer.cs` (linhas do cartão, contador, aba Runas).
  - `src/SideWar.App/Composition/TdUiPresenter.cs` (liga botões/opção), `src/SideWar.Persistence/Settings/AppSettings.cs` e `SettingsSerializer.cs` (`td.autoRestart`).
  - Testes: `tests/SideWar.Runtime.Tests/Td/TdUiControllerTests.cs` (T-32.9 a T-32.14, relógio injetável), `tests/SideWar.Rendering.Tests/Golden/` (cartão de fim, DPI 100/150/200%; testes interativos só com autorização, memória `sem-testes-interativos-sem-aviso`).
- **Ambiente:** 🟡 WSL + Windows (render/golden); testes de UI no WSL.

### 11.3 Ordem de execução

**TD.30a → TD.30b → TD.30c → TD.32a → TD.32b → TD.31a → … → TD.31e**, uma por vez: TD.30b/30c, TD.32 e TD.31 mexem nos **mesmos arquivos** (`TdMatchState*.cs`, `TdTypes.cs`, `TdUiController.cs`, `TdUiRenderer.cs`, `TdUiModel.cs`, goldens TD), então **sem paralelismo**. Cada ficha só abre depois de a anterior estar commitada com testes verdes. **O SDD-31 não é editado aqui**: o `PLANO.md` registra a nova ordem na linha da TD.32; a menção "TD.30c → TD.31a" em SDD-31 §12.4 passa a ser lida como "TD.30c → TD.32a → TD.32b → TD.31a" (ajuste textual no SDD-31 fica para a ficha de documentação TD.12 ou para o Tech Lead). O Advisor (TD.32a) é a ideia 6 do SDD-31 §13, que **fica atendida** sem ficha própria.

## 12. Critérios de aceitação

| CA | Critério |
|---|---|
| **CA-32.1** | Em Derrota da corrida contínua, `EndCardOpen` é verdadeiro e `ModalOpen` **falso**; o cartão mostra, nesta ordem, título, onda alcançada e recorde, patente, legado, vazamentos, Advisor, botões; nenhuma janela é ativada; toda linha ≥ 14 DIP. |
| **CA-32.2** | Com `Leaks` conhecidos (ex.: Runner×7, Zero-day×1, Grunt×3), o cartão lista **Runner x7, Grunt x3, Zero-day x1** (por contagem, desempate por dano e ordem do conteúdo); sem vazamentos a linha não aparece. |
| **CA-32.3** | Com `td.autoRestart` desligado (padrão, inclusive em `settings.json` antigo sem a chave) **nada reinicia sozinho**; ligado, a contagem de 8 s reinicia ao zerar. |
| **CA-32.4** | Com relógio injetável: a contagem **congela** com `BossHidden`, com modal aberto e com pausa, e continua do mesmo ponto; "Ver runas" e abrir Configurações a **cancelam**; "Recomecar" age na hora; nunca usa `DateTime.Now` direto. |
| **CA-32.5** | "Ver runas" abre a aba Runas somente leitura com o contador de legado; "Recomecar" cria nova corrida na fase 1 com as `LegacyRunes` do perfil. |
| **CA-32.6** | Advisor: com a maior parte do dano vazado em `fast`, sugere uma torre **liberada** cujo `strong` contém `fast` (menor custo); sem candidata liberada, sugere entre as 5 atuais; sem nenhuma, **sem linha**; com conteúdo de 5 torres nunca sugere id inexistente; com torre Azure carregada e liberada, passa a poder sugeri-la **sem mudar código**. |
| **CA-32.7** | `L(W)`: `W = 0..4 → 0`; `5..9 → 1`; `12 → 2`; `29 → 5`; `30, 77, 10 000 → 6`; monotônica e limitada a 6. |
| **CA-32.8** | `LegacyRunes = N` desperta exatamente N nós distintos na **fronteira**, sem gastar fragmento; mesma seed e mesmas opções dão as mesmas runas e o **mesmo `StateHash`** em 5 execuções (Linux/Windows); `LegacyRunes = 0` dá hash **idêntico** ao atual (goldens TD intactos); replay com `Options.LegacyRunes` reproduz o hash. |
| **CA-32.9** | Layout: nenhuma linha abaixo de 14 DIP; ao faltar espaço, as linhas são cortadas na ordem do RF-32.1; goldens do cartão em DPI 100/150/200%. |
| **CA-32.10** | Crédito **uma vez**: chamar `OnMatchEnded` duas vezes (ou reabrir o app) com a mesma corrida soma o legado **uma única vez**; o recorde só sobe. |
| **CA-32.11** | Perfil v3 abre sem perda e vira v4 com `legacyTotal` 0, `bestRunWaves` 0; selos, estrelas, árvore e `InfiniteRuns` intactos; arquivo v4 relido preserva os campos; schema desconhecido/corrompido segue a política do `TdProfileStore` (rejeita sem apagar). |
| **CA-32.12** | O cartão mostra "+N legado"; a aba Runas mostra o contador ("Legado 11 · ..." ou "MAX"); modo clássico e Vitória **não** creditam. |
| **CA-32.13** | Nenhum efeito sobre fases/conteúdo: `ContentHash` igual; corrida com `LegacyRunes = 3` não pula fase, não concede estrela, selo, torre liberada nem crédito (exceto o efeito das próprias runas). |

## 13. Decisões (decididas conforme as recomendações, TD.32a)

As dez recomendações abaixo estão decididas. A TD.32a usa o estado inicial e incremento do PCG como identificador derivado da seed, pois a partida recebe o gerador já inicializado; reutilizar uma seed repete o identificador. Crédito no encerramento do aplicativo e extras de interface pertencem à TD.32b.

| DP | Pergunta | Opções | Decisão |
|---|---|---|---|
| **DP-32.1** | **Modelo do legado:** derivado do acumulado (não gasto) ou saldo que o jogador gasta? | (a) derivado `N = min(3, floor(total/6))`, não consumido; (b) saldo gasto em "runas iniciais" a escolher; (c) só pelo **recorde** (`N = min(3, floor(melhorOnda/15))`), sem acumular nem idempotência | **(a)**: sem tela nem escolha, coerente com "discreto". (c) é a alternativa mais simples se o usuário abrir mão dos "fragmentos de legado". |
| **DP-32.2** | **Id de idempotência da corrida** | (a) seed `"{SeedState:x}-{SeedStream:x}"`; (b) contador `runsStarted` gravado ao **iniciar** a corrida; (c) data/hora de início | **(a)**: não exige gravar ao iniciar; (b) é mais robusto a seed repetida (testes/seed fixa) ao custo de uma gravação extra por corrida. |
| **DP-32.3** | Guardar `runsPlayed` e histórico de corridas (lista das últimas N)? | (a) só contadores; (b) lista das últimas 10 corridas (onda, data) | **(a)** agora; histórico só se pedirem (cresce o perfil). |
| **DP-32.4** | Onde ficam os números (divisor 5, teto 6, k 6, máx. 3 runas) | (a) constantes em `TdLegacy`; (b) campo novo em `td-runes.json`/`td-stages.json` (muda `ContentHash` e **invalida todos os goldens TD**) | **(a)**: não muda `ContentHash`; migra para JSON só com a TD.31e/ajuste fino se o balanceamento exigir. |
| **DP-32.5** | Creditar legado também **ao sair do app** no meio da corrida? | (a) só no fim por Derrota; (b) também no `Shutdown`, mesmo caminho idempotente | **(b)**: corridas longas são fechadas pelo usuário no fim do expediente; (a) faria quem joga "infinito" nunca ganhar legado. |
| **DP-32.6** | Compatibilidade para trás do perfil v4 | (a) build antigo rejeita o perfil novo (padrão do SDD-14); (b) manter o v3 legível e gravar o legado em arquivo/campo separado | **(a)**: o produto é de um usuário só e a migração é aditiva; (b) só se houver rollback de build previsto. |
| **DP-32.7** | **Extras discretos:** contagem do auto-recomeço configurável (5 a 30 s) ou fixa 8 s; aviso de 4 s "Legado: N runas despertas" ao criar a corrida; dica "(toque p/ cancelar)" | cada um sim/não | contagem **fixa 8 s**; **sem** aviso ao criar (a teia mostra); **sem** dica extra de cancelar (o botão "Ver runas" cancela). |
| **DP-32.8** | **Como as runas iniciais entram no estado** (arquitetura) | (a) `TdMatchOptions.LegacyRunes` aplicado no `Create` pelo `Rng` (§7.1); (b) o host envia N comandos `AwakenRune` ao log com fragmentos concedidos antes do tick 0 (muda o log, gasta fragmentos, precisa de `Running`); (c) máscara de runas fixas (sem sorteio) passada nas opções | **(a)**: sem comando novo, determinístico, hash inalterado com N=0. (c) é viável se o usuário preferir "sempre as mesmas" (menos variedade, menos risco de `rangeBp`). |
| **DP-32.9** | **Auto-recomeço volta à fase 1 ou à fase em que caiu?** | (a) sempre fase 1 (corrida nova); (b) opção "recomeçar na fase mais alta concluída" | **(a)**: simples e sem atalho (alinha com "não pula fases", §7.4). |
| **DP-32.10** | Recorde de onda também no HUD/Menu fora do cartão? | (a) só no cartão e na aba Runas; (b) também no Menu | **(a)**: mantém o HUD limpo. |

## 14. Cenários de teste

**Core / Persistence (TD.32a)**
- **T-32.1** `TdLegacy.Fragments(W)`: tabela do CA-32.7 (inclui `W` muito grande, `W` negativo → 0).
- **T-32.2** `TdLegacy.StartingRunes(total)`: `0..5 → 0`, `6 → 1`, `12 → 2`, `18+ → 3`; monotônica.
- **T-32.3** `LegacyRunes = 3` desperta 3 nós **distintos** na fronteira, `Fragments == 0`, efeitos de runa refletidos (ex.: `integrity`).
- **T-32.4** Mesma seed + `LegacyRunes = 2`: 5 execuções, mesmas runas e mesmo `StateHash`; `LegacyRunes = 0` = hash anterior (golden atual).
- **T-32.5** Replay com `Options.LegacyRunes` reproduz o hash; log antigo (sem o campo) abre como 0.
- **T-32.6** `WavesReached` em Preparo inicial = 0; após 3 ondas = 3; ao concluir a fase 1 (10) e cair na onda 4 da fase 2 = 13; volta 2 (ciclo) soma corretamente.
- **T-32.7** Crédito idempotente: `OnMatchEnded` ×2 (e reabrir o perfil) soma uma vez; recorde só sobe; Vitória clássica não credita; migração v3→v4 preserva selos/estrelas/nós; perfil corrompido não é apagado.
- **T-32.8** `TdAdvisor`: traço dominante → torre liberada de menor custo; sem liberada → reserva nas 5; sem candidata → vazio; conteúdo em memória com torre extra de `strong` no traço passa a ser sugerida; nenhum id hard-coded; desempates fixos.

**UI (TD.32b)**
- **T-32.9** Layout do cartão: ordem das linhas, ≥ 14 DIP, ordem de corte, `ModalOpen` falso.
- **T-32.10** "O que mais vazou": ordenação por contagem/dano/ordem, máximo 3, linha oculta sem dados.
- **T-32.11** Auto-recomeço desligado/ligado: reinicia aos 8 s com relógio injetado; chave ausente = desligado.
- **T-32.12** Congela com boss key, modal e pausa; retoma do ponto; "Ver runas"/Configurações cancelam.
- **T-32.13** "Recomecar" cria a corrida com `LegacyRunes` do perfil; "Ver runas" é somente leitura e mostra o contador (inclui "MAX").
- **T-32.14** Golden do cartão (DPI 100/150/200%) e da aba Runas com contador.

## 15. Impacto no balanceamento dos bots

- O `TdSanityBot`/`TdBotPlans` criam a corrida com `TdMatchOptions.Default` (`LegacyRunes = 0`): **o comportamento e as medições atuais não mudam**; nenhum golden TD muda com N=0.
- Hipótese (**não medida**): `LegacyRunes = 3` equivale a ganhar cerca de **3 fragmentos de partida**, isto é, uma vantagem comparável às 3 primeiras ondas de runas; não deve mudar o fim da corrida (o HP cresce exponencialmente, §2), só empurrá-lo **algumas ondas**.
- **Medição pedida (TD.32a, evidência em `specs/evidencias/TD.32.md`):** rodar o bot (perfis Médio/Otimizado, 20 seeds) com `LegacyRunes` 0 e 3 e registrar a onda alcançada; se a diferença passar de ~15% da mediana, reduzir `k`/teto (constantes, DP-32.4). **Não** reativar nem alterar os `Skip` dos testes de bot (pertencem à TD.31e).
- Com o SDD-31 (15 torres), repetir a medição na TD.31e para checar que `rangeBp` (CDN) e `waveCredits` (Cofre) não explodem com runas iniciais (SDD-31 B-31.6).
- A pontuação por fase (`TdScoring`) e a patente (`TdRank`) **não mudam**; o recorde de onda é eixo separado.

## 16. Referências internas

SDD-28 (RF-28.14, RF-28.21, RF-28.24), SDD-29 (§20 corrida contínua, teia de runas TD.29a/b), SDD-31 (§13 ideia 6, DP-31.12, §12.4), SDD-14 (migração de perfil), ADR-026 (determinismo, layout e opções como entrada), `src/SideWar.Core/Content/td-stages.json` (`hpWaveGrowth`, `hpStageStep`, `fragments`, `bonusQuestion`), `td-runes.json` (`creditSources`, 8 tipos, 32 nós), `src/SideWar.Core/Td/` (`TdMatchState.Tick.cs` `ScaledHp`/`CycleHpGrowth`, `TdMatchState.Runes.cs`, `TdMatchState.Bonus.cs` `Leaks`/`TdDefeat`, `TdMatchState.Hash.cs`, `TdMatchState.Result.cs`, `TdRank.cs`, `TdTypes.cs` `TdMatchOptions`, `TdReplay.cs`), `src/SideWar.Persistence/Profile/` (`TdProfileData.cs`, `TdProfileMigrations.cs`), `src/SideWar.App/Composition/TdProfileRecorder.cs`, `src/SideWar.Runtime/Td/` (`TdGameHost.cs`, `TdUiController.cs` `BuildEnd`/`EndCardOpen`), memórias `visao-jogar-no-trabalho` e `sem-testes-interativos-sem-aviso`.
