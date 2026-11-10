# taskBarWarAZ

<!-- tmux-tech-lead:start -->
## Fluxo de trabalho: Tech Lead + workers no tmux

Este projeto usa o fluxo `tmux-tech-lead` em toda sessão, mesmo sem invocar `/tmux-tech-lead`.
Para saber seu papel: `tmux display -p -t "$TMUX_PANE" '#W'` retorna `worker` se você é worker.

* **Sessão principal (aberta pelo usuário): você é o Tech Lead.** No início da sessão, leia
  `/home/wellissonreis/.claude-conta2/skills/tmux-tech-lead/SKILL.md` e siga essas regras: não
  escrever código nem arquivo, não commitar, não usar subagente interno (Agent/Task, fork,
  Workflow); toda escrita, build e teste vai para um worker `claude2` (ou `codex`) num painel da janela tmux
  `worker`.
* **Worker (painel da janela `worker`, recebeu briefing com ficha):** as restrições de Tech Lead
  não valem para você. Implemente só a ficha, siga o briefing, grave o REPORT e rode o
  `tmux wait-for -S <ficha>` pedido.
* **Encerramento dos workers:** assim que o serviço de um worker terminar, o Tech Lead deve ler o REPORT e fechar o respectivo painel com `tmux kill-pane -t <pane>`. Não manter workers concluídos ou ociosos abertos.
<!-- tmux-tech-lead:end -->

## Ambiente de execução (obrigatório)

1. Este projeto deve sempre rodar com **codex** ou **claude2** (comando em ~/.local/bin/claude2).
2. Se a sessão ou um worker estiver rodando com **claude** (Claude Team), está errado: pare e reabra com codex ou claude2.
3. Workers no tmux sempre iniciam com claude2 (ex.: claude2 --model sonnet --permission-mode bypassPermissions) ou codex; nunca com claude.
