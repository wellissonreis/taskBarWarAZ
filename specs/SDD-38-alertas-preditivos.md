# SDD-38: Alertas preditivos com pausa

**Status:** especificação para TD.40 · **Depende de:** SDD-28, SDD-32 e SDD-33 · **Visão:** jogo discreto no cantinho, sem interromper o trabalho nem surpreender com derrota.

## 1. Objetivo e invariantes

Alertar por projeção simples, determinística e conservadora; explicar o motivo e sugerir uma ação. Não alterar economia, dano, velocidades, sorteio ou balanceamento. O analisador do Core apenas lê o estado, sem RNG, efeitos ou inclusão no hash; estado de apresentação e limites pertencem ao Runtime. Simulação e replay continuam determinísticos: pausas e retomadas usam os comandos registrados existentes; se uma autorização alterar a simulação, também deve ser um comando registrado. Nenhuma rede.

## 2. Catálogo inicial

Os limiares são heurísticas iniciais, não garantias de resultado. Avaliar por ordem de severidade e ordem fixa da tabela; mostrar motivo e ação.

| Tipo | Detecção determinística | Severidade | Mensagem / ação |
|---|---|---|---|
| Queda fatal | Antes de cada passo, soma conservadora do dano de inimigos que podem alcançar a borda nesse passo >= integridade; ver §3 | crítico | Sua torre vai cair. Construa ou use uma habilidade. |
| Núcleo em risco | Inimigo em campo com dano >= integridade e distância à borda <= velocidade × 5 s | crítico | Sua torre pode cair. Reforce a defesa. |
| Vazamento iminente | Inimigo a <= 3 s da borda, projetado à velocidade atual | aviso | Inimigo perto do núcleo. Intercepte-o. |
| Dano à torre | Inimigo a <= 8 s da borda | aviso | Nesse ritmo sua torre tomará dano. Reforce a defesa. |
| Poucas torres | Antes ou durante onda, menos de duas torres posicionadas | dica | Poucas torres para a onda. Construa mais. |
| Créditos parados | Créditos >= duas vezes o preço da torre disponível mais barata | dica | Créditos acumulados. Construa ou melhore sua defesa. |
| Voador sem antiaérea | Voador em campo e nenhuma torre posicionada capaz de atingi-lo segundo conteúdo | aviso | Voador sem defesa. Construa antiaérea. |
| Chefe chegando | Chefe em campo | aviso | Chefe chegando. Prepare habilidades. |
| Runa disponível | Fragmentos suficientes para despertar e fronteira não vazia | dica | Runa disponível. Desperte uma runa. |
| Habilidade parada | Habilidade liberada, pronta e com usos, enquanto há inimigos | dica | Habilidade pronta. Use se precisar. |
| Cofre/Backup | Recurso disponível e desativado, quando essa informação existir no modelo | dica | Cofre ou Backup parado. Confira sua defesa. |
| Phishing sem Sentinel | Phishing em campo e nenhuma Sentinel posicionada | aviso | Phishing sem Sentinel. Construa uma Sentinel. |

Críticos pausam automaticamente quando a opção está ligada; avisos e dicas não pausam. Sem dados suficientes, não emitir o tipo em vez de inventar risco. Nenhuma predição promete dano certo quando torres ainda podem atacar.

## 3. Proteção antes da derrota nas primeiras ondas

Nas três primeiras ondas da corrida, antes **de cada Step**, inclusive dentro de um lote de velocidade 2×/3×, considerar todos os inimigos vivos cuja posição menos `Speed / 30` alcança `LeakX`. Somar seus `CoreDamage` em inteiro saturado; se a soma >= integridade positiva, interromper **antes** do Step, mesmo que lentidão, ataque ou paralisia possam salvá-los. Ignorar reduções de velocidade é conservador e cobre vários vazamentos simultâneos já em campo. Replicadores mortos e divisões de chefe podem criar inimigos durante o próprio Step, antes de MoveEnemies: a implementação deve incluir conservadoramente esses novos inimigos na projeção ou interceptar o vazamento antes de qualquer dano fatal com um mecanismo que preserve o replay. A simples leitura da lista de inimigos antes do Step, isoladamente, não comprova a garantia. A pausa permanece indefinidamente sem interação, inclusive com reinício automático ligado; sem executar o Step não há derrota nem auto-reinício.

Cartão crítico: **“Sua torre vai cair”**, motivo e sugestão, **“Salvar (voltar ao jogo e construir/agir)”** e **“Deixar cair”**. Salvar libera a pausa de alerta, sem despausar uma pausa do usuário, e permite construir/agir; reavaliar antes de cada Step e voltar a pausar se o risco fatal persistir. Deixar cair autoriza passos até a derrota da partida corrente, sem reabrir o mesmo bloqueio; não força dano ou derrota e não autoriza partidas futuras. Só depois da derrota escolhida pode atuar o recomeço automático. Com **pausar automaticamente desligado**, preservar comportamento anterior, inclusive auto-recomeço. Desligar alertas visuais não deve desativar essa proteção enquanto a pausa automática estiver ligada.

A autorização é estado do host, fora do hash, caso não modifique regras do Core: o replay reproduz os mesmos passos e comandos efetivamente executados, sem reproduzir tempo de espera. Se for representada dentro do Core, registrar comando e estado no hash. Não usar apenas o fim de um lote de passos para inspecionar o risco.

### 3.1 Gatilho geométrico da pausa crítica (correção do usuário, 2026-11-18)

“A pausa no jogo é antes de perder, e quando o robô passa da torre mais perto do portal.” O gatilho principal da pausa crítica é **geométrico**; a simulação do passo fatal (acima) permanece como **rede de segurança**.

- **Linha de defesa:** X lógico da torre posicionada, ativa (sem desativação) e com alvos diferentes de “none” (ataque, controle ou bloqueio) mais próxima do portal (menor X). Torre vendida, desposicionada ou desativada deixa de contar e a referência é recalculada.
- **Sem torre de defesa posicionada:** a linha é a **distância de segurança** `LeakX + 240` unidades lógicas (20% do caminho) — `TdRiskAnalyzer.SafetyDistance`.
- **Disparo:** antes de cada Step, qualquer inimigo vivo com `X` estritamente menor que a linha, **voadores inclusos** (a torre não precisar alcançá-lo) e divisões/réplicas do Fork Bomb/Botnet, pausa com o alerta crítico **“Nesse ritmo sua torre vai cair.” / “Um inimigo passou da sua última torre. Construa ou use uma habilidade.”** (tipo `PassedLastTower`).
- **Salvar** reconhece os inimigos que já passaram (por id): só um inimigo **novo** passando da linha, ou o passo fatal, volta a pausar. **Deixar cair** autoriza o restante da partida corrente (nenhuma retenção, inclusive críticos comuns) até a derrota.
- Retenção do host: nenhum Step roda, o Frame não anda, sem expiração; não usa o `Pause` do Core (pausa do usuário, modal e boss key independentes). Decisões (`Held/Saved/LetFall`, com Frame) ficam no registro do host; o replay reproduz os mesmos Steps e comandos, sem tempo de espera. Não silenciável por tipo enquanto pausar automaticamente estiver ligado.
- A passagem de inimigo detectada **depois** do Step que o cruzou pausa antes do Step seguinte: nenhum Step adicional é executado com inimigo desreconhecido além da linha.

## 4. Cadência, interface e configurações

**RF-38.1 (TD.43):** os alertas de risco de derrota (`FatalDrop`, `PassedLastTower` e `CoreAtRisk`), incluindo suas pausas, aparecem apenas nas três primeiras ondas da corrida, contadas desde o início e atravessando fases; a partir da quarta são silenciados. Os alertas `ImminentLeak` e `TowerDamage` compartilham o máximo de três exibições por corrida e um cooldown comum de 60 segundos. Cada dica (`Tip`) aparece no máximo uma vez por corrida. A contagem fica no host durante a corrida e é zerada ao iniciar outra; o jogo atualmente não salva nem retoma uma corrida ativa ao fechar.

Cooldown por tipo de 60 segundos de jogo em execução, máximo de dois alertas não fatais por onda; nas três primeiras ondas o fatal independe de cooldown e limite. A troca de partida limpa contadores. Um único cartão no cantinho, legível em fonte >= 14 DIP e cores da Palette, mostra motivo e ação. Dicas e avisos oferecem **Continuar**; críticos comuns oferecem também **não pausar por este tipo**, persistido por tipo. A proteção fatal não pode ser suprimida por tipo enquanto pausa automática estiver ligada nas três primeiras ondas. Sem expiração de cartão fatal. Ao surgir, pulsar discretamente o indicador do cantinho e exibir badge; som baixo opcional. Não ativar janela, roubar foco, mover ponteiro ou enviar notificações push. Com boss key ativa, manter oculto e jamais despausar por mostrá-lo; alertas críticos continuam protegendo a partida.

Em Configurações, persistir no settings existente: **Alertas** (padrão ligado), **Pausar automaticamente** (ligado) e **Som** (desligado). Preserve configurações antigas sem perda. Pausas do usuário, boss key e modais são independentes: fechar o cartão só libera a pausa que ele próprio adquiriu. As demais continuam em vigor.

## 5. Verificação

Testar pureza e repetição da análise, limiares e empate, cooldown/limite e reset por partida, persistência/migração, pausa antes de dano fatal individual e simultâneo em velocidades variadas, ausência indefinida, Salvar e reavaliação, Deixar cair e replay, configurações desligadas, boss key e pausa anterior. Testar layout, fonte, cores e goldens do cartão sem alterar os goldens idle. Validar build Release sem warnings e suíte WSL completa.

## 6. Decisões em aberto e recomendações

- **Falsos positivos:** preferir proteção conservadora no limite fatal; ajustar alertas antecipados somente após observação.
- **Cooldown:** recomendar 60 s de jogo e dois por onda; fatal prioritário dentro das três primeiras ondas.
- **Som:** recomendar desligado por padrão; flash de barra de tarefas é opcional e não necessário para o primeiro corte.
- **Salvar:** recomendar reavaliar antes de cada passo, sem janela de graça que permita derrota por inércia.
- **Cofre/Backup:** recomendar detectar somente quando houver um sinal explícito no modelo, sem inferir por nome.
