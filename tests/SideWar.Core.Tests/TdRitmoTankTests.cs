using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>RITMO-TANK: poucos inimigos por onda, mais fortes e lentos, com tempo suficiente no alcance e escolha de torre que pesa.</summary>
public sealed class TdRitmoTankTests
{
    private static readonly TdContent C = TdContentLoader.LoadEmbedded();
    private static readonly HashSet<string> Bosses = [.. C.Enemies.Bosses.Select(b => b.Id)];

    [Fact]
    public void Waves_carry_few_common_enemies_in_every_stage()
    {
        foreach (var st in C.Waves.Stages)
        {
            double avg = st.Waves.Average(w => w.Groups.Where(g => !Bosses.Contains(g.Enemy)).Sum(g => g.Count));
            Assert.InRange(avg, 3.0, 8.5); // antes: 9 a 19 inimigos comuns por onda (-50% a -60%)
        }
    }

    [Fact]
    public void Everyone_is_slow_the_fast_ones_stay_relatively_fast_and_each_stays_three_seconds_in_range()
    {
        var grunt = C.Enemies.Enemies.Single(e => e.Id == "grunt");
        Assert.InRange(grunt.Speed, 24, 28); // ~0,65x dos 40 anteriores
        foreach (var fast in C.Enemies.Enemies.Where(e => e.Traits.Contains("fast")))
            Assert.InRange(fast.Speed / grunt.Speed, 1.4, 1.8);
        const double mediumRangeDiameter = 2 * 148.5;
        foreach (var e in C.Enemies.Enemies) Assert.True(mediumRangeDiameter / e.Speed >= 3.0, $"{e.Id}: {mediumRangeDiameter / e.Speed:F1}s no alcance");
        foreach (var b in C.Enemies.Bosses) Assert.True(b.Speed <= 16, b.Id);
    }

    [Fact]
    public void Tanks_are_very_tanky_and_total_hp_per_wave_does_not_drop()
    {
        Assert.True(C.Enemies.Enemies.Single(e => e.Id == "tank").Hp >= 1200);
        Assert.True(C.Enemies.Enemies.Single(e => e.Id == "slowloris").Hp >= 600);
        foreach (var b in C.Enemies.Bosses) Assert.True(b.Hp >= 2000, b.Id);
        double[] baseHpPerWave = [653, 666, 1484, 1278, 1661, 2026]; // fase 1 remedida com as fases longas (84 ondas)
        for (int i = 0; i < C.Waves.Stages.Length; i++)
        {
            double hp = C.Waves.Stages[i].Waves.Average(w => w.Groups.Sum(g =>
                g.Count * (C.Enemies.Enemies.FirstOrDefault(e => e.Id == g.Enemy)?.Hp ?? C.Enemies.Bosses.First(b => b.Id == g.Enemy).Hp)));
            Assert.True(hp >= baseHpPerWave[i] * 0.9, $"fase {i + 1}: HP/onda {hp:F0} < {baseHpPerWave[i]}");
        }
    }

    [Fact]
    public void Enraged_bosses_move_sixty_percent_faster()
    {
        Assert.Contains(C.Enemies.Bosses, b => b.Mechanic.Contains("60% mais rápido"));
    }

    [Fact]
    public void The_counter_bot_loses_clearly_less_than_the_random_bot()
    {
        // Escala de campanha neutra: mede a vantagem de escolher o contra, não o balanceamento (TdCampaignRulesTests).
        var neutral = C with { Stages = C.Stages with { Stages = C.Stages.Stages.Select(s => s with { HpStart = 1, HpEnd = 1, BossHp = 1 }).ToArray() } };
        long counter = 0, random = 0;
        double counterMin = 0, randomMin = 0;
        // Mede o DANO que chegou ao núcleo: o XP perdido deixou de ser proporcional (de Sênior em diante cada chegada custa meia barra).
        (long Damage, double Seconds) Play(int stage, TdBotProfile profile)
        {
            long damage = 0;
            var r = TdSanityBot.RunContinuous(neutral, stage, profile, 11, 2, stopAtCollapse: false, initialReputation: 300000, initialAwakened: UInt128.MaxValue, observe: m =>
            {
                foreach (var e in m.Events) if (e.Type == TdEventType.EnemyLeaked) damage += e.B;
                m.ClearEvents();
            });
            return (damage, r.CombatSeconds);
        }
        foreach (int stage in new[] { 3, 6 })
        {
            var c = Play(stage, TdBotProfile.Counter);
            var r = Play(stage, TdBotProfile.Aleatorio);
            counter += c.Damage; random += r.Damage; counterMin += c.Seconds; randomMin += r.Seconds;
        }
        Assert.True(counter * 2 < random, $"dano no núcleo: counter {counter} vs aleatório {random}");
        Assert.True(counterMin < randomMin, $"tempo: counter {counterMin / 60:F0} min vs aleatório {randomMin / 60:F0} min");
    }
}
