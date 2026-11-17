# TD.23 — Perfil TD v3 e selos de créditos iniciais

Implementação de 2026-11-17, limitada ao Core, Persistence/Profile e testes correspondentes.

## Migração e economia dos selos

O briefing da TD.23 pede **reembolso** dos nós obsoletos. Isso prevalece sobre a conversão automática de IDs originalmente proposta no §15 do SDD-29: removem-se `infrastructure.sell75`, `infrastructure.slots1` e `infrastructure.slots2` das compras. O total cumulativo `SealsEarned` não muda; como `SealsSpent` é derivado das compras, os custos 2/3/4 ficam integralmente disponíveis. Nenhum substituto é comprado automaticamente e não há concessão repetida de selos.

A cadeia registrada é v1 → v2 → v3. Na migração e no saneamento normal, apenas IDs de nós presentes no conteúdo embarcado são mantidos; duplicatas e IDs desconhecidos são descartados. Campos de progresso, importação idle, coroa, medalhas, recordes, torres desbloqueadas e infinito são preservados pelo saneamento existente. Runas não entram no DTO nem no JSON escrito.

Exemplo coberto para v1 e v2: 30 selos ganhos; compras válidas de 1+1 e antigas de 2+3+4; saldo antigo 19 → saldo v3 28. Reabrir/salvar/reabrir mantém 28. Comprar os três novos nós volta a 19. Duplicata de compra antiga não gera reembolso extra.

| Nó novo | Nome | Custo em selos | Créditos iniciais |
|---|---|---:|---:|
| infrastructure.startingCredits1 | Créditos iniciais I | 2 | +40 |
| infrastructure.startingCredits2 | Créditos iniciais II | 3 | +40 |
| infrastructure.startingCredits3 | Créditos iniciais III | 4 | +40 |

O Core soma efeitos `startingCredits` dos nós comprados na criação da partida. IDs repetidos contam uma vez; IDs inexistentes não dão créditos; `Step` não repete o bônus. Os três novos somam +120; o nó existente `infrastructure.credits` mantém +10, totalizando +130 quando os quatro são possuídos. A página começa somente com o Núcleo, inclusive após migrar e comprar os novos selos. Testes verificam +0/+40/+80/+120 em todas as seis fases e hashes determinísticos.

## Hashes e bot

Mudou somente o golden StateHash do bot, pois usa o conteúdo embarcado e o hash inclui os bytes de `td-meta.json`:

| Golden TD | Antes | Depois |
|---|---:|---:|
| SanityBot Otimizado fase 3 | 2514828015037595496 | 932256803118187624 |
| Placement | 15168844986091215698 | 15168844986091215698 |
| BombCrate | 17027428272486969366 | 17027428272486969366 |
| Match | 4316735678008544025 | 4316735678008544025 |
| Combat | 1135319241239498339 | 1135319241239498339 |

ContentHash TD: `4cb08f4250caf4dbe9f2851519ec22fc0d9a67da175b1cd2320ff99aff08d801` → `4ceed5ba9eb2697f0ae5343de4c63da0e6438c0283cb1122397e6b5ee94ce7a4`.

Os quatro goldens de mecânica usam conteúdo congelado, preservado. Nenhuma mudança no TdSanityBot, nos planos, nas seeds, na curva ou nos critérios da TD.22; apenas a constante golden do teste foi atualizada. Goldens e código idle intactos. Nenhum PNG regenerado nesta ficha.

## Validação

- Build geral Release: `dotnet build SideWar.slnx -c Release --no-restore -m:1 -warnaserror -p:BuildInParallel=false -p:ContinuousIntegrationBuild=true`: **0 warnings, 0 erros**.
- Core Release: **474 passando, zero falhas/skips**, incluindo matriz de balanceamento da TD.22 e replays.
- Persistence Release: **135 passando, zero falhas, 1 skip preexistente** (KillLoopTests, fora de Profile).
- `git diff --check` e verificação do diff staged limpos.
- Rodadas intermediárias: três avisos CA1861 nos testes novos corrigidos; comparação do fixture de migração ajustada para a ordenação canônica de infinito; golden do bot atualizado após falha esperada. Uma tentativa concorrente de build teve colisão MSB3026; build final serial passou.

## Encaminhamento para TD.24b

A tela da árvore de selos deve usar os IDs, nomes, custos e efeitos acima e eliminar qualquer rótulo/ícone ligado a revenda ou posição extra dos nós antigos. As compras antigas ficam desmarcadas e o saldo reembolsado aparece disponível. Conferir textos, ícones e goldens da árvore; não confundir selos permanentes com runas por partida. Nenhum arquivo de UI foi editado pela TD.23; mudanças paralelas da TD.24a foram preservadas. Sem validação ao vivo/Windows executado nesta ficha.
