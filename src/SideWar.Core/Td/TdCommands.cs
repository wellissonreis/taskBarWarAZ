namespace SideWar.Core.Td;

// Comandos da partida TD (SDD-28 RF-28.16). Todos passam por TdMatchState.Apply e entram no log do replay.
// Nomes iguais aos do SDD; ficam no namespace SideWar.Core.Td (coexistem com os comandos idle de SideWar.Core.Simulation).

/// <summary>Inicia a próxima onda (só em Preparo). Com auto-início em contagem, iniciar antes paga bônus (RF-28.3).</summary>
public sealed record StartWave : IGameCommand;

/// <summary>Chamada antecipada: só em Onda com spawns esgotados; sobrepõe a próxima onda e paga bônus (RF-28.3).</summary>
public sealed record CallNextWave : IGameCommand;

/// <summary>Constrói uma torre (<c>TowerId</c> do td-towers.json) na posição lógica X, em Preparo ou Onda.</summary>
public sealed record BuildTower(string TowerId, int X) : IGameCommand;

/// <summary>Vende a torre devolvendo <c>sellRefund</c> do total investido, a qualquer momento (RF-28.4).</summary>
public sealed record SellTower(int TowerId) : IGameCommand;

/// <summary>Melhora a torre linearmente (níveis 1 a 3). Ramos/capstone (nível 4/5) são da TD.3 (<c>ChooseBranch</c>).</summary>
public sealed record UpgradeTower(int TowerId) : IGameCommand;

/// <summary>Configura o auto-início (<c>match.autoStart</c>); <c>CountdownSeconds</c> é limitado ao intervalo do conteúdo.</summary>
public sealed record SetAutoStart(AutoStartMode Mode, double CountdownSeconds) : IGameCommand;
