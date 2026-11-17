using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using AnswerBonus = SideWar.Core.Td.AnswerBonus;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;
using UseAbility = SideWar.Core.Td.UseAbility;

namespace SideWar.Core.Tests;

/// <summary>TD.17: AnswerBonus (RF-28.23) e estatísticas/análise de derrota (RF-28.24).</summary>
public sealed class TdBonusDefeatTests
{
    private static readonly TdContent C = TdMechanicsContent.Load();

    private static TdMatchState New(int stage = 1, ulong seed = 7)
    {
        var s = TdMatchState.Create(C, stage, Rng.Seed(seed, 54));
        Assert.True(s.Apply(new LayoutChanged(StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)]))).IsOk);
        return s;
    }

    private static void RunWave(TdMatchState s)
    {
        for (int i = 0; i < 30 * 900 && s.Phase == MatchPhase.Onda; i++) s.Step();
    }

    [Fact]
    public void AnswerBonus_correct_pays_base_plus_per_wave_per_offered_question_and_logs_event()
    {
        var s = New();
        long c0 = s.Credits;
        var r = s.Apply(new AnswerBonus(7, true));
        Assert.True(r.IsOk);
        var q = C.Runes.CreditSources;
        Assert.Equal(c0 + q.BonusQuestionBase + q.BonusQuestionPerWave * 1, s.Credits);
        Assert.Contains(s.Events, e => e.Type == TdEventType.BonusAnswered && e.A == 7 && e.B == s.Credits - c0);
        // TD.30b: cada pergunta oferecida (cadência por tempo real no Runtime) vale uma resposta; a 2ª paga de novo.
        Assert.True(s.Apply(new AnswerBonus(8, true)).IsOk);
        Assert.Equal(c0 + 2 * (q.BonusQuestionBase + q.BonusQuestionPerWave), s.Credits);
    }

    [Fact]
    public void AnswerBonus_wrong_has_no_penalty_and_does_not_block_the_next_question()
    {
        var s = New();
        long c0 = s.Credits;
        Assert.True(s.Apply(new AnswerBonus(3, false)).IsOk);
        Assert.Equal(c0, s.Credits);
        Assert.Equal(1, s.BonusWrongCount);
        Assert.True(s.Apply(new AnswerBonus(4, true)).IsOk);
        Assert.Equal(1, s.BonusCorrectCount);
    }

    [Fact]
    public void AnswerBonus_is_accepted_in_prep_and_in_wave_paying_by_the_current_wave_number()
    {
        var s = New();
        Assert.True(s.Apply(new BuildTower("bolt", 600)).IsOk);
        long c0 = s.Credits;
        Assert.True(s.Apply(new AnswerBonus(1, true)).IsOk); // Preparo: paga pela próxima onda (1)
        Assert.Equal(c0 + s.BonusCreditsFor(1), s.Credits);
        Assert.True(s.Apply(new StartWave()).IsOk);
        long c1 = s.Credits;
        Assert.True(s.Apply(new AnswerBonus(2, true)).IsOk); // Onda (clássico): vale e paga pela onda atual
        Assert.Equal(c1 + s.BonusCreditsFor(s.WaveNumber), s.Credits);
        Assert.True(s.Apply(new AnswerBonus(3, false)).IsOk); // várias por onda: a cadência é do Runtime
        RunWave(s);
        Assert.Equal(MatchPhase.Preparo, s.Phase);
        Assert.True(s.Apply(new AnswerBonus(4, true)).IsOk);
    }

    [Fact]
    public void AnswerBonus_changes_the_hash_and_replays_identically()
    {
        ulong Run(bool answer)
        {
            var s = New();
            if (answer) s.Apply(new AnswerBonus(5, true));
            return s.ComputeStateHash();
        }
        Assert.Equal(Run(true), Run(true));
        Assert.NotEqual(Run(true), Run(false));
    }

    [Fact]
    public void Leak_stats_and_defeat_analysis_without_towers_blame_the_portal_side()
    {
        var s = New();
        s.Apply(new StartWave());
        for (int i = 0; i < 30 * 900 && s.Phase != MatchPhase.Derrota; i++)
        {
            if (s.Phase == MatchPhase.Preparo) s.Apply(new StartWave());
            s.Step();
        }
        Assert.Equal(MatchPhase.Derrota, s.Phase);
        Assert.NotEmpty(s.Leaks);
        Assert.Equal(s.IntegrityMax, s.Leaks.Sum(l => l.Damage));
        var d = s.DefeatAnalysis();
        Assert.Equal(TdDefeatReason.FewTowersNearPortal, d.Reason);
        Assert.Equal(TdDefeatTip.BuildNearPortal, d.Tip);
    }

    [Fact]
    public void Leak_stats_do_not_change_the_state_hash()
    {
        var a = New(); var b = New();
        a.Apply(new StartWave()); b.Apply(new StartWave());
        for (int i = 0; i < 600; i++) { a.Step(); b.Step(); }
        _ = a.Leaks; _ = a.DefeatAnalysis();
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact]
    public void Question_bank_is_valid_and_outside_the_td_content_hash()
    {
        var bank = TdQuestionBank.LoadEmbedded();
        Assert.True(bank.Questions.Length >= 60);
        Assert.DoesNotContain("td-questions.json", TdContentLoader.FileNames);
    }
}
