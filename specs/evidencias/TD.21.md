# TD.21 — Core de runas

Implementado em 2026-11-17, sobre a TD.20. Sem executar, fechar ou reabrir o App do usuário.

## Entrega

- `BuyRune(string RuneId)` e `RefundRune(string RuneId)` em Preparo/Onda e também em pausa. Núcleo já possuído e indevolvível; sem gates de fase/setor. Rejeições atômicas para desconhecido, já comprado, não comprado, não adjacente e créditos insuficientes (`InsufficientCredits`, o motivo existente).
- Adjacência bidirecional, inclusive fronteira por qualquer lado. Devolução remove o nó e todos os comprados sem caminho até o Núcleo; preserva ciclos e caminhos alternativos. Devolve 100% dos custos fixos, sem tokens ou taxas.
- `OwnedRunes`/`OwnsRune`, bitset por ordem do JSON no StateHash. Modificadores inteiros derivados por setor/target fora do hash. Comandos novos usam o replay existente `(Frame, Seq)`; compra/devolução não avançam tempo ou RNG.
- Pequenos, notáveis e keystones dos seis setores ligados ao combate: dano/cadência/alcance/área, perfuração, quarto tiro dobrado, lentidão/paralisia/vulnerabilidade, fogo, nós/retensão/explosão/frente, auras e duração de Escalar, Diversidade, integridade, venda e renda. Efeitos valem para todas as torres do tipo e as construídas depois.
- Seis fronteiras: dano em lentos (Raio/Canhão), dano em retidos (Canhão), duração de nós com Scale Set, desconto de torre, dano de Raio sem vazamento. Retidos recalculados antes dos tiros, sem depender do cache do tick anterior. Histórico de vazamento entra no hash e não é apagado por cura.
- Cota folgada escolhe `towerCostTableLow`; Auto-orçamento desconta 5%; runas de venda somam pontos-base com teto de 85%. Metade-para-o-par nos custos; piso na venda, como TD.20.
- Fontes §6 ligadas ao `creditSources` já entregue na TD.19: início +80, onda 60+5w, onda sem vazamento 40+2w, caixa 40+2w, pergunta 40+3w, chefe morto +120, modificador de ouro por morte. Cada onda sobreposta guarda seu próprio sinal de vazamento no hash.
- Nenhuma alteração em Runtime/Rendering de produção. Bot sem mudanças além do golden, com os três skips `recalibra na TD.22` preservados.

## Semântica e ressalvas

- Lentidão pequena soma pontos-base à redução; Inverno usa o raio de controle existente de 50 unidades. Efeitos mantêm a ordem de dano/arredondamento existente. Duração base dos nós continua 6s.
- Compras/devoluções não reiniciam recargas, contadores de tiro, vidas de nós, efeitos ativos ou zonas em curso. Dano/retensão/explosão dos nós existentes são recalculados; duração já iniciada permanece. A integridade extra é reversível, mantendo o dano perdido; devolvê-la com apenas 1 de integridade causa derrota ao chegar a zero.
- **Inconsistência matemática do SDD preservada, sem reabrir decisão:** a análise antiarbitragem da §5.2 considera só o desconto de 5%, mas Cota folgada também reduz o preço. Com 10 Raios em campo, comprar o 11º com a tabela Low custa 557; devolver Cota e vender sob a normal rende `floor(838 × 0,70) = 586`, lucro 29. Esta ficha segue as fórmulas explícitas de custo vigente, venda fungível e devolução livre; não acrescenta taxa, trava ou preço histórico não autorizado. A alegação de ausência de arbitragem em toda troca de runas não é satisfeita por essas fórmulas; fica registrada para a revisão de economia/balanceamento da TD.22.
- Meta permanente e migração não alteradas (TD.23); modal/aba Runas não implementadas (TD.24a/b).

## Goldens StateHash (old → new)

| Replay | Old | New |
|---|---:|---:|
| Placement | 7658827911200447953 | 10643278674487539972 |
| BombCrate | 6138761065689756581 | 588953149544461392 |
| Match | 16774011634538135045 | 2075538275561993642 |
| Combat | 14218406681746317761 | 14724320659106336327 |
| SanityBot | 6447598150438456504 | 15227494432892406870 |

Motivos: bitset e histórico de vazamento no hash; renda §6 e bônus por onda perfeita. Mesmos logs/seeds repetidos cinco vezes; replay novo cobre comandos de runa durante pausa no mesmo frame. ContentHash permanece o da TD.20 (nenhum JSON alterado). Goldens idle não alterados.

## Goldens PNG TD

Regenerados apenas os testes `SideWar.Rendering.Tests.Golden.Td*` com `SIDEWAR_UPDATE_GOLDEN=1`: 181 casos. 20 PNGs mudaram. Revisão visual dos cenários de construção/tutorial, pergunta certa, preview de onda e vitória em 100%/150%: valores de renda/pontuação atualizados; estrutura e legibilidade preservadas.

SHA-256 old → new:

- `td-tut-build-100.png`: `574fad145335b2b0cb6754cfe17951608752a588f2c38626bbfaa3a0c5376619` → `a87d25d0d3c97663ee11b7f1a3034352124a995bcb82facb78c8032f28550252`
- `td-tut-build-150.png`: `c9894877bd0e2dcd278ac533457828dc5789279051c1d61d0d9a3fcc074196ad` → `ea9536c9eb4db7c51f099ba17580e0a43e73cdb920aca4fd5c190bbe87aa1651`
- `td-tut-core-100.png`: `72f13b0618d0412556a17e7e6b3da3b4f66a6f1a1f21cde45c07cb28e9b0bc6a` → `227e95466371de5f207df6831565eac261ecedefe780ca892a8b980472a59625`
- `td-tut-core-150.png`: `428fdabc7376971d67586a51cc03729e8a16323ebffaf75d2b33354e60dd566f` → `5238efc3186f1d8d17b46ee974b7e660c9564880e3c9f5204fd5dc3bb50dae36`
- `td-tut-credits-100.png`: `93a686ecc0f52043fa374c99df146dcc07eaad7650fb02f8b1aedbce97eeaf28` → `7eafc81fdce229232056d36bbb26b354eedb6c12624e57914f829cbafa04831b`
- `td-tut-credits-150.png`: `e10e7d86a738dc51c542a01a67e2d331799e1a9b340d19020b1435bc0301ccb1` → `bcb0c13f660a5a00596fe718450ad5c1a1c12deb51f00d7c4405282e306726d9`
- `td-tut-hudcollapse-100.png`: `2dd443ba5c67fe0016c40769c020002235b59f137b234c40fa1a3d49307802bc` → `29213d715dbbcc29fff71ce6d9de70ae6bd626628f8acc439cfd5244b8cc4bc3`
- `td-tut-hudcollapse-150.png`: `e40b32aa3102c5cfb1fb37f853fb05a95fb05331c38bfc660e5ca084d87829c8` → `bf2efa5e50e4f3bb64c5cda8c14127f2659bf303fbd6775ef46ee38073c1ed77`
- `td-tut-quiz-100.png`: `d3337390b370f9138c40b7bfca99c6e94d07c73654542e2790434157f90af6fb` → `06832c337e7a5cf98802071fb3419016db189a53e5caa5ead27a5f7a27c34ea7`
- `td-tut-quiz-150.png`: `a043839a74ad2935b8da7bf99ce6a34cc1e117d557f21a8861d0ce3949c5d5cf` → `eefa4ed0cfd241aa9dd5c1791318a344e4bf1b58e699fef1f024d9d3cf3c7aff`
- `td-tut-quizright-100.png`: `445dfeeffa03432c7f4b1f1f56a353f7b04c6877b49a942bc5d30d1eecbbdd48` → `861f8683d29660e0cb0b88adfc5eb244748b4c07008484d70a3d9e6099a49808`
- `td-tut-quizright-150.png`: `c03c90b70b86314a69eebe724be514b438906e853ff7a1ccf3d61ccaa7cf189a` → `5a9ba87a262c4670cd1a5bea6a65ac6175d28ae98c1f6939fc8edb164b799e19`
- `td-tut-tower-100.png`: `67bf66733df6c929b4dba340aa651f772f5ad8daba8a295818bb7f84ca156059` → `75198a0395b6165dcda2ec05d7fcba1f90450d810d072050ecc72fab71ef1e03`
- `td-tut-tower-150.png`: `bcb9d7af9bdfe710f2d6307d2698408fdd92ce03630bd0ffef615e92056d449b` → `bc735ca095d265cbe23469d13ffdac08f0f7c503952b30a64d89f63dedc70207`
- `td-tut-wave-100.png`: `75a7acbbd3a21bbc2d826da93fc9cdb4c86a2b97b0732a82c37b0895957e6f6f` → `3e1aced266853287c96c9101d906105ea7fa02c5a6f088e792494a334c2cbf97`
- `td-tut-wave-150.png`: `92c1c2aa64247bf537f6a656f93912f18375e67c0b4a7320a84c83bad4a86d31` → `5108731c5fe776a3080dd58ef0d6e1f6a6f5aafa8b9ba1596c204ee90474f7a2`
- `td-ui-preview-100.png`: `8774c5ba74e0f3bee90f861fa543351095a3bbbbe3bb8314b95249867cf23b8f` → `89c375d6c77a3eb9fa39927ff6457bbf3be27702a68b7bde7bfecd1bab87a4a6`
- `td-ui-preview-150.png`: `d6a1115f040581914f5f4c2a4f1958520d362076a9613bc31cb7b9005467b288` → `bf7ccada8fc54b18124360a66b29a19873c263b8305e26962be87b3c53a50826`
- `td-ui-victory-100.png`: `9408cda79dd78e0963481c97d413b5083eadf41a4669dda190814d55035a31ee` → `f062297408fcf2cf92e2e1b59832d5c57ea070c51662c3ab14509b6ad47155f6`
- `td-ui-victory-150.png`: `019128b36f218df54cd59e1c7b0bd47fb8cceb3cc0b87fedd9e4a50321a9736b` → `73bfc787a04773c713d9746f34c0e15c3da546fc1af8db1f4c753db8b44dd5c8`

## Verificação

- `scripts/wsl/build.sh Release`: 0 warnings, 0 erros.
- `scripts/wsl/test.sh`: Core 463 passando + 3 skips de balanceamento, Runtime 782, Persistence 132 + 1 skip de ambiente, Rendering 624; nenhuma falha.
- Verificação dos 181 goldens TD após regeneração, sem variável de atualização: todos passando.
- Windows com `SIDEWAR_WIN_MIRROR=/mnt/c/dev/sidewar-TD21`: `scripts/wsl/win.sh build.ps1` após limpeza do mirror: 0 warnings, 0 erros; `scripts/wsl/win.sh test.ps1`: Core 463 + 3 skips, Runtime 782, Persistence 133, Rendering 496 + 128 skips de SO; nenhuma falha. Os 18 casos novos de runas passaram em ambos os sistemas, com os mesmos cinco hashes.
- `git diff --check`: limpo.
- Uma rodada intermediária apresentou falha no teste idle de alocação `SimProgressTests.Progress_Steps_Zero_Alloc`; passou na rodada final sem alterar código/testes idle (mesmo comportamento intermitente registrado na TD.20).
