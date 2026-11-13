# MTC-TA.10-Windows: widget compacto à direita

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário. Ficha: PLANO §8.1 TA.10 (parte Windows; CA-23.3, CA-23.4, CA-23.9, CA-23.10). Fonte dos pontos de ligação: REPORT TA.10 ("Pontos de ligação Windows (TA.10-Windows, pendentes)").
> **Pré-condição de código:** o REPORT TA.10 (WSL) deixa pendente: `DockController.Relayout` ainda usa `WidgetGeometry.NominalWidthDip` (480) em vez da largura medida (`WidgetRenderer.MeasureWidthDip`), redimensionar a janela quando a largura mudar, ordem de z com `SceneWindow`/`ZOrderCoordinator` e menu do widget seguindo a nova posição. Os goldens do widget ainda não existem (T5.6 pendente).

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| MTC-23.1-a | Comparar o widget lado a lado com o artefato de design em **100%** | Conteúdo coincide com o canvas: fontes, tamanhos, cores, larguras 70/52, gap 12, padding 0 6 0 12, botões 44×44, gap 2, barra de ativo 16×3, badge 14 DIP, fundo alfa 235 sem raio (SDD-23 CA-23.4; tabela MTC-23.1) | | | | |
| MTC-23.1-b | Idem em **150%** | Idem (CA-23.4) | | | | |
| MTC-23.1-c | Idem em **200%** | Idem (CA-23.4) | | | | |
| MTC-23.3-a | Em 1920 px de largura, medir o `left` e a altura do widget | Altura 48 DIP; `left = min(W − 548, W − 8 − largura)` (1372 em 1920 com a largura do canvas), `top` no topo da cena; com e sem o chip ×3 (SDD-23 CA-23.3, ADR-034) | | | | |
| MTC-23.3-b | Larguras de tela 1280, 1366 e 1920, escalas 100/125/150/175/200% | Widget cabe e fica à direita (SDD-23 CA-23.3 e CA-23.10; a tabela de T-23.3 cobre por cálculo, aqui é a conferência visual) | | | | |
| MTC-23.3-c | Barra de **40 DIP (Windows 10)** e **48 DIP (Windows 11)** | Geometria correta nos dois (tabela MTC-23.3). *Windows 10 não é suportado oficialmente (SDD-18 RF-18.7); executar só se houver imagem disponível* | | | | |
| MTC-23.3-d | Barra à esquerda/topo; ocultação automática; 2 monitores | Geometria correta nos hospedeiros `Taskbar` e `Band` (tabela MTC-23.3; CA-23.3). **(2 monitores)** | | | | |
| MTC-23.3-e | Mudar o texto do selo/chip ×3 (PRESENTE ×2,0 / AUSENTE ×0,25) e observar a janela | A janela acompanha a largura natural medida do widget (REPORT TA.10: recalcular por evento, debounce 200 ms); em mudança só de posição usa `SetWindowPos(NOSIZE\|NOACTIVATE)` | | | | |
| TA.10-f | Clicar nos 4 botões e no resto do widget, inclusive nos cantos | Todo pixel da caixa consome clique, sem cantos arredondados (CA-23.9; REPORT TA.10: `WidgetShape` sem raio) | | | | |
| TA.10-g | Passar o mouse sobre um botão | Hover de `hover-nav` a `.10` (alfa 26) (REPORT TA.10). *O REPORT aponta SDD-20 linha 149 ainda dizendo `.08`; se a execução mostrar divergência, anotar, pois o código segue SDD-23/ADR-034* | | | | |
