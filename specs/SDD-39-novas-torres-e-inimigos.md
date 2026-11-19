# SDD-39: novas torres, inimigos e ritmo de desbloqueio

**Status:** decisões aprovadas em 2026-11-19; execução por fichas. Ficha TD.41, 2026-11-19. **Depende:** SDD-28, SDD-31 e SDD-37; prevalece o comportamento atual do Core onde documentos históricos divergem. Todos os números e custos abaixo são hipóteses não medidas, inclusive duração e estimativas de trabalho. Nenhum balanceamento foi validado em partida.

## 1. Diagnóstico: por que a fusão passa despercebida

A fusão TD.37a-d existe. `td-fusion.json` tem `minStage: 3`: a fase é absoluta na corrida; a base e o módulo também precisam estar liberados, a base posicionada e simples, e o jogador precisa pagar o módulo e respeitar limites. O módulo é comprado, não consome uma segunda torre já construída; a ordem base+módulo importa. Há nove pares nomeados e combinações genéricas por papel.

O acesso implementado passa pela aba Torres/inspetor de uma torre, botão **Fundir**, escolha do módulo, preview e confirmação. Antes da fase 3 o botão no inspetor diz “Fundir (fase 3)” e fica desabilitado, portanto é preciso abrir esse inspetor para descobri-lo. Ao liberar, existe uma dica no canto por 20 segundos, registrada como vista ao exibir, não ao executar uma fusão; ela pode ser perdida enquanto a pessoa trabalha, dispensada ou encoberta por outros estados. O cartão mostra custo, efeito e impedimentos. A SDD-37 também descreve escolher uma torre e apontar para outra como gesto; não há evidência neste fluxo implementado de que arrastar ou construir sobre outra torre execute fusão. Logo, a hipótese de pouca descobribilidade combina acesso tardio, entrada aninhada e dica passageira; não é prova de que todos os jogadores a percam.

### Tempo até a fase 3 com delay zero

A corrida contínua inicia a próxima onda quando a anterior **termina**, e a fase seguinte ao concluir a última; delay zero não elimina a agenda de spawns nem o tempo dos inimigos em campo. Na fase 1 há 10 ondas, na 2 há 12. Somando, em cada onda, `max(offset + (count − 1) × interval)` em `td-waves.json`, obtemos **261,3 s + 310,8 s = 572,1 s**, cerca de **9 min 32 s de tempo de mundo** até os últimos spawns, sob ondas sequenciais, sem chamadas antecipadas e ignorando jitter, quantização e limite de entidades. Depois de cada último spawn ainda é preciso eliminar ou deixar vazar os sobreviventes; preparação inicial, pausas, jogo em segundo plano e desempenho também acrescentam tempo de relógio. O jitter de 10% do intervalo e a quantização impedem tratar 9:32 como limite matemático exato; chamadas antecipadas podem sobrepor ondas e reduzir a soma. Em velocidade 1×, sem pausas, é uma aproximação otimista de relógio; outras velocidades alteram a conversão. Derrota antes da fase 3 significa nunca ver a liberação. Não houve medição nem simulação de uma partida real.

### Alternativas de descoberta e liberação

| Opção | Benefício | Custo/risco |
|---|---|---|
| Fase 1, após a primeira onda | Demonstra o diferencial cedo; usar dica com primeira torre e créditos suficientes | Mais conceitos no tutorial; fase 1 mais forte; exige calibrar economia e fluxo |
| **Fase 2, com chamada persistente** (recomendada) | Introduz depois das três torres básicas, antes de esperar 22 ondas | Continua dependendo de vencer a fase 1; recalibrar ondas e custo |
| Manter fase 3, com aviso persistente desde fase 1 | Menor mudança de equilíbrio e respeita DP-37.2 vigente | Não corrige espera longa; pode frustrar |

Em qualquer opção: mostrar “Fusão: fase N” na lista de torres e no inspetor; ao liberar, botão discreto “Experimentar fusão” que leva a uma base elegível e permanece disponível até a primeira confirmação bem-sucedida, com opção de dispensar. Explicar “compra um módulo; não junta duas torres existentes”, nome, custo e efeito. Oferecer também gesto opcional de selecionar um módulo e clicar numa base, com destaque e confirmação explícita; preservar inspeção normal, teclado e acessibilidade, nunca inferir fusão por coordenadas nem construir em posição vizinha ao rejeitar. Não presumir que esse gesto exista hoje. Evitar modal obrigatório ou pausa: o jogo acompanha trabalho.

## 2. Ritmo atual e curva proposta

Hoje `td-towers.json` libera 3/2/2/3/3/2 torres adicionais nas fases 1–6, respectivamente: Raio, Gelo, Canhão; Balanceador, Functions; Policy, Cosmos; Scale Set, CDN, Pipeline; Sentinel, Fila, Backup; Cofre, AKS. A fase 1 tem apenas Requisição e Ping nas ondas; fase 2 acrescenta Pacote blindado; fase 3 acrescenta Rajada, Tráfego pesado, Fork Bomb e Ransomware. Há 15 torres, 11 inimigos comuns e três chefes no catálogo total: a queixa é coerente com exposição inicial, não ausência de conteúdo total. `introStage` sozinho não cria spawns: é preciso alterar também `td-waves.json` e verificar perfil/liberação.

Curva aprovada, acumulados de torres disponíveis e inimigos comuns já apresentados; chefes separados. Estreias em grupo pequeno, com aviso antes, contra-ataque acessível na mesma fase e intervalo de aprendizado; conservar ondas de respiro e não acelerar ondas para atingir metas. Os nomes novos são definidos na seção 3.

| Fase | Torres disponíveis acumuladas | Novos inimigos comuns na agenda | Comuns acumulados / chefes introduzidos |
|---|---|---|---|
| 1 | 5: Raio, Gelo, Canhão, Functions, DNS | Requisição, Ping, Rajada, SYN Flood | 4 / 0 |
| 2 | 8: + Balanceador, Policy, Front Door | Pacote blindado, Bot aéreo, Scanner | 7 / 0 |
| 3 | 12: + Cosmos, Escudo, Scale Set, WAF | Tráfego pesado, Fork Bomb, Trojan, Slowloris | 11 / Ransomware |
| 4 | 15: + CDN, Pipeline, Event Grid | Verme, Pacote fragmentado | 13 / 0 |
| 5 | 19: + Sentinel, Fila, Backup, Bastion | Parasita, Phishing, Túnel DNS | 16 / Botnet |
| 6 | 22: + Cofre, AKS, Firewall | Carga criptografada | 17 / Zero-day |

São 7 torres e 6 inimigos propostos além dos atuais. A agenda deve respeitar a semântica dos tipos, não apenas seus nomes: por exemplo, voador exige Raio/Gelo/Functions e blindado exige Canhão ou Policy. A fase 1 tem créditos iniciais 220 e pode não conseguir comprar tudo; disponibilidade não significa compra garantida. A introdução de Rajada cedo precisa ser testada, pois seu HP atual é 150 apesar do nome. Antecipar Policy, Scale Set, Functions e outras altera decisões vigentes de SDD-31; foi aprovada na DP-39.2 e de calibração. Não prometer uma torre nova em cada onda. Revisar UI paginada, legibilidade e acessibilidade antes de aumentar o catálogo.

## 3. Catálogo proposto

Valores de custo e estatísticas são candidatos não medidos. **P** = pequeno (reuso de comportamento implementado, mas inclui cadastro, validação, assets, UI, testes e replay); **M** = médio (generalização ou novo efeito); **G** = grande (novo estado/sistema). Essas faixas são ordinais, não prazo. `kind`, `stats` e `traits` existentes não garantem que um ID novo herde comportamento: várias rotinas despacham por ID e exigem generalização explícita. Nenhuma linha é promessa de apenas editar JSON.

### 3.1 Sete torres

| ID / nome, fase proposta | Papel, números candidatos e limites | Forte / fraco | Viabilidade |
|---|---|---|---|
| `dns` DNS, 1 | Ataque, ambos; custo 75, alcance 125, dano 14, intervalo 0,7 s | Ping e básicos / armadura e chefes; alternativa leve ao Raio | P: disparo comum; integrar IDs e conteúdo |
| `frontdoor` Front Door, 2 | Controle solo; custo 110, alcance 90, um nó, dano 3/s, espera 6 s | rápidos / voadores; menos dano que Balanceador | P–M: reutilizar nós, parametrizar tratamento de ID |
| `shield` Escudo, 3 | Suporte; custo 135, alcance 110; bônus de dano de 8% às outras torres | duráveis / dreno; não ataca, não beneficia a si | P–M: generalizar aura Scale Set; apesar do nome não cria escudo defensivo |
| `waf` WAF, 3 | Ataque ambos; custo 125, alcance 100, dano 7, lentidão ×0,8, intervalo 1,2 s | rápidos / blindados e chefes resistentes a controle | P–M: generalizar Gelo, inclusive duração/empilhamento já existentes |
| `eventgrid` Event Grid, 4 | Ataque ambos; custo 145, alcance 105, dano 16, 2 saltos, queda 80%, intervalo 1,7 s | grupos / alvos isolados e armadura | P–M: generalizar encadeamento Cosmos |
| `bastion` Bastion, 5 | Suporte; custo 175, alcance 120, reativa uma torre drenada a cada 8 s; sem cura de núcleo | dreno / inimigos sem dreno; não ataca | M: separar reativação de cura de Backup e parametrizar |
| `firewall` Firewall, 6 | Ataque solo, custo 165, alcance 110, dano 80, área 35, intervalo 2,4 s | enxames e blindados / voadores e rápidos | P–M: generalizar projétil em área do Canhão |

Os protótipos deliberadamente combinam papéis existentes; aparência, alcance, ritmo e fraquezas devem distinguir cada um em playtest. Não introduzir novos recursos de escudo ou economia implícitos pelo nome. Adicionar cada tipo ao papel de fusão apropriado (`attack`, `control`, `support`), descrições e `strong`/`weak`, com ícone legível e localizável. Verificar se cada efeito é compatível com os parâmetros efetivamente suportados antes de fixar schema.

### 3.2 Seis inimigos

HP, velocidade, armadura e recompensa abaixo são pontos de partida; dano ao núcleo 1 salvo indicação. Definições por combinação de traços só valem depois de verificar compatibilidade, incluindo imunidade de chefe e mutações; valores não representam dificuldade medida.

| ID / nome, fase proposta | Comportamento proposto e candidatos | Contra-ataque e leitura | Viabilidade |
|---|---|---|---|
| `synflood` SYN Flood, 1 | Grupo de pacotes básicos: HP 35, velocidade 45, armadura 0, ouro 3 | Canhão e área; grupo telegrafado, vulnerável a dano em área | P: agenda e inimigo básico; limitar quantidade e recompensa |
| `scanner` Scanner, 2 | Rápido: HP 55, velocidade 78, armadura 0, ouro 7 | Gelo, Balanceador, Functions; silhueta distinta do Ping | P: comportamento básico rápido |
| `slowloris` Slowloris, 3 | Durável: HP 230, velocidade 22, armadura 2, ouro 15, dano 2 | Raio, Pipeline quando disponível; lento, não drena | P: stats e traço `durable` |
| `fragment` Pacote fragmentado, 4 | Divide ao morrer: HP 85, velocidade 40, ouro 9, cópias com 35% do HP | Cosmos/Canhão; mostrar divisão e respeitar teto | M: generalizar Fork por ID, cópias, contagem da onda e recompensa |
| `dnstunnel` Túnel DNS, 5 | Camuflagem: HP 80, velocidade 40, ouro 12, revelação 55 | Sentinel e torres que possam mirar após revelação; contorno visível | M: generalizar Phishing, confirmar detecção e descrição reais |
| `encrypted` Carga criptografada, 6 | Escudo 80, HP 100, velocidade 34, ouro 14, dano 2 | Pipeline e dano concentrado; barra de escudo visível | M: generalizar Trojan e testar absorção e estado |

Não somar ouro indevido por cópias; detalhar regra em execução. Trait é metadado e, em diversos casos, a mecânica é associada ao ID; validar construção de stats, alvo, movimento, spawns, dano e serialização individualmente.

### 3.3 Quatro pares nomeados adicionais

Pares **ordenados** e distintos dos nove existentes; candidatas ao vocabulário de efeitos `hitDamage`, `hitSlow`, `hitSplash`, `hitChain`, `hitCorrode`, `nodeSlow`, `periodicDamage`, `periodicSlow` já validado em `TdContentValidator`. Par nomeado substitui o efeito genérico do módulo conforme SDD-37; não acumular os dois. Preço normal do módulo; testar alvo, fonte de dano/alcance e runas antes de afirmar equivalência.

| Base + módulo → nome | Efeito candidato | Custo |
|---|---|---|
| DNS + Gelo → **Resolução gélida** | `hitSlow`: acerto de DNS desacelera conforme regra do módulo Gelo | P–M: validar suporte por ID e fonte do slow |
| DNS + Cosmos → **Consulta distribuída** | `hitChain`: um salto, 40% do dano, raio conforme efeito existente | P–M: validar alvo e raio |
| Firewall + Policy → **Muralha corrosiva** | `hitCorrode`: reduz 25% da armadura do alvo principal por 3 s, sem acúmulo | P–M: validar ocorrência do efeito no disparo em área |
| Front Door + Gelo → **Porta congelante** | `nodeSlow`: inimigo retido pelo nó recebe lentidão do módulo | M: confirmar que o gatilho de nó aceite base nova |

Pares ficam disponíveis apenas quando ambas as torres e fusão estiverem liberadas; nenhum cria terceiro componente. Se o efeito existente exigir IDs específicos, implementar suporte explícito ou escolher outro par após testes.

## 4. Decisões do usuário — DECIDIDAS em 2026-11-19

| DP | Decisão | Decisão aprovada |
|---|---|---|
| **DECIDIDA** DP-39.1 | Fusão na fase 1, 2 ou 3? | Fase 2 com convite persistente; qualquer mudança substitui explicitamente DP-37.2 |
| **DECIDIDA** DP-39.2 | Antecipar torres e inimigos existentes conforme tabela? | Sim, com telegráfo e contra-ataque; revisa marcos da SDD-31 |
| **DECIDIDA** DP-39.3 | Aceitar sete torres e seis inimigos ou selecionar um subconjunto? | Sete e seis em lotes; eliminar duplicações após playtest |
| **DECIDIDA** DP-39.4 | Gesto de módulo sobre base além do inspetor? | Sim, seleção e clique com confirmação; sem arrastar obrigatório |
| **DECIDIDA** DP-39.5 | Introduzir quatro pares e preservar combinações genéricas? | Sim, com preview de efeito e custo, mantendo genéricas |
| **DECIDIDA** DP-39.6 | Ritmo da primeira fase e carga de informação? | Quatro inimigos, cinco torres disponíveis, estreias espaçadas; ajustar após observação |

A TD.39a antecipa somente os tipos existentes; DNS, SYN Flood e os demais tipos novos chegam nas fichas TD.39c/d. Portanto os totais da tabela são o alvo final, não o resultado imediato da TD.39a.

## 5. Fichas de execução após decisão

| Ficha | Escopo e dependências | Aceite |
|---|---|---|
| TD.39a — Curva e dados | Aplicar DP-39.1–2 e 6: `minStage`, liberações e agendas, validar perfil, custos, telegráfo e cenários de campanha/corrida; preservar ritmo de spawn salvo decisão | Cada estreia ocorre como planejada, com defesa disponível; histórico e progresso existentes tratados; testes de conteúdo e fronteiras de fases |
| TD.39b — Descoberta de fusão | Após TD.39a e DP-39.4: indicação persistente, primeiro uso, gesto opcional, preview e acessibilidade | Jogador encontra e conclui fusão sem instrução externa; rejeições sem gasto, dica dispensável, inspeção e teclado preservados; evidência visual Windows e testes de fluxo |
| TD.39c — Torres e pares | Após DPs 3 e 5: generalizar efeitos por dados onde necessário, registrar papéis, pares, limites, custos, assets e textos | Testes de efeito, alvo, ordenação, fusão genérica/nomeada, venda e limites; IDs válidos, diferenças compreensíveis |
| TD.39d — Inimigos e ondas | Após TD.39a e 39c: inimigos aprovados, identidade visual, spawns, counters, cópias e interações com mutações | Estreias e avisos corretos; teste de escudo, camuflagem, divisão, teto, ouro e encerramento da onda; contra-ataques acessíveis |
| TD.39e — Compatibilidade e playtest | Após b–d: conteúdo/hash e replay, salvamentos e progressão, UI/DPI, orçamento de entidades; comparar ao baseline | Mesmo seed/conteúdo/comandos produz mesmo hash; incompatibilidade explícita, sem migração silenciosa; ao menos 20 seeds por cenário e perfis simples/otimizado, mais sessões humanas de descoberta; registrar distribuição de tempo à fase 3, taxa de descoberta/fusão, vazamentos, diversidade e créditos. Ajustes apenas com dados |

Testar também partidas com chamadas antecipadas, pausa, velocidades, modo clássico e corrida contínua; os dados e as métricas devem identificar modo e condições. Fusão precoce e torres adicionais mudam potência e economia: não assumir que a curva é justa sem medição. Mudanças de conteúdo afetam `ContentHash` e podem afetar RNG e replay; versionar ou rejeitar incompatibilidades explicitamente, conforme SDD-37. O redimensionamento independente do seletor de torres é uma necessidade relatada separada e deve ser coordenado com a UI, sem supor que esteja resolvido por esta ficha.
