namespace SideWar.Core.Td;

// Comandos da partida TD (SDD-28 RF-28.16). Todos passam por TdMatchState.Apply e entram no log do replay.
// Nomes iguais aos do SDD; ficam no namespace SideWar.Core.Td (coexistem com os comandos idle de SideWar.Core.Simulation).

/// <summary>Inicia a próxima onda (só em Preparo). Com auto-início em contagem, iniciar antes paga bônus (RF-28.3).</summary>
public sealed record StartWave : IGameCommand;

/// <summary>Chamada antecipada: só em Onda com spawns esgotados; sobrepõe a próxima onda e paga bônus (RF-28.3).</summary>
public sealed record CallNextWave : IGameCommand;

/// <summary>Constrói uma torre (<c>TowerId</c> do td-towers.json) na posição lógica X, em Preparo ou Onda.</summary>
public sealed record BuildTower(string TowerId, int X) : IGameCommand;

/// <summary>Vende a torre a qualquer momento (SDD-29 §5.2): devolve <c>taxa × custo(tipo, n−1)</c>, fungível entre torres do mesmo tipo.</summary>
public sealed record SellTower(int TowerId) : IGameCommand;

/// <summary>Configura o auto-início (<c>match.autoStart</c>); <c>CountdownSeconds</c> é limitado ao intervalo do conteúdo.</summary>
public sealed record SetAutoStart(AutoStartMode Mode, double CountdownSeconds) : IGameCommand;

/// <summary>Troca o modo de mira de uma torre de ataque, a qualquer momento (RF-28.6).</summary>
public sealed record SetTargeting(int TowerId, TdTargeting Mode) : IGameCommand;

/// <summary>Usa uma habilidade (<c>scale</c>, <c>firewall</c>, <c>snapshot</c>); só em Onda (RF-28.9).</summary>
public sealed record UseAbility(string AbilityId) : IGameCommand;

/// <summary>
/// Novo layout da barra (RF-28.15, ADR-026): largura e zonas livres em DIP, vindas do <c>BuildZoneMap</c> pelo hospedeiro (o Core não chama o Runtime).
/// Muda só o mapa lógico → físico (<c>DIP = X × Largura / 1200</c>); torres cuja zona sumiu ficam SEM POSIÇÃO, e as que voltarem a caber são reposicionadas.
/// </summary>
public sealed record LayoutChanged(SideWar.Core.Simulation.StripLayout Layout) : IGameCommand;

/// <summary>Posiciona, sem custo, uma torre SEM POSIÇÃO em X lógico (snap de 44 DIP, RF-28.20); torre já posicionada é rejeitada.</summary>
public sealed record PlaceTower(int TowerId, int X) : IGameCommand;

/// <summary>Reposiciona, sem custo, uma torre SEM POSIÇÃO em X lógico (snap de 44 DIP); mantém a recarga. Torre posicionada é rejeitada (NotUnpositioned).</summary>
public sealed record MoveTower(int TowerId, int X) : IGameCommand;

/// <summary>Sinal "presente agora" do hospedeiro (RF-28.19): enviado ao log só quando muda; o Core nunca lê relógio nem entrada.</summary>
public sealed record SetPresence(bool Present) : IGameCommand;

/// <summary>Coleta a caixa-bomba ativa a tempo (RF-28.19): créditos de creditSources.crateBase/PerWave e redução de recarga de habilidade.</summary>
public sealed record CollectCrate(int CrateId) : IGameCommand;

/// <summary>
/// Pergunta-bônus do Preparo (RF-28.23, 2026-11-17): o hospedeiro confere V/F no banco <c>td-questions.json</c> e registra o resultado.
/// Só em Preparo e uma vez por Preparo; acerto paga créditos e reduz recargas (<c>rules.bonusQuestion</c>), erro não tem penalidade.
/// </summary>
public sealed record AnswerBonus(int QuestionId, bool Correct) : IGameCommand;

/// <summary>Pausa a simulação (SDD-29 §11, TD.19b): o <c>Step</c> deixa de avançar tudo. Só com a partida em andamento e fora de pausa.</summary>
public sealed record Pause : IGameCommand;

/// <summary>Retoma a simulação pausada por <see cref="Pause"/>.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716", Justification = "Nome fixado por SDD-29 §11.")]
public sealed record Resume : IGameCommand;

/// <summary>Compra uma runa adjacente com créditos, inclusive em pausa.</summary>
public sealed record BuyRune(string RuneId) : IGameCommand;

/// <summary>Devolve uma runa e os nós desconectados do Núcleo por 100% dos créditos.</summary>
public sealed record RefundRune(string RuneId) : IGameCommand;
