using SideWar.Core.Missions;
using SideWar.Core.Simulation;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Tests.Panel;
using SideWar.Runtime.Widget;
using Xunit;

namespace SideWar.Runtime.Tests.Composition;

/// <summary>T6.11b: painel das 4 abas ligado ao jogo (fakes; sem janelas).</summary>
public sealed class PanelCoordinatorTests
{
    private sealed class FakePanel : IPanelPresenter
    {
        public PanelViewState View { get; } = new();
        public int Shows, Hides, Redraws;
        public readonly List<BuildOutcome> Outcomes = [];
        public void Show() => Shows++;
        public void Hide() => Hides++;
        public void Redraw() => Redraws++;
        public void BuildModeChanged(BuildOutcome outcome) => Outcomes.Add(outcome);
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly FakePanel Panel = new();
        public readonly GameRuntime Runtime;
        public readonly MissionService Missions;
        public readonly PanelCoordinator Coord;
        public int Changes;

        public Rig()
        {
            Runtime = new GameRuntime(new GameRuntimeOptions { Clock = Clock, UtcNow = () => new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc) });
            Missions = new MissionService(Runtime.Content, 777);
            Missions.Sync(new DateOnly(2026, 11, 13), []);
            Coord = new PanelCoordinator(Runtime, Missions, Panel, new FakeTextMetrics(), Clock, () => Changes++);
            Runtime.Dispatch(Runtime.App.LeaveHidden(HiddenReason.Starting));
            Runtime.State.Credits = 100_000;
            Runtime.Host.OnWake(Clock);
        }

        public void Advance(double s) => Clock.AdvanceTicks((long)(s * Clock.TicksPerSecond));
    }

    [Fact(DisplayName = "T6.11b widget alterna aba: abre, troca (rolagem zerada) e a mesma aba fecha; badge/aba ativa avisam o widget")]
    public void ToggleTabs()
    {
        var r = new Rig();
        Assert.Null(r.Coord.ActiveTab);
        r.Coord.Execute(PanelCommand.ToggleTab(WidgetTab.Missoes));
        Assert.Equal(WidgetTab.Missoes, r.Coord.ActiveTab);
        Assert.NotNull(r.Coord.MissionsLayout);
        Assert.Equal(1, r.Panel.Shows);
        r.Coord.Execute(PanelCommand.ToggleTab(WidgetTab.Migrar));
        Assert.Equal(WidgetTab.Migrar, r.Coord.ActiveTab);
        Assert.NotNull(r.Coord.Migrate);
        r.Coord.Execute(PanelCommand.ToggleTab(WidgetTab.Migrar));
        Assert.Null(r.Coord.ActiveTab);
        Assert.Equal(1, r.Panel.Hides);
        Assert.True(r.Changes >= 3);
    }

    [Fact(DisplayName = "T6.11b as 4 abas montam modelo e layout reais")]
    public void AllFourTabsBuild()
    {
        var r = new Rig();
        foreach (var tab in new[] { WidgetTab.Arsenal, WidgetTab.Fases, WidgetTab.Missoes, WidgetTab.Migrar })
        {
            r.Coord.Open(tab);
            Assert.True(r.Panel.View.Content > 0, tab.ToString());
        }
        Assert.NotNull(r.Coord.Arsenal);
        Assert.NotNull(r.Coord.Campaign);
    }

    [Fact(DisplayName = "T6.11b COLETAR: clique no corpo posta... aplica GrantMissionReward entre ticks e marca Coletada")]
    public void CollectMissionAppliesGrant()
    {
        var r = new Rig();
        var first = r.Missions.Missions[0];
        var def = r.Runtime.Content.Missions.Missions.First(m => m.Id == first.Id);
        r.Missions.Restore(new MissionServiceState("2026-11-13", [new MissionEntry(first.Id, def.Target, MissionStatus.Completed)], 0));
        r.Coord.Open(WidgetTab.Missoes);
        var layout = r.Coord.MissionsLayout!;
        (float x, float y) = FindHit(layout, TabBodyHitKind.MissionCollect);
        long before = r.Runtime.State.MissionsCollected;
        r.Coord.PointerDown(x, y, default);
        r.Coord.PointerUp(x, y, default);
        Assert.Equal(before + 1, r.Runtime.State.MissionsCollected);
        Assert.Equal(MissionStatus.Collected, r.Missions.Missions[0].Status);
        Assert.Equal(0, r.Missions.Badge);
    }

    [Fact(DisplayName = "T6.11b MIGRAR: 1º clique arma, expira em 5 s e troca de aba desarma")]
    public void MigrateArmingTimeoutAndCancel()
    {
        var r = new Rig();
        r.Runtime.State.StageWaveMax = 20; // migração disponível (≥ onda 15)
        r.Coord.Open(WidgetTab.Migrar);
        (float x, float y) = FindHit(r.Coord.Migrate!, TabBodyHitKind.Migrate);
        r.Coord.PointerDown(x, y, default);
        r.Coord.PointerUp(x, y, default);
        Assert.True(r.Coord.Controller.Arming.Armed);
        Assert.NotNull(r.Coord.NextDeadline);
        r.Advance(5.1);
        r.Coord.OnWake(r.Clock);
        Assert.False(r.Coord.Controller.Arming.Armed);
        r.Coord.PointerDown(x, y, default);
        r.Coord.PointerUp(x, y, default);
        Assert.True(r.Coord.Controller.Arming.Armed);
        r.Coord.Execute(PanelCommand.ToggleTab(WidgetTab.Missoes));
        Assert.False(r.Coord.Controller.Arming.Armed);
    }

    [Fact(DisplayName = "T6.11b Arsenal: toggle de auto-escala, melhoria vira comando e ícone inicia o modo construção")]
    public void ArsenalActions()
    {
        var r = new Rig();
        Assert.True(r.Runtime.State.Apply(r.Runtime.Context, new BuildTower(TowerType.Bolt, TestLayouts.X(0))).IsOk);
        r.Runtime.State.Credits = 10_000_000;
        r.Coord.Open(WidgetTab.Arsenal);
        var arsenal = r.Coord.Arsenal!;
        // auto-escala
        (float x, float y) = FindArsenalHit(arsenal, ArsenalHitKind.AutoScaleToggle);
        bool was = r.Runtime.Host.AutoScaleEnabled;
        r.Coord.PointerDown(x, y, default);
        r.Coord.PointerUp(x, y, default);
        Assert.Equal(!was, r.Runtime.Host.AutoScaleEnabled);
        // melhoria: comando ao host, aplicado no próximo wake
        arsenal = r.Coord.Arsenal!;
        (x, y) = FindArsenalHit(arsenal, ArsenalHitKind.UpgradeTower);
        int level = r.Runtime.State.Towers.At(0).Level;
        r.Coord.PointerDown(x, y, default);
        r.Coord.PointerUp(x, y, default);
        r.Advance(0.05);
        r.Runtime.Host.OnWake(r.Clock);
        Assert.Equal(level + 1, r.Runtime.State.Towers.At(0).Level);
        // ícone: modo construção já no mouse down; soltar sem mover arma
        (x, y) = FindArsenalHit(r.Coord.Arsenal!, ArsenalHitKind.TowerIcon);
        r.Coord.PointerDown(x, y, new BuildPointer(100, 100, 0, true));
        Assert.Equal(BuildPhase.Pressed, r.Coord.BuildMode.Phase);
        r.Coord.PointerUp(x, y, new BuildPointer(100, 100, 0, true));
        Assert.Equal(BuildPhase.Armed, r.Coord.BuildMode.Phase);
        Assert.NotNull(r.Coord.Building);
        Assert.Equal(2, r.Panel.Outcomes.Count);
        r.Coord.Close(); // fechar o painel cancela o modo (clique no painel)
        Assert.Equal(BuildPhase.Idle, r.Coord.BuildMode.Phase);
    }

    [Fact(DisplayName = "T6.11b clique numa torre da cena abre o Arsenal e rola até a linha dela uma vez")]
    public void OpenArsenalScrollsToTower()
    {
        var r = new Rig();
        for (int i = 0; i < 3; i++) Assert.True(r.Runtime.State.Apply(r.Runtime.Context, new BuildTower(TowerType.Bolt, TestLayouts.X(i * 2))).IsOk);
        r.Coord.Open(WidgetTab.Arsenal);
        var rows = r.Coord.Arsenal!.Rows.Where(x => x.Model.Kind == ArsenalRowKind.Tower).ToList();
        Assert.NotEmpty(rows);
        int id = rows[^1].Model.TowerId;
        r.Coord.Close();
        r.Coord.Execute(PanelCommand.OpenArsenal(id));
        Assert.Equal(WidgetTab.Arsenal, r.Coord.ActiveTab);
        Assert.True(r.Coord.Arsenal!.TryGetTowerRowTop(id, out float top));
        Assert.Equal(PanelScroll.Clamp(top, r.Panel.View.Content, r.Panel.View.Viewport), r.Panel.View.Offset);
    }

    [Fact(DisplayName = "T6.11b refresh a 1 Hz só redesenha se o dado exibido mudou (RF-24.13)")]
    public void RedrawOnlyOnChange()
    {
        var r = new Rig();
        r.Coord.Open(WidgetTab.Fases);
        int redraws = r.Panel.Redraws;
        for (int i = 0; i < 5; i++) { r.Advance(1.1); r.Coord.OnWake(r.Clock); }
        Assert.Equal(redraws, r.Panel.Redraws); // nada mudou: nenhum quadro
        r.Runtime.State.Wave++;
        r.Advance(1.1);
        r.Coord.OnWake(r.Clock);
        Assert.Equal(redraws + 1, r.Panel.Redraws);
    }

    [Fact(DisplayName = "T6.11b painel fechado: sem deadline")]
    public void ClosedHasNoDeadline()
    {
        var r = new Rig();
        Assert.Null(r.Coord.NextDeadline);
        r.Coord.Open(WidgetTab.Fases);
        Assert.NotNull(r.Coord.NextDeadline);
        r.Coord.Close();
        Assert.Null(r.Coord.NextDeadline);
    }

    private static (float X, float Y) FindHit(MissionsLayout layout, TabBodyHitKind kind) => Scan((x, y) => layout.Hit(x, y, 0f).Kind == kind);
    private static (float X, float Y) FindHit(MigrateLayout layout, TabBodyHitKind kind) => Scan((x, y) => layout.Hit(x, y, 0f).Kind == kind);
    private static (float X, float Y) FindArsenalHit(ArsenalLayout layout, ArsenalHitKind kind) =>
        Scan((x, y) => layout.Hit(x, y, 0f, PanelScroll.ViewportDip).Kind == kind);

    private static (float X, float Y) Scan(Func<float, float, bool> match)
    {
        for (float y = PanelScroll.ViewportTopDip; y < PanelScroll.ViewportTopDip + PanelScroll.ViewportDip; y += 2)
            for (float x = 4; x < 380; x += 2)
                if (match(x, y)) return (x, y);
        throw new InvalidOperationException("hit not found");
    }
}
