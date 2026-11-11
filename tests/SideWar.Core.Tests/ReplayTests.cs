using SideWar.Core.Bot;
using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>
/// T3.10 — 3 partidas de replay (CA-11.2, CA-27.11) e bot de sanidade (CA-12.3). Os goldens abaixo valem para o Core da T3.8;
/// a T6.6B altera Core/Simulation e eles devem ser REGENERADOS depois dela (só a API pública é usada aqui).
/// </summary>
public sealed class ReplayTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();

    private static StripLayout Narrow() =>
        StripLayout.Create(900f, 94f, 10f, 880f, new[] { 300f, 350f, 400f, 450f, 500f, 550f });

    private static StripLayout Tiny() =>
        StripLayout.Create(700f, 94f, 10f, 680f, new[] { 300f, 360f, 420f });

    /// <summary>A: bot, fallback de 8 slots, 200 ondas concluídas; LayoutChanged estreita (6 slots) e depois restaura.</summary>
    public static ReplayScript BotLong() => new("bot-200", 1, 1, SanityBot.FallbackLayout(),
        new[]
        {
            new ReplayCommand(20_000, new LayoutChanged(Narrow())),
            new ReplayCommand(60_000, new LayoutChanged(Tiny())),
            new ReplayCommand(100_000, new LayoutChanged(SanityBot.FallbackLayout())),
        }, UseBot: true, StopAfterWavesCleared: 200, MaxTicks: 600_000);

    /// <summary>B: comandos manuais (construir, melhorar, mira) com LayoutChanged no meio; 6000 ticks (200 s).</summary>
    public static ReplayScript Manual() => new("manual", 7, 9, Narrow(),
        new[]
        {
            new ReplayCommand(0, new BuildTower(TowerType.Bolt, 0)),
            new ReplayCommand(0, new BuildTower(TowerType.Cannon, 1)),
            new ReplayCommand(60, new BuildTower(TowerType.Frost, 2)),
            new ReplayCommand(900, new UpgradeTower(1)),
            new ReplayCommand(1200, new SetTargeting(1, Targeting.Strong)),
            new ReplayCommand(2400, new LayoutChanged(Tiny())),
            new ReplayCommand(2400, new BuildTower(TowerType.Bolt, 5)), // rejeitado: slot inexistente no novo layout
            new ReplayCommand(3000, new UpgradeTower(2)),
            new ReplayCommand(4200, new LayoutChanged(Narrow())),
            new ReplayCommand(4300, new BuildTower(TowerType.Bolt, 3)),
        }, UseBot: false, StopAfterWavesCleared: 0, MaxTicks: 6000);

    /// <summary>C: bot com outra seed e layout pequeno, 40 ondas, com LayoutChanged duas vezes.</summary>
    public static ReplayScript BotShort() => new("bot-40", 123456789, 987654321, Tiny(),
        new[]
        {
            new ReplayCommand(5_000, new LayoutChanged(Narrow())),
            new ReplayCommand(15_000, new LayoutChanged(SanityBot.FallbackLayout())),
        }, UseBot: true, StopAfterWavesCleared: 40, MaxTicks: 200_000);

    public static TheoryData<string> Names() => new() { "bot-200", "manual", "bot-40" };

    private static ReplayScript Get(string name) => name switch
    {
        "bot-200" => BotLong(), "manual" => Manual(), _ => BotShort(),
    };

    [Theory(DisplayName = "CA-11.2/CA-27.11 Replay: StateHash final idêntico em 5 execuções e igual ao golden")]
    [MemberData(nameof(Names))]
    public void Replay_Is_Deterministic(string name)
    {
        var first = ReplayRunner.Run(Content, Get(name));
        for (int i = 0; i < 4; i++) Assert.Equal(first, ReplayRunner.Run(Content, Get(name)));
        Assert.Equal(Golden[name], first.StateHash);
    }

    [Fact(DisplayName = "CA-27.11 Replay de 200 ondas conclui 200 ondas (parada por WaveCleared)")]
    public void Long_Replay_Reaches_200_Waves()
    {
        var r = ReplayRunner.Run(Content, BotLong());
        Assert.Equal(200, r.WavesCleared);
        Assert.True(r.Ticks < 600_000);
    }

    [Fact(DisplayName = "CA-11.2 Replay manual: comando fora do layout é rejeitado e conta")]
    public void Manual_Replay_Rejections()
    {
        var r = ReplayRunner.Run(Content, Manual());
        Assert.True(r.CommandsRejected >= 1);
    }

    [Fact(DisplayName = "CA-11.2 Replay: seed ou layout diferente muda o hash")]
    public void Different_Input_Different_Hash()
    {
        var a = ReplayRunner.Run(Content, BotShort());
        var b = ReplayRunner.Run(Content, BotShort() with { SeedState = 1 });
        Assert.NotEqual(a.StateHash, b.StateHash);
    }

    [Fact(DisplayName = "CA-12.3 SanityBot: constrói na ordem bolt, bolt, frost, bolt, cannon, bolt e depois melhora")]
    public void Bot_Build_Order()
    {
        var ctx = new SimContext(Content);
        var s = SimState.CreateInitial(Content, SanityBot.FallbackLayout(), Rng.Seed(1, 1));
        s.Credits = 1_000_000;
        new SanityBot(ctx).Act(s);
        var types = Enumerable.Range(0, 6).Select(i => s.Towers.At(s.FindTowerAtSlot(i).Index).Type).ToArray();
        Assert.Equal(new[] { TowerType.Bolt, TowerType.Bolt, TowerType.Frost, TowerType.Bolt, TowerType.Cannon, TowerType.Bolt }, types);
        Assert.Equal(8, s.Towers.Count);        // 8 slots ocupados
        Assert.True(s.ManualUpgrades > 0);      // créditos sobrantes viram melhorias
    }

    [Fact(DisplayName = "CA-12.3 SanityBot sem créditos não faz nada")]
    public void Bot_Idle_Without_Credits()
    {
        var ctx = new SimContext(Content);
        var s = SimState.CreateInitial(Content, SanityBot.FallbackLayout(), Rng.Seed(1, 1));
        s.Credits = 0;
        Assert.Equal(0, new SanityBot(ctx).Act(s));
    }

    // Goldens do Core da T3.8 (Linux; ver specs/evidencias/T3.10.md). REGENERAR depois da T6.6B.
    private static readonly Dictionary<string, ulong> Golden = new()
    {
        ["bot-200"] = 7034930283139373023UL, ["manual"] = 3780424209191337524UL, ["bot-40"] = 17563337984345861725UL,
    };
}
