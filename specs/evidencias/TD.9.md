# Evidência TD.9: sprites do design na faixa TD

- Renderer: `TdBandRenderer` (sprites de `assets/source/design` via atlas embutido, pré-escaladas por DPI, pixels inteiros), `TdUiRenderer` (ícones de torre no popover de construção e no inspetor).
- Mapa conteúdo → sprite: `src/SideWar.Rendering/Scene/TdArtMap.cs`, espelhado em `assets/source/design/MANIFEST.json` (`tdMapping`, `provisional`).
- Goldens regenerados (revisados a olho): `td-strip-*` (8), `td-ui-chooser/inspector/branches` (6). Cópias em `specs/evidencias/TD.9/golden-*.png`.
- Captura real no Windows: NÃO feita (o App do usuário estava rodando de C:\dev\sidewar; não foi encerrado). Só goldens.
- Alocação por frame: teste `Draw_DoesNotAllocateInSteadyState` (< 512 B/frame) passa.
