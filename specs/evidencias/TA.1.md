# TA.1: spike de z-order e clique da cena sobre a barra (decide `towersInBar`)

Data: 2026-11-16. Worker claude2 (ficha TA.1). Base: main com alterações de outras fichas não commitadas em `src/`/`tests/` (não tocadas). Referências: SDD-21 DP-33.4, SDD-22 RF-22.1/RNF-22.11, MTC-21.13, MTC-22.11, `specs/mtc/MTC-TA.1.md`.

## 1. Recomendação

**Reprovado: manter `towersInBar = false` por padrão e usar o hospedeiro `Band`** (fallback de 28–32 DIP já aceito pelo usuário). A ficha TD.7 segue com o trilho de inimigos na **Band mínima**, não dentro da barra.

Motivos, em ordem de peso (números na seção 3):

1. **Iniciar e Pesquisa não têm cura por `SetWindowPos`.** Ao abri-los, `Shell_TrayWnd` passa para a banda de z 6 (`GetWindowBand`); a janela do spike fica na banda 1 e nunca a supera, por mais topmost que seja. As torres ficam cobertas durante todo o menu aberto (≥ 1,3 s, o que DP-33.4 já aceitava) **e por ≥ 1,5 s depois do Esc** (a barra só volta à banda 1 após o próximo clique/ativação de outro app, ~0,25 a 0,5 s depois dele). O limite de 150 ms é estourado em 6/6 repetições, em todas as variantes. A aceitação de DP-33.4 cobre "enquanto abertos", não o resíduo depois de fechar.
2. **A reafirmação literal da spec (gatilhos de foreground/REORDER/LOCATIONCHANGE com debounce de 100 ms) mede 140 a 179 ms** (mediana 140 a 156 ms) mesmo no caso fácil (clique em área vazia/ícone/rajada), ou seja, no limite e acima de 150 ms em parte das repetições.
3. **Cintilação:** mesmo com reafirmação imediata (borda de subida, `SetWindowPos` ~15 a 35 ms após o evento) a janela some por 14 a 34 ms (1 a 2 quadros a 60 Hz) a cada clique em que o foco vem de outro app. É uma piscada por interação, repetida em todo clique na barra; não consegui julgá-la a olho (ver limitações), mas a medida por pixel é inequívoca.
4. **Os gatilhos da DP-33.4 (a) são insuficientes:** (i) a seta da bandeja abre uma janela XAML do próprio explorer (`TopLevelWindowForOverflowXamlIsland`) que sobe a barra sem foreground em `Shell_TrayWnd` (A e B ficam cobertas 6/6); (ii) a volta da banda 6 para 1 não emite REORDER/LOCATIONCHANGE (nenhum evento recebido), então só um poll cura (variante P, 50 ms = ~20 `SetWindowPos`/s em vão enquanto a barra está na banda 6). Ou seja, a solução aprovável exigiria foreground global + poll contínuo, ainda sem resolver o ponto 1.
5. Subir de banda (UIAccess) está fora de questão: exige binário assinado em pasta confiável, e a banda de UIAccess é, ao que tudo indica, menor que a 6 (numeração não documentada; não testado).

O que **funcionou** e pode ser reaproveitado na Band/no widget (SDD-23 RF-23.10): reafirmação imediata + `SWP_NOACTIVATE` recupera em 38 a 86 ms nos cliques comuns; clique passante em Passivo e Interativo está correto (seção 3.4).

## 2. Metodologia

- **Programa:** `tools/spikes/TA1ZOrder/Spike.cs` (descartável, fora da `SideWar.slnx`; C# 5, compilado com o csc do .NET Framework 4 x64; ver README do spike). Não toca `src/`.
- **Janela:** layered topmost (`WS_EX_LAYERED|TOPMOST|TOOLWINDOW|NOACTIVATE`), do tamanho da barra (1920×48 px na tela principal, 100 % DPI), pintada com `UpdateLayeredWindow` (alfa pré-multiplicado): trilho laranja opaco de 14 px no topo, 3 torres opacas de 24 px (magenta, ciano, amarelo) em x = 1360/1440/1520 (zona livre entre o último ícone e a bandeja, medida por UIA), alfa 0 no resto. Passivo = `WS_EX_TRANSPARENT` na janela inteira; Interativo = sem ele (alfa 0 atravessa).
- **Reafirmação** (`SetWinEventHook` fora de processo na thread da janela): `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_REORDER`, `EVENT_OBJECT_LOCATIONCHANGE`; ação `SetWindowPos(HWND_TOPMOST, NOMOVE|NOSIZE|NOACTIVATE)`, só se a janela não estiver acima de `Shell_TrayWnd` (`GetWindow(GW_HWNDPREV)`). Variantes: **A** = gatilhos da spec (foreground em `Shell_TrayWnd`/`Shell_SecondaryTrayWnd`, REORDER/LOCATIONCHANGE da barra) com debounce *trailing* de 100 ms; **B** = idem com reafirmação imediata na borda de subida + trailing de 100 ms; **C** = B com qualquer mudança de foreground como gatilho; **P** = C + checagem de z-order a cada 50 ms; **N** = sem reafirmação (controle).
- **Entrada:** `SendInput` absoluto (mover, depois clique down+up num único `SendInput`). Alvos por UIA (`StartButton`, `SearchButton`, botão de ícones ocultos da bandeja, botão do Microsoft Teams, app de 1 janela, não elevado). Cursor, foco e o estado do Teams restaurados ao fim; nenhum clique destrutivo; Esc para fechar Iniciar/pesquisa/bandeja.
- **Estado inicial reprodutível:** antes de cada tentativa, clique numa janela auxiliar própria (outro app em primeiro plano, barra em banda 1), reafirmação forçada e espera por torres visíveis.
- **Medição (por tentativa, t0 = imediatamente antes do `SendInput` do clique):** laço sem pausa por 1,5 s (1,8 s no hover) amostrando (a) captura de pixels da tela (`BitBlt` do DC da tela com `CAPTUREBLT`, ou seja, a imagem composta pelo DWM) nos centros das 3 torres e no trilho, com tolerância de cor 40/255, e (b) z-order por `GetWindow`. Resultados: início da cobertura, **recuperação = primeira amostra visível depois da última amostra coberta** (∞ = não recuperou na janela), tempo coberto total, episódios de cobertura (cintilação), nº de `SetWindowPos`/gatilhos, banda de `Shell_TrayWnd` (`GetWindowBand`), janela em primeiro plano ao fim. Intervalo médio entre amostras 10 a 13 ms (máx 12 a 46 ms).
- **Repetições:** 6 por caso nas variantes A, B, C, P; 3 em N; passivo/interativo 6 cada. Casos: área vazia, ícone (1º clique ativa o app, 2º minimiza), Iniciar, pesquisa, seta da bandeja (cada um *aberto* e *depois do Esc*, e *depois de clicar em outro app*), hover e clique em miniatura, rajada de 20 cliques a 50 ms.

## 3. Resultados

Ambiente: Windows 11 Pro build 26300, 2 monitores (barra de 1920×48 px no principal), 100 % DPI. Valores em ms: recuperação **mediana / máx**; "cob." = maior tempo coberto; "n cob." = repetições (de 6) em que alguma amostra apareceu coberta. ∞ = não recuperou. Meta: ≤ 150 ms.

### 3.1 Cliques comuns (estado inicial limpo)

| caso | A (spec, debounce 100) | B (imediata) | C (+qualquer foreground) | P (C + poll 50 ms) | N (sem reafirmar) |
|---|---|---|---|---|---|
| clique em área vazia | 140 / 150, cob. 122 | 49 / 57, cob. 24 (4/6) | 56 / 58, cob. 21 | 38 / 46, cob. 14 (4/6) | ∞ (3/3) |
| clique em ícone (ativa app) | **156 / 173**, cob. 124 | 53 / 55, cob. 20 (3/6) | 57 / 64, cob. 21 (5/6) | 49 / 63, cob. 23 (4/6) | ∞ |
| 2º clique no ícone (minimiza) | 0 (0/6) | 0 (0/6) | 0 (0/6) | 0 (0/6) | ∞ (resíduo do anterior) |
| rajada de 20 cliques | **155 / 179**, cob. 133 | 75 / 86, cob. 34 | 58 / 70, cob. 17 (5/6) | 60 / 83, cob. 22 | ∞ |
| hover em miniatura | 0 (0/6) | 0 (0/6) | 0 (0/6) | 0 (0/6) | 0 |
| clique em miniatura | 88 / 125, cob. 60 (3/6) | 0 / 106, cob. 34 (1/6) | 0 / **179**, cob. 124 (2/6) | 0 / 138, cob. 53 (2/6) | 0 |
| bandeja, após Esc (fecha) | 141 / 146 | 33 / 41 | 0 / 44 | 0 / 29 | ∞ |
| bandeja, aberta (seta ocultos) | **∞ (6/6)** | **∞ (6/6)** | 64 / 65 | 51 / 67 | ∞ |

`SetWindowPos`: 1 por interação (B/C/P), inclusive 1 por rajada de 20 cliques (os demais caem na checagem "já está acima"); 1 por interação também em A. Em P, 20 a 25 chamadas por 1,5 s enquanto a barra está na banda 6 (inúteis).

### 3.2 Iniciar e Pesquisa (todas as variantes: A, B, C, P; 6/6)

| fase | resultado |
|---|---|
| menu aberto (1,5 s) | coberta o tempo todo (cob. 1,3 a 1,5 s; 2 episódios no Iniciar: reafirmação rápida e nova cobertura quando o menu abre = piscada dupla); barra na banda 6 |
| 1,5 s depois do Esc | **não recuperada (∞)** em A, B, C e P; foco já voltou a outro app (`TA1Focus`), barra segue na banda 6 |
| ao clicar em outro app depois disso | banda volta a 1 ~0,25 a 0,5 s depois do clique, **sem nenhum evento** de REORDER/LOCATIONCHANGE/foreground novo: A, B e C ficam cobertas (∞ em 1,25 s); só P (poll) recupera |

Nenhuma amostra do z-order via `GetWindow` revelou o problema: a janela aparece "acima" de `Shell_TrayWnd` na lista, mas a barra está noutra banda; só a captura de pixels e `GetWindowBand` mostram. (Teste `diag`, 2 repetições adicionais: `trayBand=6`, `oursBand=1`, `SetWindowPos` manual sem efeito.)

### 3.3 Controle N (sem reafirmação)

Qualquer clique que dê foreground à barra cobre as torres até o fim da janela (1,5 s): confirma o risco da DP-33.4 e que a reafirmação é o que recupera nos cliques comuns.

### 3.4 Clique passante (6 repetições cada)

| modo | alvo | resultado |
|---|---|---|
| Passivo | torre | 6/6 atravessou: `WindowFromPoint` = `Shell_TrayWnd`, 0 mensagens `WM_LBUTTONDOWN` na janela |
| Passivo | ícone sob a janela | 6/6 chegou ao Windows (Teams foi ativado) |
| Interativo | torre (alfa 255) | 6/6 recebido pela janela (`WindowFromPoint` = janela do spike) |
| Interativo | espaço livre (alfa 0) | 0/6 recebido; `WindowFromPoint` = barra |
| Interativo | ícone | 6/6 chegou ao Windows |

A janela nunca ativou (NOACTIVATE + `WM_MOUSEACTIVATE` = `MA_NOACTIVATE`).

## 4. Observações técnicas

- Reordenação por clique: só quando o foreground vem de **outro app** para a barra (clique em ícone que ativa o app, área vazia, bandeja). Se a barra já é a janela em primeiro plano, novos cliques não cobrem (a rajada de 20 cliques cobre **uma vez**, no 1º).
- O minimizar do app (2º clique no ícone) devolve o foco ao app anterior: sem cobertura.
- O hover de miniaturas não altera o z-order.
- Variante A fica 20 a 40 ms acima de 100 ms por causa do `SetTimer` (granularidade) e da latência do evento (15 a 50 ms); com borda de subida o gatilho age em ~2 ms após o evento.
- Bandas medidas por `GetWindowBand` (API não documentada): barra normal = 1; barra com Iniciar/pesquisa = 6; a janela do spike = 1.

## 5. Texto para DP-33.4 (c)

> Medido em TA.1 (2026-11-16, Windows 11 build 26300): com reafirmação imediata a janela volta em ≤ 86 ms nos cliques comuns, mas some 1 a 2 quadros por clique; Iniciar e Pesquisa movem `Shell_TrayWnd` para uma banda de z superior e as torres só voltam depois do próximo clique em outro app (≥ 1,5 s após fechar), e os gatilhos propostos não cobrem a seta da bandeja nem a volta da banda. **`towersInBar` fica `false` por padrão; hospedeiro `Band`.**

## 6. Limitações

- **Uma máquina, uma versão do Windows** (11 Pro build 26300, canal de pré-visualização), um tema, 1920×1080 a 100 % DPI, 2 monitores (só a barra do principal testada). Outra build ou DPI pode mudar tempos; a existência da banda 6 deve ser revalidada antes de qualquer decisão em sentido contrário.
- **Máquina em uso pelo usuário** durante a medição; o `SideWar.App` dele continuou rodando (janelas `SideWar.Scene` e `SideWar.Widget` acima da barra, sem cobrir a faixa de 48 px; não encerrado). O Chrome/VS Code dele podiam estar em primeiro plano, e o foco foi trocado para a janela auxiliar a cada tentativa. Uma execução inicial (variante A) abortou por exceção de índice no analisador e deixou o Teams restaurado; foi minimizado de volta ao final (estado original) e as execuções foram refeitas.
- **Resolução temporal ~10 ms** (captura de tela por `BitBlt` em laço sem pausa; máx de 12 a 46 ms entre amostras). Coberturas < 10 ms podem não aparecer ("n cob." subestima).
- **Cintilação não foi julgada visualmente nem filmada**: usada a proxy de pixels (episódios e tempo coberto). 14 a 34 ms de ausência = 1 a 2 quadros a 60 Hz.
- O app de teste foi o **Microsoft Teams** (1 janela, não elevado): o Gerenciador de Tarefas (elevado) bloqueia o `SendInput` (UIPI) e foi descartado; o Chrome (2 janelas) abriria a lista de miniaturas em vez de alternar.
- **Miniaturas:** a janela de miniaturas não foi localizada por classe no Windows 11 26300; o clique usou uma posição estimada 90 px acima do ícone (o clique ativou o Teams, então acertou uma miniatura, mas não há prova do alvo exato).
- A seta da bandeja foi o único item de bandeja clicado (relógio e ícones de sistema não, para não abrir painéis de configuração).
- Barra com ocultação automática, vertical/no topo, DPI misto, barra do monitor secundário e HDR não testados (DP-33.5 já envia esses casos ao `Band`).
- Variante P e a análise de banda surgiram durante o spike (não estavam na ficha) para entender a causa; os números de P são do mesmo conjunto de 6 repetições.
