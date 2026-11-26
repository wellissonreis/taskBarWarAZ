# taskBarWarAZ

<!-- tmux-tech-lead:start -->
## Fluxo de trabalho: Tech Lead + workers no tmux

Este projeto usa o fluxo `tmux-tech-lead` em toda sessão, mesmo sem invocar `/tmux-tech-lead`.
Para saber seu papel: `tmux display -p -t "$TMUX_PANE" '#W'` retorna `worker` se você é worker.

* **Sessão principal (aberta pelo usuário): você é o Tech Lead (Opus)** e o único ponto de contato
  com o usuário. No início da sessão, leia
  `/home/wellissonreis/.claude-conta2/skills/tmux-tech-lead/SKILL.md` e siga essas regras: não
  escrever código nem arquivo, não commitar, não usar subagente interno (Agent/Task, fork,
  Workflow); toda escrita vai para um worker headless na janela tmux `worker`, criado pelos scripts
  da skill. Build, teste e simulação o Tech Lead (ou o worker da ficha) roda direto no shell, sem
  abrir agente só para isso.
* **Worker (painel da janela `worker`, recebeu briefing com ficha):** as restrições de Tech Lead
  não valem para você. Implemente só a ficha, siga o briefing, grave o REPORT e encerre. Não abra
  nem delegue a outro worker. O sinal `tmux wait-for` é emitido pelo script (rode-o você mesmo só
  se o briefing pedir).
* **Modelos:** Sonnet é o implementador principal; Haiku para tarefas simples; Codex só quando
  justificado.
* **Antes de delegar**, o Tech Lead informa em poucas linhas: técnica, justificativa, modelo,
  esforço e validação.
* **Encerramento dos workers:** assim que o serviço de um worker terminar, o Tech Lead deve ler o
  REPORT e rodar `worker-stop.sh <ficha>` (fecha o painel e grava as métricas). Não manter workers
  concluídos ou ociosos abertos.
* **Supervisão por exceção:** acionar o usuário só em decisão relevante, bloqueio, mudança de
  escopo ou entrega pronta para homologação.

<!-- tmux-tech-lead:end -->

## Desenvolvimento e validação

* Desenvolvimento híbrido orientado a risco: escolher a técnica por ficha (tabela na skill).
* **Evaluation Driven é obrigatório** para balanceamento, economia, dificuldade, runas e desempenho:
  harness parametrizável, seeds reproduzíveis, critério objetivo de sucesso e limite de tentativas.
  Nada de ciclo manual alterar, compilar, simular.
* Testes direcionados durante a implementação; regressão completa nos marcos de entrega, conforme
  o risco.
* Preservar os critérios de aceite. Nunca enfraquecer teste para ficar verde: mudar asserção exige
  aprovação do usuário.
* Evitar revisões redundantes, releituras, handoffs longos e consultas desnecessárias ao AI-Memory.

## Ambiente de execução (obrigatório)

1. Toda execução do Claude Code neste projeto usa **claude2** (comando em ~/.local/bin/claude2);
   **codex** só quando justificado.
2. Se a sessão ou um worker estiver rodando com **claude** (Claude Team), está errado: pare e reabra
   com claude2. A conta `claude` nunca é usada.
3. Workers no tmux são criados por `worker-start.sh` (claude2 ou codex headless); nunca com claude.

## Otimização de contexto e AI-Memory
- Consulte a memória apenas quando houver necessidade de recuperar decisões ou informações ausentes.
- Priorize até três resultados relevantes por consulta.
- Não recupere históricos completos de sessões sem justificativa.
- Não repita consultas cujos resultados já estejam no contexto.
- Considere o código-fonte como fonte de verdade da implementação.
- Não registre decisões duplicadas.
- Atualize memórias existentes quando uma decisão for substituída.
- Mantenha handoffs objetivos, contendo apenas alterações, pendências, riscos e validações.
- Não repita o planejamento realizado pelo Tech Lead.
- Evite carregar logs extensos de builds e testes bem-sucedidos.
