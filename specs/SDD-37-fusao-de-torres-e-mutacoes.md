# SDD-37: Fusão de torres e mutações de inimigos

**Status:** PROPOSTA, somente especificação · **Etapa:** ciclo TD · **Depende de:** SDD-28, SDD-29 §20, SDD-31, SDD-33 quando implementada, Core TD e conteúdo versionado · **Fichas:** TD.37a–e (§10). Valores iniciais propostos, não medidos; sujeitos a playtest.

## 1. Objetivo e contexto

Permitir colocar uma segunda torre sobre a primeira e formar uma unidade híbrida; combinar traços de inimigos ao longo de uma corrida infinita, de modo que uma defesa confortável volte a exigir decisões. SideWar fica no canto da tela enquanto a pessoa trabalha: decisões espaçadas, leitura breve, ritmo lento, dificuldade real, sem exigir vigilância constante. Não acelerar intervalos ou aumentar quantidade de ondas como efeito desta ficha.

O Core atual tem 15 definições em `td-towers.json`, posições livres, construção com `BuildTower`, movimento e venda, teto técnico de 48 entidades, preço por tipo com tabela e arredondamento metade para o par, e venda limitada ao preço pago. Uma torre pode ficar sem posição após mudança de layout. A corrida persistente percorre seis definições de fase e continua com número absoluto crescente. O despertar de runas vigente usa fragmentos; partes antigas da SDD-29 descrevem uma versão substituída. SDD-33 é proposta: suas bênçãos e respiro são condicionais à sua existência, não pressupostos do Core atual. SDD-12 contém regras históricas substituídas; prevalecem Core e SDD-28/29 vigentes.

**Fora:** implementar código ou interface nesta ficha, terceira componente, fusão de inimigos entre si, rearranjar fases ou mudar os valores atuais de combate sem calibração, alterar o jogo em função de relógio real ou atenção do usuário.

## 2. Modelo e construção da fusão

**RF-37.1 — Duas componentes.** Uma torre colocada é a **base** A; selecionar B e confirmar sobre A cria um híbrido ordenado A+B, na mesma posição, com o mesmo ID e estado de desativação da base. A define chassi, mira, alcance, cadência, padrão e restrições de alvo; B fornece o módulo do §3. Inverter A e B pode mudar o resultado: Gelo+Raio é gelo elétrico e Canhão+Raio é canhão elétrico. Uma entidade, um ícone, até duas componentes; não recebe uma terceira. A+B de mesmo tipo é permitido e usa a regra de mesmo papel; não cria duas torres empilhadas independentes.

**RF-37.2 — Comando e atomicidade.** Introduzir `FuseTower(baseId, componentTypeId)` explícito no log; o gesto de construir sobre a torre resolve para esse comando, nunca por coincidência de coordenadas. Só em Preparo ou Onda, inclusive pausa de menu, se base existe, está posicionada, ainda é simples, B e fusão estão liberadas, há créditos e limites permitem. Validar tudo antes de alterar créditos, contagens, RNG ou estado; rejeição informa motivo e não altera nada. Um alvo ocupado e não elegível não vira construção em local vizinho. Fusão mantém alvo selecionado quando válido, tempo de desativação e recarga atual; não dispara ataque gratuito nem reinicia a espera de B. Para ações futuras de B, iniciar seu temporizador completo na fusão. Movimento, venda, desaparecimento de zona e recolocação operam sobre a unidade inteira; sem posição não age nem produz aura/renda.

**RF-37.3 — Economia e limites.** Custo de B = `NextTowerCost(B)` imediatamente antes de adicionar a componente, sem taxa extra; desconto e arredondamento seguem as regras vigentes. Contagem por tipo inclui cada componente de todas as entidades, posicionadas ou não; A+A conta duas vezes. Cada híbrido ocupa uma entidade do limite técnico de 48, mas uma fusão em entidade existente é permitida mesmo com 48 entidades. Limites por tipo contam componentes: no máximo duas componentes Cofre no total, inclusive em híbridos e sem posição; A+A não contorna o teto. Fusão nunca consome uma torre já construída em outro local: compra B por crédito.

Guardar preço pago de A e B separadamente. Venda é indivisível: somar, para cada componente, `min(preço pago, SellValueAt(tipo, contagem antes da remoção))`, calculando em ordem fixa base depois módulo e decrementando a contagem virtual entre as duas; então remover ambas e seus efeitos. Usar a taxa e teto vigentes; não há reembolso extra nem venda parcial. Ex.: A+A usa contagens n e n−1, impedindo reembolso duplicado da mesma posição da tabela. Venda e fusão devem ser transações atômicas e sem lucro por ciclo compra/venda.

**RF-37.4 — Liberação.** Proposta inicial: fusão a partir da fase absoluta 3 da corrida, ou ao iniciar partida em fase 3 ou superior; além disso ambas as definições precisam estar liberadas pelas regras existentes (`UnlockedTowerStage`), inclusive limites do perfil. A liberação é permanente somente durante a corrida; reinício obedece à fase inicial/perfil. Antes disso, indicar discretamente “Fusão na fase 3”. O desbloqueio absoluto não volta para 1 ao iniciar outro ciclo.

## 3. Matriz de combinações e efeitos

A tabela contém pares-chave **ordenados**; parâmetros abaixo são pontos de partida ajustáveis no conteúdo, não medições. Dano, área, efeitos e frequências são do ataque da base, salvo indicação; a componente B não executa um segundo ataque completo. Controle usa as imunidades, tetos e duração existentes; sem acumular lentidão ou marca repetidamente além dos limites atuais. Alvo voador só é atingido se a base já o atinge; efeitos de área de solo continuam de solo.

| Base + módulo | Nome | Efeito inicial por ação elegível |
|---|---|---|
| Gelo + Raio | Gelo elétrico | tiro de Gelo mantém lentidão e adiciona dano elétrico igual a 35% do dano base do Raio |
| Raio + Gelo | Raio gélido | tiro de Raio aplica lentidão do Gelo por sua duração vigente, além do dano de Raio |
| Canhão + Raio | Canhão elétrico | impacto de Canhão adiciona 35% do dano base do Raio ao alvo principal, não ao splash; continua só solo |
| Raio + Canhão | Raio explosivo | impacto de Raio causa splash de solo com raio e fator do Canhão; alvo principal recebe dano uma vez |
| Raio + Cosmos | Raio encadeado | um salto adicional para alvo elegível no raio de cadeia Cosmos, 40% do dano do tiro; desempate por distância e ID |
| Canhão + Policy | Canhão de ruptura | impacto aplica corrosão de 25% da armadura por 3 s ao alvo principal, sem somar instâncias |
| Balanceador + Gelo | Bloqueio gelado | nó do Balanceador aplica lentidão de Gelo aos inimigos de solo retidos, respeitando limites |
| Scale Set + Raio | Aura elétrica | mantém aura; a cada 3 s atinge um alvo elegível no alcance com 35% do dano base de Raio |
| Cofre + Gelo | Cofre gélido | mantém renda do Cofre; a cada 3 s aplica lentidão de Gelo a um alvo elegível no alcance de Gelo |

**RF-37.5 — Regra genérica para todos os demais pares.** IDs e papéis vêm do catálogo atual: ataque (`bolt`, `cannon`, `functions`, `cdn`, `cosmos`, `pipeline`); controle (`frost`, `balancer`, `policy`, `sentinel`, `queue`); suporte (`scaleset`, `backup`, `aks`); economia (`vault`). O papel é o da tabela, inclusive AKS como suporte. Primeiro aplicar a linha específica acima; senão, conservar integralmente o comportamento da base e acrescentar **um** módulo conforme B:

| Papel de B | Módulo genérico |
|---|---|
| Ataque | a cada 3 s, um disparo independente de 35% do dano base de B; alcance e restrição de alvos de B; sem rajada, cadeia, splash, zona cega ou efeitos adicionais de B. Se B não tiver dano positivo, não é elegível |
| Controle | a cada 3 s, um alvo elegível no alcance de B recebe lentidão de 15% por 2 s; usa restrição de voo de B quando for solo, demais atingem ambos; não copia nós, marca, empurrão ou remoção de armadura |
| Suporte | aura de +5% de dano a outras entidades posicionadas no alcance de B, sem autoaura; não copia cura, pods ou reativação |
| Economia | no fim da onda, com híbrido posicionado e ativo, recebe 50% da renda base de Cofre, arredondada para baixo, sem bônus sem vazamento |

Para B igual a A vale a mesma tabela genérica: uma componente repetida não duplica o comportamento completo. Para módulo genérico de disparo/controle, escolher alvo segundo mira da base se disponível, senão primeiro; empates por ID, sem alvo não consome recarga. A ação de módulo usa temporizador separado e não altera recarga da base. O módulo genérico de suporte acumula com outras fontes segundo os tetos de aura existentes, nunca consigo mesmo. O híbrido Cofre base mantém renda integral e o módulo Cofre adiciona apenas a meia renda acima; ambos contam para o limite de dois. A identidade visual e o tooltip mostram exatamente o módulo e suas restrições, não prometem o conjunto completo da segunda torre.

**RF-37.6 — Runas, bênçãos e aura.** Efeitos globais de alcance aplicam uma vez a cada alcance usado. Runas de tipo aplicam ao efeito da componente correspondente: dano do Raio somente na parcela Raio, lentidão do Gelo somente no efeito Gelo; não se aplica o mesmo canal duas vezes ao mesmo cálculo, mesmo em A+A. Efeitos que dependem de ID, contagem ou evento devem consultar componentes em vez de somente `DefIndex`, sem conceder dois disparos, nós ou pagamentos além dos especificados. Se SDD-33 estiver ativa, seus canais e tetos agregados aplicam-se por efeito uma vez; a bênção `vault` de renda de onda mantém seu escopo próprio e não multiplica indevidamente o pagamento da torre Cofre. Scale Set como base fornece sua aura normal a outras entidades; como módulo usa apenas o efeito genérico, salvo par nomeado. Uma fonte nunca beneficia a si própria; demais regras de sobreposição e desativação atuais permanecem. Resolver componentes, bônus, armadura/escudo e arredondamentos em ordem fixa documentada no conteúdo.

## 4. Mutações combináveis

**RF-37.7 — Representação.** Cada spawn mantém definição base e conjunto canônico de traços de mutação, além dos traços nativos. O traço nativo já presente não aplica seu multiplicador de novo. Catálogo inicial: `fast` (velocidade ×1,5), `durable` (HP ×1,7), `flying` (voa e ignora bloqueio/ataques só solo), `slow` (velocidade ×0,7, rótulo “Lento”), `shielded` (escudo inicial = 35% do HP máximo escalado), `armored` (armadura +12). Multiplicadores são sobre os valores escalados da base, na ordem HP, velocidade, armadura, escudo; `fast` e `slow` são mutuamente exclusivos. Velocidade é limitada a 15–120 unidades/s após os fatores; HP, recompensa e dano ao núcleo seguem limites numéricos do Core. `slow` é característica de nascimento, diferente de lentidão de Gelo, aplicada depois com os tetos atuais. Escudo usa a mecânica existente; se base já possui escudo, não acrescentar outro.

Exemplos: `tank` com `fast` = **Tank Rápido** (HP nativo de tank, velocidade ×1,5); `flyer` com `durable` e `slow` = **Voador Tank Lento** (HP ×1,7, velocidade ×0,7, continua voador). Não adicionar `durable` novamente ao tank nem `flying` ao flyer. Um inimigo pode ter até dois traços **adicionados** além dos nativos; catálogo validado evita duplicatas, exclusão fast+slow e combinações sem efeito. Chefes e réplicas mantêm comportamento atual sem mutações nesta primeira versão; não criar novas cópias nem alterar mecânicas especiais por mutação.

**RF-37.8 — Crescimento gradual e espaçado.** A dificuldade afrouxar é entendida como momento para introduzir variedade ao longo da corrida; não detectar atenção, FPS nem usar tempo real. Contar `G` como ondas concluídas na corrida (SDD-33 se existir; senão contador equivalente). Na montagem de cada onda, capturar G e fase absoluta; definir elegibilidade de mutações por esses valores, sem reduzir o nível após troca de fase/ciclo. Proposta inicial: antes da fase 3 ou G<20: nenhuma; fase ≥3 e G≥20: até um traço em até 10% dos não chefes; fase ≥5 e G≥45: até 20%, com no máximo um de dois traços por onda; fase ≥7 e G≥80: até 30%, no máximo 25% dos mutados com dois traços. Um limite percentual usa piso, mas quando houver pelo menos 5 inimigos elegíveis e a mutação estiver habilitada, permitir um mutado; limite de dois traços usa piso, permitindo um a partir de 4 mutados. Pelo menos 70% permanecem sem mutação adicionada. Não inserir mutação em onda `rest` ou respiro pós-chefe quando esse recurso existir; na dúvida sobre sobreposição de ondas, capturar a decisão para cada agenda no início. Onda pequena, incompatibilidade ou falta de candidatos pode gerar menos mutados; nunca compensar em ondas futuras.

Elegibilidade por traço: `fast`/`slow`/`durable` fase ≥3; `flying`/`shielded` fase ≥4; `armored` fase ≥5. Para não criar salto brusco, primeira ocorrência de cada traço é isolada, e só a partir de cinco ondas concluídas após sua estreia pode integrar dupla; guardar estreia por traço na corrida. Primeiro anúncio precede o primeiro spawn. Selecionar candidatos e traços de forma determinística a partir de listas ordenadas por índice do spawn e ID de traço; distribuir os escolhidos ao longo da agenda, sem modificar quantidade, intervalo ou inimigo base. Não usar rerolls ilimitados: filtrar compatibilidade antes da escolha. Recompensa de abate inicial ×1,15 por traço adicionado, limitada a ×1,3, arredondada para baixo; dano ao núcleo não muda. Os fatores são candidatos de balanceamento, não promessa de dificuldade medida.

**RF-37.9 — Matriz e interação.** `strong`/`weak` no catálogo são orientação; não há multiplicador genérico automático no combate atual. Mostrar união dos traços nativos e adicionados para consulta e avisos; não multiplicar vantagens por cada etiqueta. Mecânicas reais são aplicadas uma vez: voo veta alvos de solo e nós; escudo absorve conforme Core; armadura usa cálculo atual, inclusive Policy/corrosão; lentidão e imunidades seguem limites existentes. Um Canhão continua sem atingir voador mesmo se forte contra outro traço. Se um tipo estiver nas duas listas, mostrar “misto” e detalhar por efeito, sem bônus escondido. A eventual implementação de matriz numérica requer ficha e calibração próprias.

## 5. Interface e legibilidade

**RF-37.10 — Fusão.** Ao escolher uma torre no painel existente, apontar para uma torre elegível destaca seu contorno e abre preview discreto: `Base + Módulo → nome`, custo exato, efeito, restrição de alvo, renda e impacto no limite; confirmar explicitamente envia o comando. Clique normal de inspeção continua acessível; cancelar não gasta nada. Para inelegível, motivo legível (fase, créditos, tipo, terceiro componente, sem posição ou limite de Cofre). Se estado mudar antes de confirmar, Core revalida e UI atualiza custo ou explica rejeição. Inspetor mostra componentes, valor de venda total, mira e efeitos reais. Ícone composto combina símbolo principal da base com pequeno selo da componente, acompanhado de nome/tooltip e não apenas cor; acessível a teclado e tecnologias assistivas existentes. Sem duplicar hotspots, com layout/DPI da aplicação.

**RF-37.11 — Aviso de onda.** Na prévia da próxima onda mostrar tipos e traços previstos, por exemplo `Tank Rápido · Voador Tank Lento`, quantidade e ícones com texto acessível. Para início automático ou ondas sobrepostas, aviso discreto no chip antes do primeiro spawn, sem modal, som obrigatório, animação chamativa ou pausa; se a agenda só nasce no início, o início deve expor a agenda e garantir pelo menos 2 s de mundo até o primeiro spawn mutado, sem acelerar os demais. A prévia e a agenda efetiva devem ser idênticas, inclusive após alterações de layout e pausa; chamadas antecipadas mostram o aviso da onda correspondente. A ausência de prévia não deve mudar RNG nem estado. Rótulos persistem no inspetor do inimigo, sem poluir a faixa.

## 6. Determinismo, conteúdo e compatibilidade

**RF-37.12.** Conteúdo versionado descreve papéis, pares nomeados, módulos, fatores, progressão e compatibilidade; validar IDs, limites, pares únicos, traços incompatíveis e parâmetros positivos. Incluir dados no `ContentHash`. Agenda de cada onda guarda traços escolhidos por spawn, inclusive para ondas simultâneas; inimigos guardam traços e estado relevante, inclusive escudo mutado. A decisão usa RNG derivado independente da seed da partida e identificador absoluto da onda, com algoritmo e ordem fixos; não consome RNG principal nem é refeita por UI. Guardar no estado/hash qualquer estado do gerador que sobreviva à montagem e estreias, contador G, agenda, componentes, preços, temporizadores e efeitos não deriváveis; serializar ordem canônica, inclusive casos com escudo adicionado a base sem escudo nativo. Comando rejeitado não muda RNG/hash. Eventos visuais ficam fora do hash.

Versionar formato de replay e conteúdo: replay novo registra opções, seed, comandos e versão/hash de conteúdo; replay antigo usa caminho e conteúdo antigos ou falha com incompatibilidade explícita, nunca reproduz silenciosamente sob regras novas. Com recurso desabilitado e conteúdo histórico correspondente, preservar hashes antigos. Empates são resolvidos por ID estável e operações numéricas usam arredondamento fixo da simulação, sem relógio real.

## 7. Balanceamento e ritmo

Os fatores são hipóteses iniciais: módulo de ataque 35%, suporte 5%, mutações graduais e teto de 30%. Medir pelo menos 20 seeds, por fase e por perfil simples e otimizado, em corrida longa, com e sem fusão e mutações; registrar mediana e dispersão de ondas concluídas, tempo de combate por fase, créditos, vazamentos, diversidade de construções, frequência de ações por 10 minutos e distribuição de mortes. Comparar cenários isolados e combinados com as mesmas seeds, inclusive com bênçãos/respiro quando disponíveis. Investigar custo crescente, multiplicador de ciclo e limite de inimigos se houver muro ou facilidade persistente. Meta qualitativa: progressão mais espaçada e jogo difícil com tempo para reagir; não aprovar pela duração isolada nem ajustar secretamente a favor/contra o jogador. Ajustar catálogo e agenda em TD.37e com evidências; não mudar intervalo de onda nem HP global nesta proposta.

## 8. Critérios de aceitação

- **CA-37.1:** construir sobre base e confirmar cria exatamente uma entidade de duas componentes; inversão produz resultados da tabela; terceiros componentes e transações inválidas são rejeitados atomicamente.
- **CA-37.2:** contagens, preços e venda inclusive A+A e duas componentes Cofre respeitam limites, preço pago e regras com 48 entidades; venda não gera arbitragem; mudança de layout e reposicionamento preservam o híbrido.
- **CA-37.3:** cada par nomeado e cada papel genérico aplica apenas os efeitos previstos, restrições de solo/voo, cooldowns, runas, bênçãos disponíveis e aura sem duplicação ou autoaura.
- **CA-37.4:** Tank Rápido e Voador Tank Lento existem com atributos, escudo/armadura e restrições conforme §4; traços incompatíveis, duplicados, chefes e réplicas são tratados conforme regras.
- **CA-37.5:** agenda em fases e G de fronteira, ciclos, respiro, ondas pequenas e sobrepostas observa tetos, estreias e liberação; não altera quantidade nem acelera spawn.
- **CA-37.6:** preview e aviso batem com o que será executado; operação por teclado, motivo de rejeição, ícone e tooltip são legíveis e discretos.
- **CA-37.7:** mesmas seed, conteúdo e comandos geram agendas e `StateHash` iguais; replay cobre fusão, venda, mutação com escudo e simultaneidade; incompatibilidade antiga é explícita e RNG principal permanece estável.
- **CA-37.8:** TD.37e publica resultados e parâmetros efetivos com as medições de §7; sem dados, valores permanecem marcados não medidos.

## 9. Riscos e decisões em aberto

| ID | Questão | Recomendação |
|---|---|---|
| DP-37.1 | Ordem da fusão e mesmo tipo | **Ordenada e A+A permitido**: base dita comportamento, módulo traz efeito limitado; preview torna diferença explícita. |
| DP-37.2 | Liberação e pacing | **Fase absoluta 3 e marcos de §4**, calibrar com duração real; não acelerar ondas. |
| DP-37.3 | Acúmulo com Cofre e aura | **Contar componentes, máximo dois Cofres e sem autoaura**; pagar renda do módulo apenas conforme §3. |
| DP-37.4 | Geração de mutações quando fica fácil | **Progressão determinística por fase/G**, sem leitura de atenção ou adaptação invisível; medir antes de cogitar detector de facilidade. |
| DP-37.5 | Força de inimigos e híbridos | **Começar pelos fatores propostos**, testar 20 seeds e ajustar antes de assumir equilíbrio. |
| DP-37.6 | Bênçãos SDD-33 | **Integrar apenas se disponíveis**, preservando escopo e tetos; não bloquear fusão por ficha ainda proposta. |
| DP-37.7 | Compatibilidade de replay | **Versionar conteúdo e reprodutor**, manter caminho antigo ou erro explícito; não prometer mesmo hash com conteúdo novo. |
| DP-37.8 | Mutações em chefes e réplicas | **Excluir inicialmente**, preservar mecânicas e orçamento da onda; avaliar depois com dados. |

## 10. Fichas de execução

| Ficha | Entrega | Depende de |
|---|---|---|
| **TD.37a — Core fusão** | Modelo de componentes, comando, economia/venda, limites, pares e módulos, runas/aura, conteúdo, hash/replay e validações; testes CA-37.1–3 e 7 | decisões desta SDD e conteúdo TD atual |
| **TD.37b — UI fusão** | Seleção sobre torre, confirmação, preview, custo, rejeição, ícone, tooltip, acessibilidade e evidências visuais; CA-37.6 | TD.37a |
| **TD.37c — Core mutações** | Catálogo, agenda determinística, traços, progressão/estreias, combate, hash/replay e testes CA-37.4–5 e 7 | decisões desta SDD |
| **TD.37d — UI/avisos mutações** | Prévia fiel, aviso discreto por onda, rótulos e acessibilidade; CA-37.6 | TD.37c |
| **TD.37e — Balanceamento** | Experimentos pareados de §7, métricas, evidência e proposta de parâmetros; CA-37.8 | TD.37a–d |
