# TD.19b — Pausa determinística no Core (SDD-29 §11, CA-29.8)

- `Pause`/`Resume` (TdCommands.cs); `CommandReason` ganhou `AlreadyPaused`, `NotPaused`, `PausedCommand` (anexados ao fim do enum).
- `Paused` entra no `StateHash` **só quando verdadeiro** → hash de partidas sem pausa inalterado (nenhum golden regenerado).
- `Step()` com `Paused` retorna sem avançar nada (nem `Frame`): inimigos, spawns, recargas, caixa-bomba, contagem de auto-início, ondas sobrepostas, tempo. Presença não depende de relógio.
- Recusados em pausa (`PausedCommand`): `StartWave`, `CallNextWave`, `UseAbility`, `CollectCrate`. Aceitos: construir/vender/mirar/`AnswerBonus`/`SetPresence`/`LayoutChanged`/`Resume`.
- Replay: `TdLogEntry(Frame, Command, Seq = 0)` ordenado por (Frame, Seq); runner para se pausado sem comando aplicável.
- Testes: `TdPauseTests` (9): máquina de estados, hash, política de comandos, congelamento (Preparo e Onda/caixa/habilidades), replay com pausas em 9 pontos + 40 pares = mesmo hash, comandos dentro da pausa (5 execuções), replay terminando pausado.
- Core.Tests: 461 verdes.
