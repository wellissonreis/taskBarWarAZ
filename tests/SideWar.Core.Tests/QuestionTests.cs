using System.Text;
using SideWar.Core.Content;
using SideWar.Core.Events;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class QuestionTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly QuestionBank Bank = QuestionBankLoader.LoadEmbedded(Content);
    private const string Topic = "Computação em nuvem";

    /// <summary>LCG simples e determinístico (o Rng PCG32 real entra na parte B).</summary>
    private sealed class TestRng(uint seed) : IQuestionRng
    {
        private uint _s = seed;
        public int NextInt(int exclusiveMax)
        {
            _s = unchecked(_s * 1664525u + 1013904223u);
            return (int)((_s >> 8) % (uint)exclusiveMax);
        }
    }

    private static QuestionBankLoadResult LoadWith(Func<string, string> edit)
        => QuestionBankLoader.Load(Encoding.UTF8.GetBytes(edit(Encoding.UTF8.GetString(QuestionBankLoader.ReadEmbedded()))), Content);

    private static void AssertError(QuestionBankLoadResult r, string fragment)
    {
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, m => m.Contains(fragment, StringComparison.Ordinal));
    }

    // ---- Banco e validador (CA-26.8) ----

    [Fact(DisplayName = "T-26.8 Embedded bank is valid, provisional and covers every topic minimum")]
    public void Bank_Embedded_Valid()
    {
        Assert.True(Bank.Provisional);
        var min = QuestionBankValidator.MinimumPerTopic(Content.Stages, Content.Waves.BossEvery);
        Assert.Equal(10, min.Count);
        foreach (var (topic, need) in min)
            Assert.True(Bank.ForTopic(topic).Count >= need, topic);
        Assert.All(Bank.Questions, q => Assert.Equal(1, q.Options.Count(o => o.Correct)));
        Assert.Contains(Bank.Questions, q => q.CorrectIndex > 0);
        Assert.NotEmpty(QuestionBankValidator.Warnings(Bank, Content.Stages, Content.Waves.BossEvery));
    }

    [Fact(DisplayName = "CA-26.8 Validator rejects 2 or 5 options, 0 or 2 correct, duplicate id, unknown topic, short topic")]
    public void Validator_Rejects()
    {
        AssertError(LoadWith(s => s.Replace("\"id\": \"cloud-1\"", "\"id\": \"cloud-1\", \"signals\": [\"a\"]")), "signals");
        AssertError(LoadWith(s => s.Replace("\"id\": \"cloud-2\"", "\"id\": \"cloud-1\"")), "id duplicado 'cloud-1'");
        AssertError(LoadWith(s => s.Replace("\"stageTopic\": \"IaaS\"", "\"stageTopic\": \"Nope\"")), "stageTopic desconhecido");
        AssertError(LoadWith(s => s.Replace("\"stageTopic\": \"IaaS\"", "\"stageTopic\": \"SaaS\"")), "tópico 'IaaS'");
        AssertError(LoadWith(s => s.Replace("\"correct\": true", "\"correct\": false")), "exatamente uma");
        AssertError(LoadWith(s => s.Replace("\"correct\": false", "\"correct\": true")), "exatamente uma");
        AssertError(LoadWith(s => s.Replace("\"kind\": \"transfer\"", "\"kind\": \"other\"")), "kind inválido");
        AssertError(LoadWith(s => s.Replace("\"problem\": \"Qual é a diferença entre escalabilidade e elasticidade?\"", "\"problem\": \"  \"")), "problem vazio");
        AssertError(QuestionBankLoader.Load("{\"provisional\":true,\"questions\":[{\"x\":1}]}"u8.ToArray(), Content), "questions.json");
        AssertError(QuestionBankLoader.Load("{\"provisional\":true,\"questions\":[null]}"u8.ToArray(), Content), "nulo");
    }

    [Fact(DisplayName = "CA-26.8 Validator rejects 2 and 5 options and accepts 3 and 4")]
    public void Validator_OptionCount()
    {
        QuestionOption O(bool c) => new("t", c, "e");
        QuestionDef Q(string id, params QuestionOption[] o) => new(id, Topic, "mandatory", "p", o);
        var stages = new[] { new StageDef(1, "t", false, 60, Topic) };
        IReadOnlyList<string> Run(params QuestionDef[] qs) => QuestionBankValidator.Validate(new QuestionBank(false, qs), stages, 20);

        var three = Q("a", O(true), O(false), O(false));
        var four = Q("b", O(true), O(false), O(false), O(false));
        var five = Q("c", O(true), O(false), O(false), O(false), O(false));
        var two = Q("d", O(true), O(false));
        Assert.Empty(Run(three, four, Q("e", O(false), O(true), O(false))));
        Assert.Contains(Run(three, four, three with { Id = "e" }, five), m => m.Contains("3 ou 4"));
        Assert.Contains(Run(three, four, three with { Id = "e" }, two), m => m.Contains("3 ou 4"));
        Assert.Contains(Run(three, four), m => m.Contains("mínimo 3"));
        Assert.Empty(Run(three with { Signals = ["a", "b", "c"] }, four, three with { Id = "e" }));
        Assert.Contains(Run(three with { Signals = ["a", " ", "c"] }, four, three with { Id = "e" }), m => m.Contains("signal vazio"));
    }

    // ---- Seleção (CA-26.4) ----

    [Fact(DisplayName = "CA-26.4 No repetition until exhausted, then the set resets; same seed gives same sequence")]
    public void Selector_NoRepeat_Reset_Deterministic()
    {
        var n = Bank.ForTopic(Topic).Count;
        List<string> Sequence(uint seed, int draws)
        {
            var sel = new QuestionSelector(Bank);
            var rng = new TestRng(seed);
            return [.. Enumerable.Range(0, draws).Select(_ => sel.Draw(1, 1, Topic, rng).Id)];
        }
        var seq = Sequence(7, n * 2);
        Assert.Equal(n, seq.Take(n).Distinct().Count());
        Assert.Equal(n, seq.Skip(n).Distinct().Count());
        Assert.Equal(seq, Sequence(7, n * 2));
        Assert.NotEqual(seq, Sequence(8, n * 2));
    }

    [Fact(DisplayName = "CA-26.4 Used sets are per (stage, attempt), persist via snapshot and release returns the question")]
    public void Selector_Scopes_Snapshot_Release()
    {
        var sel = new QuestionSelector(Bank);
        var rng = new TestRng(1);
        var a = sel.Draw(1, 1, Topic, rng);
        Assert.Equal([a.Id], sel.Used(1, 1));
        Assert.Empty(sel.Used(1, 2));
        var other = sel.Draw(1, 2, Topic, rng);
        Assert.Equal([other.Id], sel.Used(1, 2));

        var copy = new QuestionSelector(Bank);
        copy.Restore(sel.Snapshot());
        Assert.Equal(sel.Used(1, 1), copy.Used(1, 1));
        Assert.Equal(sel.Draw(1, 1, Topic, new TestRng(5)).Id, copy.Draw(1, 1, Topic, new TestRng(5)).Id);

        sel.Release(1, 2, other.Id);
        sel.Release(9, 9, "x");
        Assert.Empty(sel.Used(1, 2));
        Assert.Throws<InvalidOperationException>(() => sel.Draw(1, 1, "Tema inexistente", rng));
    }

    // ---- Ledger (CA-26.3) ----

    private static readonly LedgerKey Key = new(1, 1, 20);

    [Fact(DisplayName = "CA-26.3 Second answer is rejected and does not change the bonus")]
    public void Ledger_SecondAnswerRejected()
    {
        var ledger = new QuestionLedger();
        Assert.Equal(LedgerStatus.Ok, ledger.Record(Key, "q", 1, true));
        Assert.Equal(LedgerStatus.AlreadyUsed, ledger.Record(Key, "q2", 0, false));
        Assert.True(ledger.Get(Key)!.Correct);
        Assert.Equal("q", ledger.Get(Key)!.QuestionId);
        Assert.True(ledger.HasPendingBonus(Key));
        Assert.False(ledger.Has(new LedgerKey(1, 1, 40)));
        Assert.Null(ledger.Get(new LedgerKey(1, 1, 40)));
    }

    [Fact(DisplayName = "CA-26.3 Bonus: restore before spawn reapplies once; after spawn does not")]
    public void Ledger_Bonus_Restore()
    {
        var ledger = new QuestionLedger();
        ledger.Record(Key, "q", 0, true);

        var before = new QuestionLedger();
        before.Restore(ledger.Snapshot());           // restore antes do spawn
        Assert.Equal(0.4, before.ConsumeBossHpMultiplier(Key));
        Assert.Equal(1.0, before.ConsumeBossHpMultiplier(Key));

        var after = new QuestionLedger();
        after.Restore(before.Snapshot());            // restore depois do spawn
        Assert.False(after.HasPendingBonus(Key));
        Assert.Equal(1.0, after.ConsumeBossHpMultiplier(Key));
        Assert.True(after.Get(Key)!.BonusConsumed);
    }

    [Fact(DisplayName = "CA-26.3 Wrong and timeout give no bonus; retreat does not repeat question or bonus")]
    public void Ledger_NoBonus_WrongTimeout_And_Retreat()
    {
        var ledger = new QuestionLedger();
        var sel = new QuestionSelector(Bank);
        ledger.Record(new LedgerKey(1, 1, 20), "a", 1, false);
        ledger.Record(new LedgerKey(1, 1, 40), "b", null, true);
        Assert.False(ledger.Get(new LedgerKey(1, 1, 40))!.Correct); // timeout nunca é correto
        Assert.True(ledger.Get(new LedgerKey(1, 1, 40))!.TimedOut);
        Assert.Equal(1.0, ledger.ConsumeBossHpMultiplier(new LedgerKey(1, 1, 20)));
        Assert.Equal(1.0, ledger.ConsumeBossHpMultiplier(new LedgerKey(1, 1, 40)));
        Assert.Equal(1.0, ledger.ConsumeBossHpMultiplier(new LedgerKey(1, 1, 60)));

        // Recuo por queda: ao voltar à onda 20, o ledger já tem a resposta -> sem novo encontro.
        Assert.Null(BossEncounter.Begin(Bank, sel, ledger, new LedgerKey(1, 1, 20), Topic, new TestRng(1)));
        Assert.Empty(sel.Used(1, 1));
    }

    [Fact(DisplayName = "T-26.3 Counters for stars and missions; snapshot is ordered")]
    public void Ledger_Counts_Snapshot()
    {
        var ledger = new QuestionLedger();
        ledger.Record(new LedgerKey(1, 1, 40), "b", 0, true);
        ledger.Record(new LedgerKey(1, 1, 20), "a", 1, false);
        ledger.Record(new LedgerKey(1, 1, 60), "c", null, false);
        ledger.Record(new LedgerKey(1, 2, 20), "d", 0, true);
        Assert.Equal(1, ledger.CorrectCount(1, 1));
        Assert.Equal(2, ledger.AnsweredCount(1, 1));
        Assert.Equal(4, ledger.Count);
        Assert.Equal([20, 40, 60, 20], ledger.Snapshot().Select(kv => kv.Key.Wave));
    }

    // ---- Encontro (CA-26.2, CA-26.7) ----

    private static (BossEncounter Enc, QuestionLedger Ledger, QuestionSelector Sel) Start()
    {
        var ledger = new QuestionLedger();
        var sel = new QuestionSelector(Bank);
        var enc = BossEncounter.Begin(Bank, sel, ledger, Key, Topic, new TestRng(3))!;
        return (enc, ledger, sel);
    }

    [Fact(DisplayName = "CA-26.2 Correct answer: HP x0.4, common chest, stars and mission, ledger written")]
    public void Boss_Correct()
    {
        var (enc, ledger, _) = Start();
        Assert.True(enc.IsActive);
        Assert.Equal(45, enc.Remaining);
        Assert.Equal(LedgerStatus.Ok, enc.Answer(enc.Question.CorrectIndex));
        var o = enc.Outcome!;
        Assert.Equal(BossResult.Correct, o.Result);
        Assert.Equal(0.4, o.BossHpMultiplier);
        Assert.True(o.GrantsCommonChest && o.CountsForStars && o.CountsForMission);
        Assert.Equal(enc.Question.Options[o.CorrectIndex].Explanation, o.CorrectExplanation);
        Assert.Equal(o.CorrectExplanation, o.ChosenExplanation);
        Assert.True(ledger.HasPendingBonus(Key));
        Assert.Equal(LedgerStatus.AlreadyUsed, enc.Answer(0));
        Assert.False(enc.IsActive);
    }

    [Fact(DisplayName = "CA-26.2 Wrong answer: correct option highlighted, full strength, counts for mission only")]
    public void Boss_Wrong()
    {
        var (enc, ledger, _) = Start();
        var wrong = Enumerable.Range(0, enc.Question.Options.Length).First(i => i != enc.Question.CorrectIndex);
        enc.Answer(wrong);
        var o = enc.Outcome!;
        Assert.Equal(BossResult.Wrong, o.Result);
        Assert.Equal(enc.Question.CorrectIndex, o.CorrectIndex);
        Assert.Equal(enc.Question.Options[wrong].Explanation, o.ChosenExplanation);
        Assert.Equal(1.0, o.BossHpMultiplier);
        Assert.False(o.GrantsCommonChest || o.CountsForStars);
        Assert.True(o.CountsForMission);
        Assert.False(ledger.HasPendingBonus(Key));
    }

    [Fact(DisplayName = "CA-26.2 Timeout at 45 s: full strength, no stars or mission, ledger marked timeout")]
    public void Boss_Timeout()
    {
        var (enc, ledger, _) = Start();
        enc.Advance(44.9);
        Assert.True(enc.IsActive);
        Assert.Equal(0.1, enc.Remaining, 6);
        enc.Advance(0.2);
        var o = enc.Outcome!;
        Assert.Equal(BossResult.Timeout, o.Result);
        Assert.Null(o.OptionIndex);
        Assert.Null(o.ChosenExplanation);
        Assert.Equal(1.0, o.BossHpMultiplier);
        Assert.False(o.CountsForMission || o.CountsForStars || o.GrantsCommonChest);
        Assert.True(ledger.Get(Key)!.TimedOut);
        Assert.Equal(LedgerStatus.AlreadyUsed, enc.Answer(0));
        enc.Advance(10);
        Assert.Equal(0, enc.Remaining);
    }

    [Fact(DisplayName = "CA-26.2 Closing the card does not change the 45 s countdown")]
    public void Boss_CardDoesNotAffectTime()
    {
        var (a, _, _) = Start();
        var (b, _, _) = Start();
        a.OpenCard();
        Assert.True(a.CardOpen);
        a.Advance(10);
        a.CloseCard();
        a.Advance(15);
        b.Advance(25);
        Assert.False(a.CardOpen);
        Assert.Equal(b.Remaining, a.Remaining);
        Assert.Equal(20, a.Remaining);
        a.Answer(a.Question.CorrectIndex);
        a.OpenCard();
        Assert.False(a.CardOpen);
        Assert.Throws<ArgumentOutOfRangeException>(() => Start().Enc.Answer(99));
    }

    [Fact(DisplayName = "CA-26.7 Cancel (Absent/Offline) leaves the ledger untouched, returns the question and redraws on return")]
    public void Boss_Cancel()
    {
        var (enc, ledger, sel) = Start();
        enc.OpenCard();
        Assert.Single(sel.Used(1, 1));
        enc.Cancel();
        Assert.True(enc.IsCancelled);
        Assert.False(enc.IsActive || enc.CardOpen);
        Assert.Null(enc.Outcome);
        Assert.Equal(0, ledger.Count);
        Assert.Empty(sel.Used(1, 1));
        enc.Advance(60);
        Assert.Equal(0, ledger.Count);
        Assert.Equal(LedgerStatus.AlreadyUsed, enc.Answer(0));
        enc.Cancel();

        var again = BossEncounter.Begin(Bank, sel, ledger, Key, Topic, new TestRng(9));
        Assert.NotNull(again);
        Assert.Equal(0, again.Elapsed);
    }

    [Fact(DisplayName = "CA-26.3 Restore before answer rewrites the same question and elapsed time; after answer starts nothing")]
    public void Boss_Restore()
    {
        var (enc, ledger, sel) = Start();
        enc.Advance(12);
        var state = enc.State;
        var restored = BossEncounter.Restore(Bank, sel, ledger, state)!;
        Assert.Equal(enc.Question, restored.Question);
        Assert.Equal(12, restored.Elapsed);
        Assert.Equal(33, restored.Remaining);

        restored.Answer(restored.Question.CorrectIndex);
        Assert.Null(BossEncounter.Restore(Bank, sel, ledger, state));
        Assert.Throws<InvalidOperationException>(() =>
            BossEncounter.Restore(Bank, sel, new QuestionLedger(), state with { QuestionId = "nope" }));
    }
}
