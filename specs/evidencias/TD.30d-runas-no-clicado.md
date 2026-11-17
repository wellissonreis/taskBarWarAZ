# TD.30d — despertar o nó clicado

- `AwakenRune(string NodeId)`: desperta exatamente o nó pedido; sem sorteio, sem consumo de `Rng`. Ordem de validação: fase (`InvalidPhase`), nó existe (`UnknownRuneNode`), fragmento (`NoFragments`), teia completa (`WebComplete`), fronteira (`RuneNotInFrontier`). Rejeição não consome fragmento nem altera o hash.
- Ordem do `Rng` agora: só `BeginWave` (jitter) consome; `AwakenRune` não. SDD-29/31/32/33 mencionam o sorteio (histórico); vale esta ficha.
- Hash: `GoldenOptimizedStage3` mudou (712050039865426561 → 18013785666056342698) porque o bot passou a despertar o menor id da fronteira e o `Rng` deixou de ser consumido no despertar.
- Goldens PNG das cenas de runas (RuneWindow*/RunesTip*, 100/150%) regenerados: a cena desperta os 4 menores ids da fronteira em vez de 4 sorteados; só muda quais nós aparecem despertos.
- UI: clique em nó disponível envia `AwakenRune(nodeId)`; hover/balão e tipo oculto inalterados. Botão `AwakenRune` da UI desperta o nó selecionado se estiver na fronteira.
