# Evidência TD.15: onboarding (tutorial, Como jogar, dicas de 1ª vez)

Sem testes interativos e sem abrir o App (o usuário estava jogando em C:\dev\sidewar). Verificação por testes puros e goldens.

## Goldens novos (revisados visualmente)
`tests/SideWar.Rendering.Tests/Golden/td-tut-{build,point,tower,wave,upgrade,upgradeinspector,ability,crate,help,tip}-{100,150}.png`
(balão com seta + destaque no HUD/popover/faixa; cartão Como jogar; dica de 1ª vez).
Regenerados (item "Como jogar" no menu): `td-ui-menu-100.png`, `td-ui-menu-150.png`. Demais goldens TD/idle intactos.

## Testes
- `TdOnboardingTests` (Runtime, 20): passos por ação real, destaque, pular, fase 1, fim da partida, hash/log intactos, Como jogar/Refazer, dicas.
- `TdOnboardingGoldenTests` (Rendering, 41), `SettingsTdTests` (+1), `MenuModelTests` (+1).

## Determinismo
Nenhum comando novo no log. `Tutorial_does_not_change_the_simulation_hash_or_the_command_log`: mesmo `StateHash` com e sem onboarding. Goldens de hash TD antigos = novos.
