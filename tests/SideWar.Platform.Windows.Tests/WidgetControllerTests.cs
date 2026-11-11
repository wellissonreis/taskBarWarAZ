using SideWar.Core;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Text;
using SideWar.Rendering.Widget;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Widget;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

[Trait("Env", "WindowsInteractive")]
public sealed class WidgetControllerTests
{
    private sealed class IdleHost : IGameLoopHost
    {
        public long? NextDeadline => null;
        public void OnWake(IClock clock) { }
        public void OnCommand(IGameCommand command) { }
        public void Shutdown() { }
    }

    private sealed class Sink : IPresenceSink
    {
        public int Count;
        public void NotifyInteraction() => Count++;
    }

    private sealed class FakeMenu : IWidgetMenu
    {
        public int Shown;
        public (int X, int Y) At;
        public WidgetMenuResult? Answer;
        public WidgetMenuResult? Show(nint owner, int screenX, int screenY, WidgetMenuState state)
        {
            Shown++;
            At = (screenX, screenY);
            return Answer;
        }
    }

    private sealed class Rig : IDisposable
    {
        public readonly Typography Typo = new();
        public readonly IconCatalog Icons = new();
        public readonly WidgetRenderer Renderer;
        public readonly WidgetFramePainter Painter;
        public readonly Sink Presence = new();
        public readonly List<PanelCommand> Panel = [];
        public readonly List<WidgetMenuResult> Chosen = [];
        public WidgetController Controller = null!;
        public WidgetWindow Widget = null!;

        public Rig()
        {
            Renderer = new WidgetRenderer(Typo, Icons);
            Painter = new WidgetFramePainter(Renderer);
        }

        public void Build(GameThread game, IWidgetMenu menu)
        {
            game.Scene.SetBounds(new PixelRect(0, 0, 800, 96));
            Widget = game.Scene.GetOrCreateWidget();
            Widget.Apply(new WidgetPlacement(new PixelRect(20, 20, 440, 68), false));
            Controller = new WidgetController(Widget, Painter, Presence, Panel.Add, menu,
                () => new WidgetMenuState(false, false, ["1 (1920×1080)"], 0), Chosen.Add);
            Widget.Show();
            game.Scene.Show();
        }

        public void Dispose()
        {
            Controller?.Dispose();
            Painter.Dispose();
            Renderer.Dispose();
            Icons.Dispose();
            Typo.Dispose();
        }
    }

    private static async Task<(GameThread Game, List<Exception> Errors)> Start()
    {
        var errors = new List<Exception>();
        var game = new GameThread(new IdleHost(), e => { lock (errors) errors.Add(e); }, _ => { });
        await game.StartAsync();
        return (game, errors);
    }

    private static Task<T> OnGame<T>(GameThread game, Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Post(() => { try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); } });
        return tcs.Task;
    }

    private static LPARAM At(int x, int y) => new((y << 16) | (x & 0xFFFF));

    private static void Send(WidgetWindow w, uint message, int x, int y, uint wParam = 0) =>
        PInvoke.SendMessage(new HWND(w.Handle), message, new WPARAM(wParam), At(x, y));

    private static void Click(WidgetWindow w, int x, int y)
    {
        Send(w, PInvoke.WM_LBUTTONDOWN, x, y, 1);
        Send(w, PInvoke.WM_LBUTTONUP, x, y);
    }

    private static (int X, int Y) Button(WidgetController c, int index)
    {
        var r = c.Interaction.Layout.Button(index);
        return ((int)(r.X + r.W / 2), (int)(r.Y + r.H / 2));
    }

    private static WidgetModel Model(long version, string credits = "48,2K") =>
        new(version, credits, "+312/s", "37/60", true, "×2,0", null, null, 2, null, -1);

    [Fact]
    public async Task ClicksToggleTabs_CountPresenceOnce_AndKeepForeground() // CA-23.2 (por mensagens), CA-23.8, CA-23.5
    {
        var (game, errors) = await Start();
        using var rig = new Rig();
        try
        {
            var r = await OnGame(game, () =>
            {
                rig.Build(game, new FakeMenu());
                rig.Controller.SetModel(Model(1));
                var fg0 = PInvoke.GetForegroundWindow();
                for (int round = 0; round < 25; round++)
                    for (int b = 0; b < 4; b++)
                    {
                        var (x, y) = Button(rig.Controller, b);
                        Click(rig.Widget, x, y);
                    }
                Click(rig.Widget, 20, 24); // fora de botão: conta, não aciona
                var fg1 = PInvoke.GetForegroundWindow();
                return (fg0, fg1);
            });
            Assert.Equal(r.fg0, r.fg1);
            Assert.Equal(100, rig.Panel.Count);
            Assert.Equal(WidgetTab.Arsenal, rig.Panel[0].Tab);
            Assert.Equal(WidgetTab.Migrar, rig.Panel[3].Tab);
            Assert.Equal(101, rig.Presence.Count);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task MoveAndHover_DoNotCountPresence_AndRedrawOnlyOnChange() // CA-23.8, RF-23.8
    {
        var (game, errors) = await Start();
        using var rig = new Rig();
        try
        {
            var r = await OnGame(game, () =>
            {
                rig.Build(game, new FakeMenu());
                rig.Controller.SetModel(Model(1));
                long p0 = rig.Controller.PresentCount;
                var (x, y) = Button(rig.Controller, 2);
                Send(rig.Widget, PInvoke.WM_MOUSEMOVE, x, y);
                int hover = rig.Controller.Interaction.HoverIndex;
                long p1 = rig.Controller.PresentCount;
                Send(rig.Widget, PInvoke.WM_MOUSEMOVE, x + 1, y); // mesmo botão: nenhum redesenho
                long p2 = rig.Controller.PresentCount;
                Send(rig.Widget, PInvoke.WM_MOUSELEAVE, 0, 0);
                int hoverAfter = rig.Controller.Interaction.HoverIndex;
                long p3 = rig.Controller.PresentCount;
                return (p0, hover, p1, p2, hoverAfter, p3);
            });
            Assert.Equal(2, r.hover);
            Assert.Equal(r.p0 + 1, r.p1);
            Assert.Equal(r.p1, r.p2);
            Assert.Equal(-1, r.hoverAfter);
            Assert.Equal(r.p2 + 1, r.p3);
            Assert.Equal(0, rig.Presence.Count);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task PresentsOnlyWhenVersionChanges_AndAtMost2PerSecondWithRisingCredits() // CA-23.6 (parte), RNF-23.2
    {
        var (game, errors) = await Start();
        using var rig = new Rig();
        try
        {
            var r = await OnGame(game, () =>
            {
                rig.Build(game, new FakeMenu());
                rig.Controller.SetModel(Model(1));
                long idle0 = rig.Controller.PresentCount;
                for (int i = 0; i < 1000; i++) rig.Controller.SetModel(Model(1)); // sem mudança de versão
                long idle1 = rig.Controller.PresentCount;

                // Créditos subindo a cada 50 ms de relógio simulado; o builder coalesce a 2 Hz.
                var builder = new WidgetModelBuilder();
                long before = rig.Controller.PresentCount;
                for (int ms = 0; ms < 3000; ms += 50)
                {
                    var inputs = new WidgetInputs(1000 + ms, 5, 1, 60, PresenceState.Presente, 2.0, 0, 0, 0, null, -1);
                    rig.Controller.SetModel(builder.Update(inputs, TimeSpan.FromMilliseconds(ms)));
                }
                return (idle0, idle1, presents: rig.Controller.PresentCount - before);
            });
            Assert.Equal(r.idle0, r.idle1);
            Assert.InRange(r.presents, 1, 7); // ≤ 2/s em 3 s (+1 do primeiro)
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task IdleWidget_Has0PresentsFor30Seconds() // CA-23.6, RNF-23.1
    {
        var (game, errors) = await Start();
        using var rig = new Rig();
        try
        {
            await OnGame(game, () => { rig.Build(game, new FakeMenu()); rig.Controller.SetModel(Model(1)); return 0; });
            long p0 = await OnGame(game, () => rig.Controller.PresentCount);
            await Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            long p1 = await OnGame(game, () => rig.Controller.PresentCount);
            Assert.Equal(p0, p1);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Corners_AreIgnoredByInteraction_AndPassThroughTheOs() // CA-23.9
    {
        var (game, errors) = await Start();
        using var rig = new Rig();
        try
        {
            var r = await OnGame(game, () =>
            {
                rig.Build(game, new FakeMenu());
                rig.Controller.SetModel(Model(1));
                Send(rig.Widget, PInvoke.WM_LBUTTONDOWN, 0, 0, 1);
                Send(rig.Widget, PInvoke.WM_RBUTTONDOWN, 419, 47, 2);
                int ignored = rig.Presence.Count;
                var hwnd = new HWND(rig.Widget.Handle);
                var b = rig.Widget.Bounds;
                var corner = new System.Drawing.Point(b.Left, b.Top);
                var center = new System.Drawing.Point(b.Left + 210, b.Top + 24);
                return (ignored, atCorner: PInvoke.WindowFromPoint(corner) == hwnd, atCenter: PInvoke.WindowFromPoint(center) == hwnd);
            });
            Assert.Equal(0, r.ignored);
            Assert.True(r.atCenter);
            Assert.False(r.atCorner);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task RightClick_OpensMenuAtPoint_CountsPresence_DoesNotTriggerNav() // CA-23.11 (porta), CA-23.8
    {
        var (game, errors) = await Start();
        using var rig = new Rig();
        var menu = new FakeMenu { Answer = new WidgetMenuResult(WidgetMenuCommand.ToggleInteractive) };
        try
        {
            var r = await OnGame(game, () =>
            {
                rig.Build(game, menu);
                var (x, y) = Button(rig.Controller, 1);
                Send(rig.Widget, PInvoke.WM_RBUTTONDOWN, x, y, 2);
                Send(rig.Widget, PInvoke.WM_RBUTTONUP, x, y);
                var b = rig.Widget.Bounds;
                return (b.Left, b.Top, x, y);
            });
            Assert.Equal(1, menu.Shown);
            Assert.Equal((r.Left + r.x, r.Top + r.y), menu.At);
            Assert.Equal(1, rig.Presence.Count);
            Assert.Empty(rig.Panel);
            Assert.Equal([new WidgetMenuResult(WidgetMenuCommand.ToggleInteractive)], rig.Chosen);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task NativeMenu_OpensClosesAndReturnsChoice_WithoutChangingForeground() // CA-23.11
    {
        var (game, errors) = await Start();
        using var rig = new Rig();
        try
        {
            await OnGame(game, () => { rig.Build(game, new NativeWidgetMenu()); return 0; });
            var fgBefore = PInvoke.GetForegroundWindow();

            // Fecha o menu (modal na T-Game) pelo teclado: Down seleciona o 1º item, Enter confirma.
            var driver = Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    await Task.Delay(100);
                    var menuWindow = PInvoke.FindWindow("#32768", (string?)null);
                    if (menuWindow.IsNull) continue;
                    PInvoke.PostMessage(menuWindow, PInvoke.WM_KEYDOWN, (WPARAM)(nuint)0x28, default);
                    PInvoke.PostMessage(menuWindow, PInvoke.WM_KEYDOWN, (WPARAM)(nuint)0x0D, default);
                    return true;
                }
                return false;
            }, TestContext.Current.CancellationToken);
            await OnGame(game, () =>
            {
                var (x, y) = Button(rig.Controller, 0);
                Send(rig.Widget, PInvoke.WM_RBUTTONDOWN, x, y, 2);
                return 0;
            });
            Assert.True(await driver, "o menu nativo não apareceu");
            var fgAfter = PInvoke.GetForegroundWindow();
            Assert.Equal(fgBefore, fgAfter);
            Assert.Single(rig.Chosen);
            Assert.Equal(WidgetMenuCommand.ToggleInteractive, rig.Chosen[0].Command);
            Assert.Empty(rig.Panel);
            Assert.Equal(1, rig.Presence.Count);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }
}
