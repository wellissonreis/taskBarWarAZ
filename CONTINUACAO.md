# Continuação após reinício — taskBarWarAZ

Checkpoint observado em **11/11/2026**, antes do reinício. Este documento registra o estado encontrado; confira o disco antes de continuar. Não comprova execução de backup nem conclusão das tarefas.

O terminal do Claude informou retorno do limite em **12/11/2026 às 23h, America/Sao_Paulo**.

## Antes de reiniciar

Execute o backup abaixo e só reinicie se terminar sem erro. Ele inclui Git, worktrees com arquivos sem commit, históricos das sessões e relatórios temporários. O arquivo contém históricos privados; mantenha-o local.

```bash
mkdir -p "$HOME/backups-claude"
tar -czf "$HOME/backups-claude/taskBarWarAZ-antes-reboot-$(date +%Y%m%d-%H%M%S).tar.gz" \
  -C / \
  home/wellissonreis/repos/taskBarWarAZ \
  home/wellissonreis/repos/taskBarWarAZ-wt \
  home/wellissonreis/.claude-conta2/projects/-home-wellissonreis-repos-taskBarWarAZ \
  tmp/claude-1000/-home-wellissonreis-repos-taskBarWarAZ
```

Este backup foi proposto, **não executado** durante a análise. `/tmp` pode desaparecer no reinício. Não extraia o backup sobre repositórios ou históricos atuais para recuperar apenas relatórios.

## Abrir a sessão principal

```bash
cd /home/wellissonreis/repos/taskBarWarAZ
claude2 --resume 015b2283-5527-4958-b8bd-fd34471b9c0e
```

Envie à sessão: **“Leia CONTINUACAO.md integralmente, confira o estado real e execute o prompt de retomada abaixo.”**

## Prompt de retomada para o Tech Lead

Retome a operação do projeto taskBarWarAZ após reinício e interrupção por limite de uso. Você é o Tech Lead. Reconstrua o estado antes de executar integrações e continue autonomamente dentro do escopo aprovado.

### Regras e recuperação

- Raiz: `/home/wellissonreis/repos/taskBarWarAZ`.
- Worktrees: `/home/wellissonreis/repos/taskBarWarAZ-wt/<ficha>`.
- Leia `CLAUDE.md` e `/home/wellissonreis/.claude-conta2/skills/tmux-tech-lead/SKILL.md`.
- Use `claude2` ou `codex`. O wrapper `claude2` define `CLAUDE_CONFIG_DIR=$HOME/.claude-conta2` e executa `claude`; o nome do processo sozinho não identifica conta errada.
- Tech Lead coordena e revisa; workers escrevem, commitam, compilam e testam. Não use subagentes internos.
- Máximo de três workers simultâneos na janela tmux `worker`. Recrie painéis e monitores: IDs antigos e sinais `tmux wait-for` não sobrevivem ao reboot.
- Não faça push, stash, reset destrutivo, descarte de alterações nem `git add -A`/`git add .`.
- Não recrie worktrees existentes nem reinicie tarefas do zero.
- Este documento é uma entrega de documentação posterior ao checkpoint. Sua presença como arquivo não rastreado não é alteração de um worker; preserve-o.

Scratchpad original:

```text
/tmp/claude-1000/-home-wellissonreis-repos-taskBarWarAZ/015b2283-5527-4958-b8bd-fd34471b9c0e/scratchpad
```

Se desapareceu, delegue a recuperação dos relatórios e artefatos necessários a partir do backup em `/home/wellissonreis/backups-claude/`, para uma pasta persistente. Não sobrescreva código ou históricos atuais. Informe a todos os workers o caminho recuperado dos REPORTs.

Históricos completos:

```text
/home/wellissonreis/.claude-conta2/projects/-home-wellissonreis-repos-taskBarWarAZ/<session-id>.jsonl
```

Os briefings nesses históricos são a fonte de verdade para o escopo. Handoffs em `specs/evidencias` podem estar desatualizados; confronte-os com código, Git e logs.

### Estado observado

- `main` estava limpa em `641fc33`: `feat(core): implement T3.8 core, fall, stages, stars, XP and level`.
- MERGE4 concluída. Antes de MERGE5, o Tech Lead relatou 481 testes WSL aprovados e Persistence.Tests aprovado no Windows. Esses números não comprovam o estado posterior.
- MERGE5 já integrou T3.8; não repita esse passo. Não havia REPORT final `MERGE5.md`.
- Três workers pausados: MERGE5, T1.14 e T5.9.
- T6.6B tinha `specs/PLANO.md` modificado sem commit. Preserve e examine antes de rebase.
- T1.14 e T5.9 tinham implementação sem commit, detalhada abaixo.

### Worker MERGE5 — integração

- Sessão: `bf89d45d-d881-4803-87da-ba2efa10298e`.
- Retome com `claude2 --resume bf89d45d-d881-4803-87da-ba2efa10298e` em painel próprio, a partir da raiz confiável do projeto.
- Última mensagem: “Commit, ff, cleanup; then step 2”.
- T3.8 já estava na main; sua worktree já havia sido removida.
- T6.6B estava em `5662ac5`, com alteração local de `specs/PLANO.md`.
- Confira status, reflogs e eventual rebase em andamento antes de qualquer comando de integração.

Ordem original e bases de `git rebase --onto main <base>`:

| Passo | Branch | Base original | Conteúdo |
| --- | --- | --- | --- |
| 1, concluído | ficha/T3.8 | 651e37d | T3.8, integrada como 641fc33 |
| 2 | ficha/T6.6B | f6dcbd3 | T6.5 e T6.6B |
| 3 | ficha/TU.1 | 6a71e6a | T3.11, T6.2, T6.3 e TU.1 |
| 4 | ficha/T6.7 | 9b69484 | T6.7 |
| 5 | ficha/T6.9a | 9b69484 | Conferir descarte de patches T2.4b/T6.8 já integrados; restam T3.10 e T6.9a |
| 6 | ficha/T3.12 | 7d29221 | T3.12 |

Execute o rebase na worktree da branch, confira os commits restantes com `git log --oneline main..HEAD`, rode `scripts/wsl/build.sh Release` e `scripts/wsl/test.sh`, com zero warnings e tudo aprovado, e só então integre na raiz com `merge --ff-only`. Preserve um commit por ficha. Não rode testes Windows nesta integração.

Após cada rebase, atualize `specs/PLANO.md` e faça amend no último commit da branch, conforme briefing original:

- Marcar T3.8; T6.5 e T6.6 (substituir “🟡 parte A” por ✅); T3.11, T6.2 e T6.3; T6.7; T3.10; T3.12.
- Acrescentar ao fim da linha T3.11: `(+ TU.1: integração host/eventos/ausente/offline)`.
- Na linha T6.9, registrar `🟡 parte A (T6.9a)`.

Resolva conflitos mecânicos preservando ambas as intenções e consultando os REPORTs. Em conflito de comportamento, interrompa aquela integração e reporte, sem decidir arquitetura. Não aborte uma operação sem primeiro identificar e preservar alterações anteriores.

Se hashes golden de SimHashTests/ReplayTests mudarem por integrações anteriores, investigue a causa e regenere apenas quando justificado. Confirme determinismo em cinco execuções e registre antigo → novo.

Não remova branches/worktrees sem comprovar integração e ausência de trabalho exclusivo ou alterações locais. As auxiliares T6.5, T3.11, T6.2, T6.3 e T3.10 só podem ser limpas após comprovação de preservação das entregas. Não toque nas worktrees das outras tarefas.

REPORT `MERGE5.md`: hashes finais por ficha, conflitos, goldens regenerados, contagens de testes e problemas. Último comando: `tmux wait-for -S MERGE5`.

### Worker T1.14 — medição de desempenho

- Sessão: `b477f770-4be5-4d0c-85d0-f0ddca01470e`.
- Retome com `claude2 --resume b477f770-4be5-4d0c-85d0-f0ddca01470e` a partir da raiz confiável; mantenha todas as operações da tarefa em sua worktree.
- Worktree: `/home/wellissonreis/repos/taskBarWarAZ-wt/T1.14`.
- Branch `ficha/T1.14`, base/HEAD observado `335633f` (T1.13).
- Modificado: `scripts/windows/measure-perf.ps1`.
- Não rastreado: `specs/evidencias/T1.14.md`.
- Último ponto: depuração de contadores localizados do Windows pt-BR e acesso ao `dwm.exe`.

Retome o script existente, `machine.json` e `summary.json` conforme SDD-16 RF-16.3/CA-16.1. Leia `T1.13.md` para cenários `--bench`, CSV e exit codes. Colete CPU do processo e delta DWM conforme especificação, descarte aquecimento de 10 s e suporte repetições. Colete dados de máquina automaticamente; D-05 ainda não estava respondida.

Não altere `src/` nem `specs/PLANO.md`. Sem merge/rebase/push/stash/reset. Use `SIDEWAR_WIN_MIRROR=/mnt/c/dev/sidewar-T1.14`.

Valide build/test WSL e uma medição curta real no Windows, conferindo exit code e `summary.json`. Rotule cada métrica P-00/P-01/P-02/P-11/P-12/P-22 como medida ou não medida. Para amostra curta compartilhada, explicite essa limitação. Não invente números. O go/no-go CA-16.2 pertence à T1.15.

Garanta encerramento do App de benchmark. Finalize evidência e um commit com arquivos explícitos em `ficha/T1.14`: `feat(perf): implement T1.14 measure-perf, machine.json and summary.json`. Preserve a atribuição exigida no briefing original.

REPORT `T1.14.md`: resumo, números da amostra, arquivos, comandos/resultados, hash, medido/não medido e informações para T1.15. Último comando: `tmux wait-for -S T1.14`.

### Worker T5.9 — configurações e sobre

- Sessão: `8b781094-4757-4220-8976-9df31c37ffec`.
- Retome com `claude2 --resume 8b781094-4757-4220-8976-9df31c37ffec` a partir da raiz confiável; mantenha as operações na worktree da tarefa.
- Worktree: `/home/wellissonreis/repos/taskBarWarAZ-wt/T5.9`.
- Branch `ficha/T5.9`, HEAD observado `92dcbcf`.

Alterações sem commit:

- `src/SideWar.App/SideWar.App.csproj` modificado.
- `specs/evidencias/T5.9.md` não rastreado.
- `src/SideWar.App/Windows/` não rastreado.
- `tests/SideWar.App.SmokeTests/SettingsViewModelTests.cs` não rastreado.
- `tests/SideWar.App.SmokeTests/SettingsWindowTests.cs` não rastreado.

Missão: SettingsWindow e AboutWindow WPF, todos os campos de AppSettings editáveis com validação, Salvar/Cancelar, versão/licenças OFL/atribuições, abertura só por ação do usuário e callback `onClosed` para restauração de foco. Use HotkeyParser se disponível na base; caso contrário, campo texto validado por formato, conforme briefing. Referências: SDD-15 RF-15.9, SDD-14 RF-14.4, ADR-015/021/031.

Não altere Program.cs/host, Platform.Windows, implementação de Persistence ou `specs/PLANO.md`. Sem merge/rebase/push/stash/reset. Use `SIDEWAR_WIN_MIRROR=/mnt/c/dev/sidewar-T5.9`.

Houve execução de build/test WSL e Windows imediatamente antes da interrupção, mas o comando filtrava saídas por grep/tail. Exit code global zero não comprova aprovação de cada comando. Recupere logs/resultados e confirme verificações relevantes antes de finalizar. O handoff local ainda dizia “implementação em curso” e pode não refletir todo o código escrito.

Verificação original: build Release/test WSL, build Windows e `test.ps1 -IncludeInteractive`, além de `git diff --check`; registre falhas ambientais comprovadas. Finalize evidência, um commit por arquivos explícitos em `ficha/T5.9` com título `feat(ui): implement T5.9 ...`, preservando a atribuição do briefing original.

REPORT `T5.9.md`: resumo, arquivos, comandos/resultados, hash, medido/não medido e o que o App precisa conectar. Último comando: `tmux wait-for -S T5.9`.

### Outras entregas preservadas

Worktrees existentes no checkpoint, em geral com commits e REPORTs aguardando revisão/integração:

```text
T1.13 T1.14 T2.5 T2.6 T3.10 T3.11 T3.12
T4.6 T4.8 T4.9
T5.1 T5.2 T5.3 T5.4 T5.5 T5.8 T5.9
T6.2 T6.3 T6.5 T6.6B T6.7 T6.9a TU.1
```

Não confunda entrega em branch com integração na main. MERGE6 (interface/plataforma) estava previsto depois de MERGE5; reconstrua sua ordem pelas dependências e REPORTs antes de iniciá-lo.

### Verificação Windows e critérios pendentes

- Serialize testes interativos e medições. Mirrors distintos não isolam desktop, cursor, hotkeys ou DWM.
- `SceneModeTests.PeekFades` falhou na rodada posterior à T1.11, embora tivesse passado no REPORT original. Investigue isoladamente com desktop interativo e cursor real; a causa ainda não foi determinada.
- Build/test WSL não comprova comportamento Win32, foco, DPI ou desempenho Windows.
- E4 continua dependente do go/no-go CA-16.2 registrado. Não declare aprovado sem evidência.
- Após cada entrega, leia o REPORT, revise o diff, delegue verificações necessárias e feche o painel concluído. Não confunda expiração de monitor com conclusão da tarefa.

### Primeira ação e continuidade

Leia as instruções, recupere os relatórios, inventarie Git/worktrees e restaure os três workers com seus históricos completos. Se uma sessão não puder ser retomada, abra um worker novo com o briefing original recuperado do JSONL e este checkpoint; não recrie sua worktree.

Passe a cada worker seu estado parcial e peça que confira o último comando efetivamente executado antes de continuar. Registre novos REPORTs em armazenamento persistente e atualize os monitores/sinais para os painéis atuais.

Apresente um resumo curto do estado recuperado e prossiga com as tarefas já autorizadas, preservando todo trabalho parcial.
