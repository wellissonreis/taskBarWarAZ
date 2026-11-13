# SDD-14: Persistência (configurações, perfil e sessão)

**Status:** Rascunho para revisão · **Etapa:** E6 (o SettingsStore entra na E2) · **Depende de:** SDD-07, 11, 13 (apenas o `stageTopic` e o `Profile` ajustado), 21 (posição das torres e `SEM POSIÇÃO`, ADR-034; antes `slotIndex`, `SEM SLOT`), 22 (layout da faixa), 25 (presença, offline, pendências), 26 (ledger e eventos), 27 (economia, Reservas, regiões, missões) · **Bloqueia:** SDD-18

> **Atualizada pelas fichas do jogo idle (ADR-019, ADR-022, ADR-026).** O conteúdo de `session.json` e `profile.json` e os gatilhos de gravação de RF-14.5 a RF-14.8 foram ajustados abaixo; o mecanismo de arquivo (RF-14.1 a RF-14.3, RF-14.9 a RF-14.11) não muda.

## 1. Identificador e título

SDD-14: Arquivos, schemas versionados, escrita atômica, gatilhos de gravação, restauração, migração e corrupção.

> **Ajuste (ADR-022):** o `session.json` também é o **`OfflineSnapshot`** que o `OfflineCalculator` lê no retorno (SDD-25 RF-25.6); por isso `savedUtc` e os campos de SDD-25 RF-25.11 são obrigatórios.

## 2. Objetivo

Nunca perder progresso relevante e nunca corromper dados. O save de meio de onda restaura o estado **exato** (hash igual) e grava raramente (ANALISE L4, L10).

## 3. Contexto técnico

- ADR-007 (`System.Text.Json` com source-gen e escrita atômica). Gatilhos de ciclo de vida em SDD-07.
- `File.Replace` no NTFS é atômico do ponto de vista do leitor e cria o backup. Em volume sem suporte, a alternativa é `File.Move(tmp, dst, overwrite:true)`.

## 4. Escopo e exclusões

**Escopo:** pasta de dados, `settings.json`, `profile.json`, `session.json`, `session.crash.json`, `.bak`, `AtomicFileWriter`, schemas e DTOs, `Migrations`, política de gatilhos, debounce, validação no restore, modo portátil e logs (local e rotação).

**Exclusões:** nuvem e sincronização (fora do escopo, ADR-014) e o registro do Windows (apenas "iniciar com o Windows", SDD-07).

## 5. Requisitos funcionais

### RF-14.1: Pasta de dados

- O padrão é `%LOCALAPPDATA%\SideWar\` (`Environment.SpecialFolder.LocalApplicationData`).
- **Modo portátil:** se existir `portable.flag` ao lado do executável, usar `<dirExe>\data\`.
- Estrutura: `settings.json`, `profile.json`, `session.json` e os respectivos `*.bak`, `session.crash.json` (opcional), `corrupt\` (quarentena) e `logs\`.

### RF-14.2: `AtomicFileWriter.Write(path, ReadOnlySpan<byte> utf8)`

1. Escrever em `path + ".tmp"` com `FileStream(FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)` e `Flush(flushToDisk:true)`.
2. Se `path` existir: `File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors:true)`. Caso contrário, `File.Move(tmp, path)`.
3. Se o passo 2 falhar com `IOException` ou `PlatformNotSupportedException`: `File.Copy(path, path+".bak", true)` e depois `File.Move(tmp, path, true)`.
4. Até 2 novas tentativas, com 50 ms entre elas, em `IOException` de compartilhamento (antivírus). Depois disso, registrar um erro e retornar `false`. O jogo não para por falha de save; a HUD mostra o selo "⚠ falha ao salvar" por 5 s.

### RF-14.3: Envelope comum

```json
{ "schema": "sidewar.session", "schemaVersion": 1, "appVersion": "0.1.0", "savedAtUtc": "2026-11-10T12:00:00Z",
  "contentHash": "sha256-hex", "payload": { ... } }
```

- Os schemas são `sidewar.settings`, `sidewar.profile` e `sidewar.session`.
- Tamanho máximo de leitura: 1 MB. Acima disso, o arquivo é tratado como corrompido.

### RF-14.4: `settings.json` (payload)

> **Ajustado (ADR-020, ADR-028; SDD-04, SDD-05, SDD-07, SDD-15, SDD-21, SDD-22, SDD-25).** O JSON abaixo é o **original (histórico)**. Efeito das fichas novas: **removidos** `dock.size`, `dock.anchor`, `dock.marginDip` e `dock.edge` (a faixa tem 96 DIP, na largura do monitor da barra, SDD-22 RF-22.1; `dock.reserveSpace` mantém o AppBar pós-MVP, SDD-05 RF-05.8); `background` (a cena não tem fundo, SDD-04 RF-04.4 substituído); `opacity.passive`/`interactive` (presente = 255, ausente = 128, fixos, SDD-04 RF-04.1; `opacity.peek` só vale para a faixa, SDD-04 RF-04.5); `behavior.pauseWhenHidden` e `autoResume` (Hidden sempre pausa e o retorno é automático, SDD-25 RF-25.2); `behavior.pauseWhenIdle` e `idleMinutes` (substituídos pelo `PresenceTracker`, SDD-25 RF-25.1); `hotkeys.StartWave` (não há comando de onda); `gameplay.azureModule` (perguntas e picos são núcleo, ADR-024). **Acrescentados:** `taskbar.useUia` (padrão `true`, SDD-21 RF-21.7) e, em `performance`, os fps de cena **presente** (padrão 30) e **ausente** (padrão 8, SDD-22 RF-22.10); em `hotkeys`, `ToggleInteractive` passa a ser o modo interativo **da faixa**. `autoScaleOn` não é uma configuração: é estado do jogo (ligado por padrão, SDD-25 RF-25.5) e fica na sessão.

```json
{
  "dock": { "monitorDevice": "\\\\.\\DISPLAY1", "boundsHint": [0,0,1920,1080], "edge": "Bottom", "size": "M", "anchor": "Right", "marginDip": 0, "reserveSpace": false },
  "opacity": { "passive": 180, "interactive": 255, "peek": 60 },
  "background": { "color": "#0C1525", "alpha": 150 },
  "behavior": { "hideOnFullscreen": "SameMonitor", "pauseWhenHidden": true, "autoResume": false, "pauseWhenIdle": true,
                "idleMinutes": 5, "autoPassiveSeconds": 8, "peekOnHover": true, "startWithWindows": false },
  "performance": { "activeFps": 30, "saverOnBattery": true, "forceSaver": false },
  "accessibility": { "reducedMotion": false },
  "hotkeys": { "ToggleInteractive": "Ctrl+Alt+G", "ToggleVisibility": "Ctrl+Alt+H", "TogglePause": "Ctrl+Alt+P", "StartWave": "Ctrl+Alt+N" },
  "audio": { "enabled": false, "volume": 0.6 },
  "diagnostics": { "recordCommands": false, "logLevel": "Info" },
  "gameplay": { "azureModule": false }
}
```

> **Ajustado por ADR-031.** Configurações **editáveis** em `settings.json`: `performance.presentFps` ∈ {20, 30, 60} (padrão **30**; substitui `activeFps`); `performance.absentFps` ∈ {4, 8} (padrão **8**); `autoScale` (ligada por padrão); `behavior.hideOnFullscreen`; `behavior.startWithWindows`; `accessibility.reducedMotion`; `hotkeys`. **Não editáveis** (só exibidos, vêm do conteúdo, SDD-25): limiares de presença (5 min; faixas de 15 e 60 min), teto offline de 12 h e multiplicadores. `hotkeys.StartWave` e qualquer velocidade 2× não existem.

- Cada campo inválido volta ao **padrão daquele campo** (validação por campo). O arquivo inteiro não é descartado.
- A gravação tem debounce de 500 ms após a última mudança e é imediata no encerramento.

### RF-14.5: `profile.json` (payload)

> **Ajustado (SDD-27, SDD-26, SDD-25 RF-25.11).** O perfil passa a guardar o que **sobrevive ao Migrar** (SDD-27 RF-27.10): nível e XP; estrelas por fase e fases desbloqueadas; **Reservas**; **região** atual e regiões desbloqueadas; **torres desbloqueadas** (por exemplo, Scale Set ao entrar na fase 09); **missões do dia** (data local `yyyy-MM-dd`, estado `Em andamento | Concluída | Coletada` e progresso de cada uma); **ledger** de perguntas por `(fase, tentativa)` e **perguntas já sorteadas** por `(fase, tentativa)` (SDD-26 RF-26.5/26.7); **contadores de eventos** (respostas, picos escalados, caixas coletadas, abates, melhorias manuais, minutos presentes do dia); seed e estado do `Rng` persistidos (SDD-27 RF-27.9); `TotalKills` e `PlaySeconds`. Sem `ClassicRecord` nem `Concepts` (SDD-13 RF-13.4 ajustado). *A divisão exata entre `profile.json` e `session.json` segue esta ficha e a lista de SDD-25 RF-25.11; é uma alocação a confirmar na implementação (lacuna registrada no report).* O gatilho de gravação não é mais "vitória/derrota/abandono": ver RF-14.7.

`Profile` (SDD-13, RF-13.4), incluindo `Concepts`. É gravado na vitória, na derrota ou no abandono (atualização de recordes) e no encerramento, se houver mudança.

### RF-14.6: `session.json` (payload)

> **Ajustado (ADR-019, ADR-022, ADR-026, ADR-027; SDD-21 RF-21.8; SDD-25 RF-25.11).** O JSON abaixo é o **original (histórico)**. Mudanças: sem `mode`, `checkpoint`, `speed` e `userPaused` (o restore retoma sem pausa; ADR-031), `phase` Build/Victory/Defeat e `projectiles` (acerto instantâneo, ADR-027); sem `x`, `y`, `pathDist` e `lane` (mundo 1D, SDD-22 RF-22.2); créditos e XP são `long`, mais `carry` fracionário (SDD-27 RF-27.1), e HP e dano são `double` (SDD-27 RF-27.15). **Campos acrescentados:**
>
> - `savedUtc` (ISO 8601, UTC): instante do snapshot (SDD-25 RF-25.6);
> - `presence`: `{ state, lastInteractionUtc, presentSeconds, autoScaleAccumSeconds }` (SDD-25 RF-25.1/25.5);
> - `autoScaleOn` (padrão `true`);
> - `pending`: `{ credits, xp, melhorias[], quedas, ondaDe, ondaPara, tempoS, perdas }` e `cardState` (`Aberto | Recolhido`) do card "Bem-vindo de volta" (SDD-25 RF-25.9): as pendências **nunca se perdem** ao fechar o app;
> - `layout` (**ADR-026**): comprimento da faixa, posições dos slots, do núcleo e do portal, que fazem parte do estado e entram no `StateHash`; só mudam pelo comando `LayoutChanged`, aplicado entre ticks (SDD-22 RF-22.2, SDD-01 RF-01.6). Ao restaurar, o `layout` gravado é usado **como está**; se a barra atual difere, o `LayoutChanged` é emitido depois do restore, entre ticks;
> - **ADR-034 (vigente):** `towers[].anchor` (`"Start"|"End"`), `towers[].offsetDip` (inteiro ≥ 0) e `towers[].placed` (`false` = **`SEM POSIÇÃO`**: não atira, não é desenhada, não é vendida nem perdida, volta sozinha quando a posição volta a ser válida; SDD-21 RF-21.8, SDD-11 RF-11.3); o `layout` passa a ter `width`, `host` e `freeZones[]` no lugar de `slotX[]`; campo `migratedSlots` (DP-33.7, aceita). **Migração (DP-33.7, aceita):** `sidewar.session` sobe uma versão; a migração (RF-14.10) troca cada `slotIndex` por `anchor = "Start"`, `offsetDip = 0`, `placed = false`, troca `slotX[]` por `freeZones = []` e grava `migratedSlots = true`; após o restore, o primeiro `LayoutChanged` é seguido de `AutoPlaceUnpositioned()` (SDD-11 RF-11.10), entre ticks. Créditos, níveis, `Id` e o `n` do custo ficam intactos.
> - *(Histórico, ADR-020:)* `towers[].slotIndex` (0-based, no lugar de `x/y`) e o estado **`SEM SLOT`** (`slotIndex ≥ contagem de slots`): a torre **não atira, não é desenhada, não é vendida nem perdida**, e volta ao slot sem custo quando o índice reaparece (SDD-21 RF-21.8);
> - `sim`: fase/`StageRun` `(stageId, attempt, falls, ondaMax, chefesVencidos)`, onda atual e `ondaMax`, créditos, `carry`, XP, nível, HP do núcleo, `Reservas` e região vigentes, seed e estado do `Rng` (`rng`), `nextId`, estado da onda automática (fila, cursor, descanso de 1,4 s);
> - estado dos eventos (SDD-26): encontro de chefe em andamento (pergunta sorteada, tempo restante), banner de pico e `PeakQueue`, bônus ×3 ativo, caixa e agendamentos dos próximos sorteios.
>
> O `modules` opcional (ledger do módulo SDD-19) deixa de existir: o ledger agora é núcleo e vive no perfil (RF-14.5). Os efeitos visuais, os números flutuantes e os estados de hover **não** são gravados (ADR-013 refinada por ADR-027).

```json
{ "mode": "Classic|Stage", "stageId": 3, "checkpoint": { "xp": 120, "level": 4, "seed": 123456 },
  "sim": { "tick": 18234, "phase": "Wave", "wave": 17, "totalWaves": 60, "gold": 312, "xp": 40, "level": 5,
           "baseHp": 87, "maxBaseHp": 120, "nextId": 4411, "rng": [s0,s1],
           "waveState": { "schedule": [[t,kind],...], "cursor": 12, "clock": 8.4, "rest": 0.0, "spawnIndex": 12 },
           "towers": [ { "id": 5, "type": "Bolt", "x": 600, "y": 100, "level": 3, "range": 157, "damage": 32,
                         "cooldown": 0.12, "mode": "First", "hits": 2, "invested": 199, "targetId": 4402 } ],
           "enemies": [ { "id": 4402, "kind": "Tank", "pathDist": 812.5, "lane": -6, "hp": 210, "maxHp": 480,
                          "speed": 30.1, "spawnSpeed": 33.2, "armor": 0, "chilled": true, "lastHit": "Frost" } ],
           "projectiles": [ { "type": "Bolt", "x": 640, "y": 98, "speed": 392, "damage": 32, "targetId": 4402, "ownerId": 5 } ] },
  "modules": { },
  "stateHash": "fnv64-hex" }
```

- Floats são serializados com precisão "round-trip" (`"R"` / padrão do STJ para `float`).
- Ao restaurar, recalcula-se `X, Y, Dir` dos inimigos por `PathTable`, reconstroem-se os handles por `Id`, e o `StateHash` recalculado **tem de ser igual** ao gravado. Se não for, a sessão é descartada (RF-14.9). *(Mundo 1D: a posição X vem do `layout` gravado e da distância, sem `PathTable`; o hash é conferido **antes** do cálculo offline, que o altera, SDD-25 RF-25.10.)*
- `modules` é um objeto **opcional** (ledger do módulo de SDD-19), presente só quando o módulo está ativo. *(Obsoleto: SDD-19 foi substituída por ADR-024 / SDD-26; ledger e perguntas sorteadas vão para o perfil, RF-14.5.)*
- Efeitos visuais não são gravados (ADR-013).

### RF-14.7: Gatilhos de gravação da sessão

> **Ajustado (ADR-019, ADR-022; SDD-07 RF-07.3, SDD-25 RF-25.11).** Não há fases Build nem `Victory`/`Defeat`, logo as linhas "Ação em Build" e "`Victory`, `Defeat`, abandono" deixam de existir (a sessão **não** é apagada: o idle é contínuo). Gatilhos vigentes: **fim de onda** (`WaveCleared`, assíncrono); **ação do usuário** que muda o estado (construir, melhorar, `SetTargeting`, COLETAR, Migrar), com debounce de 2 s (assíncrono; no lugar de "ação em Build"); **pausa pelo usuário**; **periódico** a cada 60 s de `SimTime` em Presente ou Ausente; **snapshot ao ocultar** (`Hide`, qualquer `HiddenReason`): **síncrono e obrigatório** (SDD-07 RF-07.3 passo 4), pois grava o `OfflineSnapshot` com `savedUtc` para o cálculo analítico; e **suspensão, fim de sessão do Windows e encerramento**, síncronos. Um `LayoutChanged` não tem gatilho próprio: o novo `layout` segue no próximo save.

| Gatilho | Condição | Modo |
|---|---|---|
| `WaveCleared` (fim de onda) | sempre | assíncrono (T-IO) |
| Ação em Build (construir, melhorar, vender) | debounce de 2 s após a última | assíncrono |
| Pausa pelo usuário | se houve mudança | assíncrono |
| Hide (SDD-07) | se houve mudança | assíncrono |
| Periódico em Wave | a cada **60 s** de `SimTime` | assíncrono |
| Suspensão, fim de sessão do Windows, encerramento | sempre | **síncrono** (timeouts de SDD-07) |
| `Victory`, `Defeat`, abandono | apaga `session.json` (e `.bak`) após gravar o perfil | síncrono |

- "Mudança" vem de `Simulation.Version` (incrementada por comando e por tick) comparada com a versão do último save.
- Oculto/Offline **não há simulação nem saves periódicos** (SDD-25 RNF-25.3): o último snapshot é o do `Hide`.
- No modo assíncrono: serializar na T-Game para um `ArrayBufferWriter<byte>` reutilizado (via `Utf8JsonWriter`), copiar para um buffer de `ArrayPool` e entregar à T-IO. Se já houver uma gravação em andamento, **substituir** a pendente (fila de tamanho 1).

### RF-14.8: Início e restauração

> **Ajustado por ADR-031.** Na primeira execução (sem `session.json`) a partida começa **automaticamente**; `NoRun` é só transitório (SDD-10 RF-10.3).

1. Carregar `settings.json`, com `.bak` como fallback e padrões como último recurso.
2. Carregar `profile.json` (mesma estratégia).
3. Se `session.crash.json` for mais novo que `session.json`, mostrar a pergunta de recuperação (SDD-07, RF-07.12). Se recusada, apagar.
4. Se `session.json` for válido e `contentHash == ContentHash` atual, o jogo é restaurado e **retoma sozinho**, sem pausa e sem "Retomar partida" (o save não grava `userPaused`; ADR-031) (**ajustado**: SDD-25 RF-25.2/RF-25.10; antes, iniciava pausado com `PauseReason.User`). O tempo com o app fechado conta como **Offline**: o `OfflineCalculator` roda com `savedUtc` e o relógio atual (teto de 12 h) antes do primeiro frame e, com ≥ 10 min, cria o card "Bem-vindo de volta" (SDD-25 RF-25.9; D-11). As pendências (`pending`) e o `cardState` são restaurados.
5. Se `contentHash` divergir: tentar uma migração registrada para o par de hashes. Sem migração, descartar a sessão (mover para `corrupt\`), manter o perfil e mostrar o aviso "A partida salva era de outra versão e não pôde ser retomada" por 8 s (no widget/painel de SDD-23/SDD-24, já que a HUD antiga não existe).

### RF-14.9: Corrupção

- Um JSON inválido, schema desconhecido, `schemaVersion` maior que o suportado, tamanho acima do limite ou `stateHash` divergente faz o arquivo ser movido para `corrupt\<nome>-<yyyyMMddHHmmss>.json`. Em seguida, tenta-se o `.bak`, e por último os padrões.
- Manter no máximo 10 arquivos em `corrupt\` (apagar os mais antigos).

### RF-14.10: Migrações

- `Migrations` é uma lista ordenada de `(schema, fromVersion) → Func<JsonNode, JsonNode>`. Elas são aplicadas em sequência até a versão atual, usando `JsonNode` (DOM). A migração **só** acontece na carga e grava de volta na versão nova.
- Na v1, não há migrações. O mecanismo e um teste com uma migração fictícia (v0→v1) são obrigatórios.

### RF-14.11: Logs

- `logs\sidewar-<yyyyMMdd>.log`, em UTF-8, com 1 linha por evento: `ISO8601 [LEVEL] [thread] mensagem`.
- **Ainda não implementado** (não há logger em arquivo; SDD-16 RF-16.8 registra o flush como inexistente). Quando existir: buffer em memória com flush a cada 2 s pela T-IO, ou imediato em `Error`. Rotação: 5 arquivos de até 1 MB cada.
- Nenhum dado pessoal: sem nome de usuário em caminhos (usar `%LOCALAPPDATA%` literal no log) e sem títulos de janelas de outros apps. O diagnóstico de fullscreen registra apenas a **classe** da janela e o nome do processo.

## 6. Requisitos não funcionais

- **RNF-14.1:** Serialização da sessão em ≤ 2 ms e ≤ 64 KB no cenário `wave-heavy` (meta; benchmark).
- **RNF-14.2:** Gravação síncrona no encerramento em ≤ 200 ms em SSD (meta).
- **RNF-14.3:** Nenhuma alocação na T-Game para serializar em regime, exceto o crescimento inicial do buffer.
- **RNF-14.4:** Falha de gravação nunca derruba o processo nem bloqueia a T-Game por mais de 250 ms.

## 7. Estratégia de implementação

1. `AtomicFileWriter` e `JsonEnvelope` com testes de falha simulada (`IFileSystem` fino: porta com implementação real e falsa).
2. `SettingsStore` (E2, porque as configurações já são usadas pelo overlay).
3. `ProfileStore` e `SessionStore` com DTOs, mapeamento DTO ↔ `Simulation` e verificação de hash (E6).
4. Política de gatilhos no Runtime (`SavePolicy`, pura).

## 8. Dependências

SDD-07 (gatilhos de ciclo de vida), SDD-11 (estado e hash), SDD-13 (`stageTopic`; o perfil e o checkpoint foram substituídos por SDD-27), SDD-21 (posição das torres e `SEM POSIÇÃO`, ADR-034; antes `slotIndex`, `SEM SLOT`), SDD-22/ADR-026 (`layout`, `LayoutChanged`), SDD-25 (lista de RF-25.11), SDD-26 (ledger, perguntas sorteadas, eventos) e SDD-27 (economia, Reservas, regiões, missões, seed).

## 9. Critérios de aceitação

- **CA-14.1:** Ida e volta da sessão em 50 estados aleatórios de partidas scriptadas: `StateHash` idêntico após o restore (Linux e Windows).
- **CA-14.2:** Matar o processo (`Stop-Process -Force`) durante gravações em loop, 200 vezes, nunca deixa `session.json` ilegível: ele é válido ou é o `.bak` válido (T-14.3, Windows).
- **CA-14.3:** Arquivos corrompidos (truncado, JSON inválido, versão futura, hash divergente) vão para `corrupt\` e o app inicia normalmente (T-14.4).
- **CA-14.4:** Sessão de outro `contentHash` é descartada com aviso, e o perfil é mantido (T-14.5).
- **CA-14.5:** Durante 10 min de onda, ocorrem ≤ 12 gravações de sessão (contagem no log), excluindo os fins de onda.
- **CA-14.6 (idle):** o ciclo save → restore preserva `savedUtc`, `presence`, `pending` + `cardState`, `layout` (com `freeZones`, ADR-034), a posição das torres (`anchor`, `offsetDip`, `placed`, inclusive `SEM POSIÇÃO`; antes `slotIndex`/`SEM SLOT`); um save antigo com `slotIndex` migra conforme DP-33.7, `Rng`, `carry`, Reservas, região, missões do dia, ledger e contadores, com `StateHash` igual (SDD-25 CA-25.9); um `Hide` grava o snapshot de forma síncrona e a retomada com o relógio adiantado aciona o `OfflineCalculator` (T-14.8, T-14.9).

## 10. Cenários de teste

| ID | Cenário | Env |
|---|---|---|
| T-14.1 | Ida e volta + hash | Linux + Windows |
| T-14.2 | Settings: campo inválido volta ao padrão sem perder os demais | Linux |
| T-14.3 | Kill durante a gravação (harness com processo filho) | WindowsInteractive (não precisa de UI, mas usa processo real) |
| T-14.4 | Corrupção e quarentena (máximo de 10 arquivos) | Linux |
| T-14.5 | `contentHash` divergente | Linux |
| T-14.6 | Migração fictícia v0→v1 | Linux |
| T-14.7 | `SavePolicy`: tabela de gatilhos e debounce | Linux |
| T-14.8 | Ida e volta dos campos novos (idle): `savedUtc`, presença, pendências e `cardState`, `layout`, `anchor`/`offsetDip`/`placed` (ADR-034; antes `slotIndex` e `SEM SLOT`) e migração de save com `slotIndex` (DP-33.7), `Rng`, Reservas, região, missões, ledger, contadores | Linux |
| T-14.9 | `SavePolicy` idle: sem gatilho de fase Build; `Hide` síncrono grava o snapshot; nada é gravado oculto | Linux |

## 11. Riscos técnicos

| Risco | Prob. | Impacto | Mitigação |
|---|---|---|---|
| Antivírus bloqueando o `File.Replace` | Média | Baixo | Novas tentativas + fallback; o jogo continua |
| Pasta OneDrive ou com redirecionamento de "AppData" | Baixa | Baixo | `LocalAppData` não é sincronizado pelo OneDrive por padrão |
| Hash divergente por mudança de conteúdo em patch de balanceamento | Alta | Médio | Descartar a sessão (com aviso) e preservar o perfil; registrar migrações quando valer a pena |
