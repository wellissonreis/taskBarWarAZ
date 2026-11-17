# TD.30b — pergunta-bônus por tempo real e fonte maior

- Cadência: 1 pergunta a cada intervalo sorteado em [240 s, 360 s] de relógio real (`TdUiController.Quiz.cs`), independente de onda, velocidade 1x/2x/3x e fase. Só conta com jogo rodando (sem pausa do usuário/Core, sem modal, sem boss key, em Preparo/Onda). Abrir o app/nova partida arma um intervalo cheio. Ignorar/expirar/responder reinicia o relógio; 1 cartão por vez; respeita `BonusQuestions`. Relógio e sorteio injetáveis (`now`, `quizDraw`).
- Tutorial: o exemplo guiado continua logo após a 1ª onda (uma vez); depois vale a cadência por tempo.
- Core (`TdMatchState.Bonus.cs`): `AnswerBonus` vale em Preparo ou Onda em qualquer modo e não é mais 1 por onda (sem `AlreadyUsed`); crédito pela onda atual (Preparo: próxima). Estrutura do hash inalterada (`_bonusAnswered` continua no hash, só informativo); nenhum golden de hash mudou.
- Fonte: enunciado 15 DIP, opções 14 DIP, largura 360 DIP, altura cresce com as linhas (via escala do monitor). Sem ajuste em settings (não há padrão de tamanho de fonte); tamanho maior fixo.
- Goldens regenerados e revisados: `td-tut-quiz*-{100,150}.png` (cartão do tutorial legível; demais cenas só HUD com crédito atualizado).
