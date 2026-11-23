using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.29b: chip compacto com clique só nos botões, pausa real, velocidade, abertura pausada, tutorial na 1ª execução e aba Runas.</summary>
public sealed class TdHud29bTests
{
    private const int Width = 1000;

    private sealed class Host : ITdUiHost
    {
        public TdGameOptions GameOptions { get; private set; } = new();
        public void SetGameOptions(TdGameOptions options) => GameOptions = options;
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; }
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => stageId <= 2;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public TdRankProgress Rank => TdRank.Progress(0);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => true;
    }

    private sealed class Store : ITdOnboardingStore
    {
        public bool Done;
        public bool TutorialDone => Done;
        public void SetTutorialDone(bool done) => Done = done;
        public bool TipSeen(string id) => true;
        public void MarkTipSeen(string id) { }
    }

    private sealed class Rig
    {
        public readonly TdMatchState Match;
        public readonly List<IGameCommand> Sent = [];
        public readonly TdUiController Ui;

        public Rig(Store? store = null, int stage = 1)
        {
            Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), stage, Rng.Seed(7, 54), null, TdMatchOptions.Default);
            Match.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
            Ui = new TdUiController(() => Match, c => { Sent.Add(c); Match.Apply(c); }, new Host(), () => 0.0, store);
            Ui.Sync();
        }
    }

    [Fact]
    public void Hud_has_design_width_and_start_only_when_needed()
    {
        var r = new Rig();
        var hud = r.Ui.BuildHud();
        // em Preparo o botão Iniciar existe; depois da 1ª onda (contínua) o chip em repouso tem ≤ 220 DIP
        Assert.Equal((779f + 88f + 4f + 34f) * 0.7f, hud.Width);
        Assert.Equal(28f, hud.Height);
        r.Match.Apply(new SideWar.Core.Td.BuildTower("bolt", 600));
        var sw = r.Match.Apply(new StartWave());
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        var rest = r.Ui.BuildHud();
        Assert.Equal((779f + 34f) * 0.7f, rest.Width);
        Assert.DoesNotContain(rest.Items, i => i.Action is TdUiAction.StartWave);
        Assert.All(rest.Items, i => Assert.True(i.Rect.X + i.Rect.W <= rest.Width + 0.01f));
        Assert.All(rest.Items.Where(i => i.Kind == TdUiKind.Button), i => Assert.True(i.Rect.W <= 100f, i.Text));
    }

    [Fact]
    public void Only_buttons_receive_clicks_the_rest_of_the_chip_is_transparent()
    {
        var r = new Rig();
        r.Match.Apply(new StartWave());
        var hud = r.Ui.BuildHud();
        foreach (var it in hud.Items.Where(i => i.Kind == TdUiKind.Button))
            Assert.True(hud.OverButton(it.Rect.X + it.Rect.W / 2f, it.Rect.Y + it.Rect.H / 2f), it.Text);
        var chip = hud.Items.Single(i => i.Action == TdUiAction.HudRestore);
        var castle = hud.Items.Single(i => i.Action == TdUiAction.QuickTowers);
        float gap = (chip.Rect.X + chip.Rect.W + castle.Rect.X) / 2f; // vão entre o chip e o botão Torres
        Assert.False(hud.OverButton(gap, 14f));
        Assert.False(hud.OverButton(chip.Rect.X + 2f, 1f - 3f)); // acima do chip
        Assert.False(hud.OverButton(-5f, 14f));
    }

    [Fact]
    public void Pause_button_sends_real_pause_and_resume_and_survives_the_modal()
    {
        var r = new Rig();
        r.Match.Apply(new StartWave());
        var pause = r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.PauseToggle);
        Assert.Equal(TdIcon.Pause, pause.Icon);
        r.Ui.Activate(pause);
        Assert.True(r.Match.Paused);
        var paused = r.Ui.BuildHud().Items.Single(i => i.Action == TdUiAction.PauseToggle);
        Assert.True(paused.Selected);
        Assert.Equal(TdIcon.Play, paused.Icon);
        // a modal abre/fecha sem mexer na pausa do usuário
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.HudRestore));
        r.Ui.Cancel();
        Assert.True(r.Match.Paused);
        r.Ui.Activate(paused);
        Assert.False(r.Match.Paused);
        Assert.Single(r.Sent.OfType<Resume>());
    }

    [Fact]
    public void App_start_opens_the_game_tab_paused_and_closing_resumes()
    {
        var r = new Rig();
        r.Ui.OpenStartupModal();
        Assert.Equal(TdUiMode.Menu, r.Ui.Mode);
        Assert.True(r.Ui.ModalOpen);
        Assert.True(r.Match.Paused);
        r.Ui.Cancel();
        Assert.False(r.Ui.ModalOpen);
        Assert.False(r.Match.Paused);
    }

    [Fact]
    public void First_run_tutorial_starts_from_the_startup_modal_and_the_hint_does_not_need_focus()
    {
        var store = new Store { Done = false };
        var r = new Rig(store);
        r.Ui.OpenStartupModal();
        r.Ui.Sync();
        Assert.Equal(TdTutorialStep.HudCollapse, r.Ui.Step);
        Assert.Contains(r.Ui.BuildPopover()!.Items, i => i.SArg == "tut");
        r.Ui.Cancel(); r.Ui.Sync();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.HudMinimize)); r.Ui.Sync();
        Assert.Equal(TdTutorialStep.Core, r.Ui.Step);
        Assert.False(store.Done);
    }

    [Fact]
    public void Tutorial_points_the_castle_button_for_the_first_tower()
    {
        var store = new Store { Done = false };
        var r = new Rig(store);
        r.Ui.OpenStartupModal(); r.Ui.Sync(); r.Ui.Cancel(); r.Ui.Sync();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.HudMinimize)); r.Ui.Sync();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.HudRestore)); r.Ui.Sync();
        for (int i = 0; i < 3; i++) r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.TutorialNext));
        Assert.Equal(TdTutorialStep.Build, r.Ui.Step);
        Assert.Contains(r.Ui.BuildHud().Items, i => i.Action == TdUiAction.QuickTowers && i.Highlight);
    }

    [Fact]
    public void Runes_tab_draws_the_web_and_awakens_through_the_core_command()
    {
        var r = new Rig();
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.Menu));
        r.Ui.Activate(new TdUiItem(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Runes));
        var items = r.Ui.BuildPopover()!.Items;
        var nodes = items.Where(i => i.Action == TdUiAction.SelectRune).ToArray();
        Assert.Equal(32, nodes.Length);
        Assert.Equal(8, nodes.Count(n => n.Fill == TdUiController.RuneAvailable)); // só o anel interno está disponível no começo
        Assert.DoesNotContain(items, i => i.Action == TdUiAction.AwakenRune); // TD.30c: clicar no nó tracejado desperta (sem botão separado)
        typeof(TdMatchState).GetProperty("Fragments")!.SetValue(r.Match, 2L);
        var avail = r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.SelectRune && i.Fill == TdUiController.RuneAvailable);
        r.Ui.Activate(avail);
        Assert.Contains(r.Sent, c => c is AwakenRune a && a.NodeId == avail.SArg); // o nó clicado, não um sorteado
    }

    [Fact]
    public void Rune_types_keep_the_design_order_used_by_the_glyphs()
    {
        var types = TdContentLoader.LoadEmbedded().Runes.Types.Select(t => t.Id).ToArray();
        Assert.Equal(["range", "bolt", "credits", "frost", "core", "cannon", "cooldown", "shield"], types);
    }
}
