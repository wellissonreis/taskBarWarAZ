# taskBarWarAZ
Regras completas do projeto em CLAUDE.md.

## Worker (Codex)
- Codex só é usado quando o briefing justificar; o implementador principal é o Sonnet via claude2 (nunca a conta `claude`).
- Implemente só a ficha, não abra nem delegue a outro worker, grave o REPORT e encerre sem perguntar; diante de decisão não coberta, pare e reporte.
- Testes direcionados à mudança (regressão completa só se a ficha for de marco de entrega). Nunca enfraqueça teste para ficar verde: mudança de asserção exige aprovação do usuário.
- Balanceamento, economia, dificuldade, runas e desempenho: Evaluation Driven (harness parametrizável, seeds fixas, critério objetivo, limite de tentativas).

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
