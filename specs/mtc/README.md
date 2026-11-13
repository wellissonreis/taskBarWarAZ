# Checklists manuais (MTC): como executar

Preparado pela ficha MTC-PREP. Regra de SDD-17 RF-17.6: **o executor prepara; a execução é do usuário (ou de uma sessão com acesso ao desktop); o resultado nunca é presumido.** Todos os arquivos `MTC-*.md` desta pasta têm a tabela `ID | passos | esperado | resultado (✔/✘) | evidência | data | build`, com **resultado em branco**. Em branco = não executado. Cada "esperado" cita a fonte (SDD, RF/CA ou REPORT). Onde o texto é inferência do preparador, está dito.

Por que um desktop isolado: os testes e checklists interativos movem o cursor real e mexem em foco, z-order e hotkeys (Ctrl+Alt+G/H/P). A decisão do Tech Lead de 2026-11-13 (PLANO §8) é adiá-los até haver VM, Sandbox ou RDP. Os REPORTs WINFLAKY/WINFLAKY2 mostram falhas causadas por um mouse humano movendo o cursor durante a suíte, e por outro `SideWar.App.exe` rodando na mesma sessão (hotkeys já registradas, `FindWindow` global).

## 1. Índice dos checklists

| Arquivo | Ficha (PLANO) | Conteúdo |
|---|---|---|
| `MTC-TA.1.md` | TA.1 | Spike de z-order/clique sobre a barra: MTC-21.13, MTC-22.11, decisão de `towersInBar` |
| `MTC-T1.15.md` | T1.15 | MTC-03, 04, 05, 08.1, MTC-21.1 a 21.12, MTC-22 (1, 2, 8, 9), `--dump-taskbar` |
| `MTC-T2.7.md` | T2.7 | MTC-06 (13 linhas), MTC-07.1 a 07.4, CA-07.4, CA-07.7 |
| `MTC-T4.10.md` | T4.10, T4.10b | Roteiro da composição do App (11 passos) e do card "Bem-vindo de volta" (11 passos), MTC-25.1 |
| `MTC-TA.5-Windows.md` | TA.5 (Windows) | Cena sobre a barra, `ZOrderCoordinator`, MTC-22.1 |
| `MTC-TA.8.md` | TA.8 | Modo construção: MTC-22.12 |
| `MTC-TA.10-Windows.md` | TA.10 (Windows) | Widget compacto: MTC-23.1, MTC-23.3, hit-test, hover |
| `MTC-TA.13.md` | TA.13 | Fechamento: MTC-21.1 a 21.13, MTC-22.11 a 22.13, `strip-present` com 24 torres |
| `MTC-T5.11.md` | T5.11 (e pendências de T5.2, T5.3, T5.8, T5.9) | MTC-22 (3 a 7, 10), MTC-23.2/23.4/23.5, MTC-24.1 a 24.5, MTC-15.1 a 15.4, Configurações/Sobre |
| `MTC-T6.12.md` | T6.12 | Fechamento E6: MTC-07.1 a 07.3 com disco, MTC-25.1, MTC-26.1, P-19 |
| `MTC-T8.5.md` | T8.4, T8.5 | Pacote, MTC-18.1 (zero rede), MTC-18.2 (sem .NET), MTC-18.3 (remoção limpa) |
| `MTC-MED.md` | T1.15, T4.12, T7.1, T7.5 | Folhas de medição P-xx (SDD-16) |
| `MTC-SDD-19-20.md` | (sem ficha no PLANO) | MTC-19.1 a 19.3, MTC-20.1 a 20.3 |

**Pré-condição de código.** Várias fichas Windows ainda não foram implementadas (TA.1, TA.5-Windows, TA.8, TA.10-Windows, TA.13; a T4.10 deixou de fora painel/hotspots, EcoQoS, recuperação de crash e log em arquivo). Cada arquivo diz o que precisa existir antes. Não execute uma linha cujo recurso ainda não existe; deixe em branco e anote.

## 2. Montar o ambiente isolado

Requisitos do app: Windows 11 22H2 (build 22621) ou superior, x64, GPU com driver WDDM (SDD-18 RF-18.7). Windows 10 não é suportado oficialmente. Conferir `winver` (e `SideWar.exe --version --diag`) e registrar a build em cada linha.

### 2.1 Gerar o pacote (no PC principal, uma vez por build a testar)

`scripts/windows/publish.ps1` (REPORT T8.2) roda build Release, `test.ps1` sem interativos, `dotnet publish` win-x64, monta `SideWar.exe` + `LICENSE.txt` + `THIRD-PARTY-NOTICES.txt` + `README.txt` e produz:

```
artifacts\release\SideWar-<versão>-win-x64.zip
artifacts\release\SHA256SUMS.txt
```

Esse ZIP é self-contained (a MTC-18.2 existe justamente para provar que roda sem .NET). Anote o hash do ZIP e o `--version` (ex.: `0.1.0+3943e12`): é a coluna **build** dos checklists.

### 2.2 Opção A: Windows Sandbox (`.wsb`)

Serve para quase todos os itens de **um monitor**, e é a opção natural para MTC-18.2 (SDD-18 cita "Windows Sandbox ou VM sem .NET"). O Sandbox descarta tudo ao fechar: salve a evidência numa pasta mapeada gravável.

Crie `SideWar-MTC.wsb` (ajuste os dois caminhos do host):

```xml
<Configuration>
  <VGpu>Enable</VGpu>
  <Networking>Default</Networking>
  <MemoryInMB>8192</MemoryInMB>
  <MappedFolders>
    <MappedFolder>
      <!-- pasta do ZIP publicado pelo publish.ps1 (somente leitura) -->
      <HostFolder>C:\dev\sidewar-<ficha>\artifacts\release</HostFolder>
      <SandboxFolder>C:\Users\WDAGUtilityAccount\Desktop\pacote</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
    <MappedFolder>
      <!-- evidências: gravável, fica no host depois que o Sandbox fecha -->
      <HostFolder>C:\dev\mtc-evidencias</HostFolder>
      <SandboxFolder>C:\Users\WDAGUtilityAccount\Desktop\evidencias</SandboxFolder>
      <ReadOnly>false</ReadOnly>
    </MappedFolder>
  </MappedFolders>
</Configuration>
```

- `Networking` em `Default` (ligado) é necessário na **MTC-18.1** (zero rede: precisa existir rede para a ausência de conexões significar algo). Nos demais itens pode ser `Disable`, o que reforça o isolamento.
- Não use `LogonCommand` para iniciar o app: a execução é manual, para você ver a sessão limpa.
- Dentro do Sandbox: copie o ZIP para `%LOCALAPPDATA%\Programs\SideWar` e extraia (RF-18.5); confira com `SHA256SUMS.txt` (`Get-FileHash -Algorithm SHA256`).
- **Limites do Sandbox (verificar no seu ambiente; o preparador não testou):** um único monitor virtual, sem bateria física e sem controle da barra/escala igual a uma VM completa. Os itens marcados **(2 monitores)** (MTC-05.1, 05.3, 06.13, 21.7, 23.3-d) e MTC-07.4 (economia de bateria) provavelmente exigem a opção B, ou o próprio PC do usuário. Se o ambiente não permitir um item, deixe o resultado em branco e anote o motivo na coluna evidência; **não marque ✔**.

### 2.3 Opção B: VM (Hyper-V, VirtualBox, VMware) ou RDP

Use quando o item exigir 2 monitores, escala/DPI mistos, barra para cima/esquerda, sono/logoff persistente, bateria, ou quando precisar instalar o SDK .NET para a suíte interativa (seção 4).

- VM com Windows 11 22H2+ x64 e **dois monitores virtuais** (Hyper-V: sessão avançada/Enhanced Session; RDP: `mstsc /multimon` com escala diferente em cada monitor). Dê aceleração de vídeo à VM; sem GPU o DWM Δ deixa de ser representativo.
- Faça um *checkpoint* da VM limpa antes de cada rodada: MTC-18.2 pede VM **sem .NET** e MTC-18.3 pede "sem resto em `%LOCALAPPDATA%` nem em `Run`".
- Um desktop só do teste: nada de Chrome pessoal em primeiro plano (o REPORT WINFLAKY achou falhas por foreground roubado).
- O Windows precisa estar em sessão ativa e desbloqueada; não toque no mouse/teclado durante testes automatizados.

### 2.4 Instrumentos citados pelos checklists

Process Explorer (wakes/s, working set), Monitor de Recursos ou `netstat -b` (MTC-18.1), `taskkill`/Gerenciador de Tarefas (reiniciar o Explorer), Win+Shift+S (captura), relógio do sistema editável (T4.10b-1) e `session.json` em `%LOCALAPPDATA%\SideWar`. Pasta de dados do app: `%LOCALAPPDATA%\SideWar\` (modo portátil: `<dirExe>\data\` se existir `portable.flag`, SDD-14 RF-14.1).

## 3. Ordem recomendada de execução

A ordem respeita as dependências do PLANO (§4 e §8) e as ordens de ambiente de §8 ("Adiadas, na ordem: TA.1 → TA.5-Windows → TA.8 → TA.10-Windows → TA.13").

0. **Preparação:** pacote da seção 2.1; `--version`, `--version --diag` (MTC-T8.5 T8.4-1 a 3), `winver`.
1. **`MTC-TA.1`** primeiro: o spike decide `towersInBar` e precede qualquer render sobre a barra (SDD-21 DP-33.4 (c); PLANO TA.1 "antes de qualquer render").
2. **`MTC-T1.15`** (E1): MTC-03, 04, 05, 08.1, 21.1 a 21.12, MTC-22 (1, 2, 8, 9); `--dump-taskbar` na máquina real; medições `MTC-MED` T1.15-go.
3. **`MTC-T2.7`** (E2): MTC-06 inteira, MTC-07.1 a 07.4.
4. **`MTC-T4.10`**: composição e card "Bem-vindo de volta" (valida a base que as fichas seguintes usam).
5. **`MTC-TA.5-Windows` → `MTC-TA.8` → `MTC-TA.10-Windows`**, na ordem de PLANO §8, assim que cada parte Windows existir.
6. **`MTC-T5.11`** (E5): MTC-22, 23, 24, 15 e pendências de T5.2/T5.3/T5.8/T5.9. Playtest de 30 min só com o mouse.
7. **`MTC-T6.12`** (E6): MTC-07 com disco, MTC-25.1, MTC-26.1.
8. **`MTC-TA.13`**: fechamento (repete MTC-21 e MTC-22.11 a 22.13 com tudo integrado) e atualiza `E1.md`/`E4.md`.
9. **`MTC-MED`** (E4/E7): P-11/P-12/P-17/P-18, T7.1 (P-00..P-22), T7.5 (soak 2 h). Rodar em máquina quieta.
10. **`MTC-T8.5`** (E8): pacote final, MTC-18.1/18.2/18.3, P-08 e P-00. Por último, em VM limpa.
11. `MTC-SDD-19-20`: sem ficha dona; encaixar no acabamento de E4/E5 quando o Tech Lead atribuir.

## 4. O que bloqueia o quê

Fontes: PLANO (Aprovação das etapas E1, E2, E4, E5, E6, E7, E8; §8) e SDD-16/21/22.

| Marco | Itens bloqueantes | Observação |
|---|---|---|
| **E1** (go/no-go; **"sem essa decisão, a E4 não começa"**, PLANO E1) | MTC-03, MTC-04, MTC-05, MTC-21 (preenchidos com capturas), MTC-08.1, MTC-22 (1, 2, 8, 9), `--dump-taskbar` do Windows 11 do usuário, P-00/01/02/11/12/22 rotulados, `machine.json` (D-05) e **decisão go ou ADR de contingência** (CA-16.2) | Para a decisão: `MTC-MED` T1.15-go |
| **Spike TA.1** | MTC-21.13 e MTC-22.11 (`MTC-TA.1.md`) | Decide `towersInBar` padrão (DP-33.4). Cena/torres sobre a barra (TA.5-Windows, TA.8, TA.10-Windows) dependem dele; reprovando, vale o hospedeiro `Band` (PLANO §8). É obrigatório na E1 (SDD-21 DP-33.4 (c)) |
| **E2** | MTC-06 a 100% (E2.md); MTC-07.1..3 ficam "parcial" | CA-06.2, CA-07.7 |
| **E4** | Decisão da E1; TA.13 atualiza `E4.md`; CA-22.1 a 22.10, incl. CA-22.3 (MTC-22.1, 22.2, 22.11); golden images, memória do atlas, CSVs | Metas de desempenho são registradas sem bloquear (PLANO E4) |
| **E5** | MTC-22, 23, 24 em `E5.md` (T5.11); CA-22.3, CA-23.2, CA-24.2 | |
| **E6** | MTC-07.1..3 completos, MTC-25.1, P-19 (retorno de 12 h) | PLANO E6, Aprovação |
| **E7** | T7.1 (P-00..P-22) e T7.5 (soak 2 h); reprovadas com correção ou ADR do usuário | Depende de D-05 |
| **E8 / release** | MTC-18.1, 18.2, 18.3 em `E8.md`, P-08, P-00 nas duas variantes, ADR da escolha | `LICENSE.txt` PENDENTE (T8.3) também bloqueia o release (REPORT T8.2) |

## 5. Como registrar evidência

1. Em cada linha execute os passos, preencha **resultado** com ✔ ou ✘ (em branco = não executado), **data** (AAAA-MM-DD) e **build** (saída de `SideWar.exe --version`, ex.: `0.1.0+3943e12`).
2. **Evidência** é um caminho relativo + nota curta. Padrão sugerido: `specs/evidencias/mtc/<arquivo-MTC>/<ID>_<AAAAMMDD-HHMM>.<png|txt|json>`, copiado da pasta `evidencias` mapeada para o repositório. Capturas: Win+Shift+S. Logs: redirecione a saída (`SideWar.exe --diag > diag.json`). Para tempos (≤ 150 ms, ≤ 1 s, ≤ 5 s), anote o valor medido e como (cronômetro de vídeo, log, CSV), não só ✔.
3. ✘ exige nota do que foi observado. Se um item não pôde ser executado (monitor, bateria, recurso não implementado), deixe em branco e escreva o motivo na evidência: **não presumir resultado**.
4. Transcreva o resumo para o `specs/evidencias/E<n>.md` da etapa (PLANO exige essas evidências) e referencie o arquivo MTC.
5. Itens medidos: rotule **medido** ou **reprovado** e anexe `machine.json`, `summary.json` e os CSVs (SDD-16 RF-16.3). Reprovação de meta pede correção ou ADR aprovada pelo usuário (CA-16.3).
6. Não edite os "esperados": se divergirem do comportamento correto, abra ajuste de SDD/ADR; não ajuste a tabela para o resultado passar.

## 6. Suíte interativa (`test.ps1 -IncludeInteractive`)

`scripts/windows/test.ps1` (SDD-02) roda `Core.Tests`, `Runtime.Tests`, `Persistence.Tests`, `Rendering.Tests` e, com `-IncludeInteractive`, também `Platform.Windows.Tests` e `App.SmokeTests` (sem o switch, aplica `--filter Env!=WindowsInteractive`). Resultados em `artifacts\test-results\windows\*.trx`.

**Requisitos:** o repositório (espelho do código, ex. `C:\dev\sidewar-<ficha>`) **e o SDK .NET** no Windows isolado, pois o script chama `dotnet.exe test`. O Sandbox padrão **não tem .NET** (é exatamente o que a MTC-18.2 explora): para rodar a suíte use VM com o SDK instalado (ou instale o SDK no Sandbox, com rede ligada, a cada sessão). Não é possível rodar a suíte a partir do ZIP publicado.

```powershell
# no desktop isolado, na raiz do repositório
powershell -File scripts\windows\build.ps1 -Configuration Release
powershell -File scripts\windows\test.ps1 -IncludeInteractive
```

Observações dos REPORTs:
- O script usa `exit $LASTEXITCODE` no primeiro projeto que falhar, então uma falha em `Platform.Windows.Tests` impede `App.SmokeTests`. Os REPORTs rodaram então `dotnet test tests/SideWar.Platform.Windows.Tests -c Release` e `tests/SideWar.App.SmokeTests` direto. Isso é um contorno aceito nos REPORTs T5.2, T5.3 e T5.8.
- `App.SmokeTests` sem janelas: `--filter "Env!=WindowsInteractive"` (22/22 no REPORT T4.10b).
- Falhas conhecidas: `FullscreenWatcherTests.BorderlessWindowOfAnotherProcessHidesAndRestores` falhava por foreground lock com outro app (Chrome) em primeiro plano; o REPORT WINFLAKY aponta correção só em `tests/` no commit `dad4967`, que já está na main como `9a64159` (MERGE7). `HotkeyTrayTests` e `WidgetWindowTests.StylesOwner...` falham se outro `SideWar.App.exe` estiver rodando (mesma sessão, hotkeys e `FindWindow` globais). `SceneModeTests` (peek, retorno a Passivo em 8 s) falha se o mouse real se mexer durante a execução (REPORT WINFLAKY2; o `CursorPin` não commitado do WINFLAKY2 só mitiga).
- **Regra para a execução:** feche qualquer `SideWar.App.exe`, não toque no mouse/teclado, nenhum outro app em primeiro plano, sessão desbloqueada. Registre as contagens por projeto e anexe os TRX.
- Memória do projeto: testes interativos movem o mouse; só rodam no isolado ou com autorização do usuário.

## 7. Medição de desempenho (`measure-perf.ps1`)

`scripts/windows/measure-perf.ps1` (SDD-16 RF-16.3, REPORT T1.14) precisa só do PowerShell e de um executável: `-Exe <caminho>` aceita o `SideWar.exe` do ZIP extraído. Sem `-Exe` ele procura `src\SideWar.App\bin\<Configuration>\...\SideWar.App.exe` (exige build do repo). Não usa `dotnet`, portanto roda no Sandbox copiando só o script e o pacote. O Windows é pt-BR: o script usa CIM, não `Get-Counter`.

Parâmetros: `-Scenario` (obrigatório; `e1-synthetic`, `hidden`, `idle-static`, `baseline-wpf` ou `e1` para os quatro), `-Seconds` (padrão 60, inclui o aquecimento), `-Repeats` (padrão 1), `-DwmRestSeconds` (padrão 60; 0 = não medir), `-WarmupSeconds` (padrão 10), `-Exe`, `-Out`, `-NoInput`, `-Configuration`.

```powershell
# oficial para o go/no-go da E1 (REPORT T1.14, "Para a T1.15"); sem outros workers
powershell -File scripts\windows\measure-perf.ps1 -Scenario e1 -Seconds 300 -Repeats 3 -DwmRestSeconds 60 `
  -Exe C:\Users\WDAGUtilityAccount\AppData\Local\Programs\SideWar\SideWar.exe `
  -Out C:\Users\WDAGUtilityAccount\Desktop\evidencias\perf\<AAAAMMDD>-e1

# um cenário só, 5 min
powershell -File scripts\windows\measure-perf.ps1 -Scenario hidden -Seconds 300 -Exe <SideWar.exe> -Out <pasta>
```

- Saídas: `machine.json` (CPU, núcleos, RAM, GPU, Windows, monitores, DPI, plano de energia), um CSV por execução e `summary.json` (com `shortSample=true` se < 300 s). O veredito do script é informativo.
- Sem `-NoInput` o script usa hotkeys e bandeja; com `-NoInput` evita injetar entrada.
- **Limites:** o DWM Δ de repouso curto é ruidoso (REPORT T1.14); meça com `-DwmRestSeconds 60`. Em VM/Sandbox a GPU é virtual, então CPU/DWM/GPU **não** valem como medição oficial das metas; use o desktop isolado para validar que o script roda e o PC de referência (D-05) para os números oficiais, rotulando a origem em `machine.json`. Os cenários de apresentação, soak e retorno offline ainda não existem no script (ver `MTC-MED.md`).
- `SideWar.exe --bench <cenario> --seconds <n> --out <arquivo.csv>` pode ser rodado sem o script (SDD-16 RF-16.2); `--bench` não altera dados do usuário (RNF-16.2).
