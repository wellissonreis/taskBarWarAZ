# MTC-T6.11: Missões, Migrar, banners, caixa e card do chefe ligados na raiz

> Formato: SDD-17 RF-17.6. **Resultado em branco** até a execução pelo usuário em desktop isolado. Fonte: seção "Itens MTC" do REPORT T6.11b (`/home/wellissonreis/taskBarWarAZ-reports/T6.11b.md`). Ficha: PLANO T6.11 (SDD-24, SDD-26; CA-26.9, CA-24.7, CA-24.8).

| ID | passos | esperado (fonte) | resultado (✔/✘) | evidência | data | build |
|---|---|---|---|---|---|---|
| T6.11b-1 | Painel das 4 abas: pelo widget, alternar Arsenal/Fases/Missões/Migrar | A mesma aba fecha; X fecha; rolagem pela roda e pela barra; badge do nav em Missões com concluídas; aba ativa com a barra âmbar; sem foco roubado (T6.11b #1) | | | | |
| T6.11b-2 | Cromo de Arsenal e Campanha | Título ARSENAL/CAMPANHA âmbar, filete, X e barra de rolagem iguais aos de Missões/Migrar (T6.11b #2) | | | | |
| T6.11b-3 | Arsenal: auto-escala, MELHORAR, clique numa torre da cena, ícone | Toggle da auto-escala age; MELHORAR gasta créditos; clique na torre abre o Arsenal rolado até a linha dela; ícone arma (borda menta) (T6.11b #3) | | | | |
| T6.11b-4 | Missões: COLETAR; reabrir o app; virada do dia (relógio do sistema) | COLETAR marca COLETADA, soma a recompensa e some o badge; reabrir mantém o dia e o progresso; a virada do dia coleta as concluídas (T6.11b #4) | | | | |
| T6.11b-5 | Migrar: 1º clique, 2º clique em ≤ 5 s, esperar > 5 s, trocar de aba, onda < 15 | 1º clique arma "CONFIRMAR"; o 2º em ≤ 5 s migra; passados 5 s volta; trocar de aba desarma; botão indisponível < onda 15 (T6.11b #5) | | | | |
| T6.11b-6 | Banner de pico | Banner âmbar com chip 0:30 e anel pulsante; clique vira "DEFESA ESCALADA ×3" em menta, chip a 60 s, e o clique não faz mais nada (T6.11b #6) | | | | |
| T6.11b-7 | Banner de chefe | Rosa, chip 0:45; clique abre o card de pergunta acima da faixa; fechar/minimizar esconde sem pausar; resultado fica ~20 s; com o bônus ativo, chip "×3 m:ss" no widget (T6.11b #7) | | | | |
| T6.11b-8 | Caixa de suprimentos | Aparece entre núcleo e portal piscando; clique credita e some. Conferir se `crate.X` é centro ou esquerda (T6.11b #8) | | | | |
| T6.11b-9 | Ausente | Banners e caixa somem; Presente volta (T6.11b #9) | | | | |
| T6.11b-10 | Oculto (Ctrl+Alt+H) | Hotspots e painel somem, 0 wakes dos coordenadores (T6.11b #10) | | | | |
