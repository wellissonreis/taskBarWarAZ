using SideWar.Core;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

[Trait("Env", "WindowsInteractive")]
public sealed class HotspotWindowTests
{
    private sealed class IdleHost : IGameLoopHost
    {
        public long? NextDeadline => null;
        public void OnWake(IClock clock) { }
        public void OnCommand(IGameCommand command) { }
        public void Shutdown() { }
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

    private static uint Ex(HWND h) => (uint)WindowLong.Get(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
    private static LPARAM Pt(int x, int y) => new((nint)((y << 16) | (x & 0xFFFF)));

    private static readonly PixelRect SceneRect = new(0, 0, 1440, 96);
    private const uint Layered = (uint)(WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
        WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST);

    [Fact]
    public async Task HotspotStylesOwnerClickableAndFollowTheScene() // CA-22.5, RF-22.8
    {
        var (game, errors) = await Start();
        try
        {
            var r = await OnGame(game, () =>
            {
                var scene = game.Scene;
                scene.SetBounds(SceneRect);
                var banner = scene.GetOrCreateBanner();
                var box = scene.GetOrCreateSupplyBox();
                banner.Apply(HotspotGeometry.Banner(SceneRect, 96, 300));
                box.Apply(HotspotGeometry.SupplyBox(SceneRect, 96, 1060));
                banner.Show(1);
                box.Show(2);
                bool aloneVisible = PInvoke.IsWindowVisible(new HWND(banner.Handle));
                var fg0 = PInvoke.GetForegroundWindow();
                scene.Show();
                var hb = new HWND(banner.Handle);
                var hx = new HWND(box.Handle);
                bool shown = PInvoke.IsWindowVisible(hb) && PInvoke.IsWindowVisible(hx);
                var fg1 = PInvoke.GetForegroundWindow();
                var ex = (Ex(hb), Ex(hx));
                var owners = (PInvoke.GetWindow(hb, GET_WINDOW_CMD.GW_OWNER), PInvoke.GetWindow(hx, GET_WINDOW_CMD.GW_OWNER));
                var hit = (PInvoke.SendMessage(hb, PInvoke.WM_NCHITTEST, 0, Pt(10, 10)).Value, PInvoke.SendMessage(hx, PInvoke.WM_NCHITTEST, 0, Pt(10, 10)).Value);
                var handle0 = banner.Handle;
                var sizes = (banner.Surface.Size, box.Surface.Size);
                scene.Hide();
                bool hidden = !PInvoke.IsWindowVisible(hb) && !PInvoke.IsWindowVisible(hx);
                scene.Show();
                bool reshown = PInvoke.IsWindowVisible(hb) && PInvoke.IsWindowVisible(hx);
                banner.Hide();
                bool bannerHidden = !PInvoke.IsWindowVisible(hb);
                banner.Show(3); // reuse: same HWND
                bool reused = banner.Handle == handle0 && ReferenceEquals(banner, scene.GetOrCreateBanner());
                return (aloneVisible, shown, ex, owners, hit, sizes, hidden, reshown, bannerHidden, reused, fg0, fg1, sceneH: scene.Handle, box.Bounds, bannerB: banner.Bounds);
            });
            Assert.False(r.aloneVisible);
            Assert.True(r.shown);
            Assert.Equal(Layered, r.ex.Item1 & Layered);
            Assert.Equal(Layered, r.ex.Item2 & Layered);
            Assert.Equal(0u, (r.ex.Item1 | r.ex.Item2) & (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT); // sempre clicável
            Assert.True(r.owners.Item1 == new HWND(r.sceneH) && r.owners.Item2 == new HWND(r.sceneH));
            Assert.Equal((nint)PInvoke.HTCLIENT, r.hit.Item1);
            Assert.Equal((nint)PInvoke.HTCLIENT, r.hit.Item2);
            Assert.Equal(new PixelSize(312, 48), r.sizes.Item1);
            Assert.Equal(new PixelSize(52, 44), r.sizes.Item2);
            Assert.True(r.hidden);
            Assert.True(r.reshown);
            Assert.True(r.bannerHidden);
            Assert.True(r.reused);
            Assert.Equal(r.fg0, r.fg1); // sem roubar foco
            Assert.Equal(new PixelRect(1060, 52, 1112, 96), r.Bounds);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
        Assert.True(PInvoke.FindWindow("SideWar.HotspotBanner", (string?)null).IsNull);
        Assert.True(PInvoke.FindWindow("SideWar.HotspotSupplyBox", (string?)null).IsNull);
    }

    [Fact]
    public async Task ClickInsideHitAreaPostsHotspotClicked_OutsideAndHiddenDoNot() // CA-22.5
    {
        var (game, errors) = await Start();
        var clicks = new List<(HotspotKind, int)>();
        try
        {
            await OnGame(game, () =>
            {
                var scene = game.Scene;
                scene.SetBounds(SceneRect);
                var banner = scene.GetOrCreateBanner();
                var box = scene.GetOrCreateSupplyBox();
                banner.Clicked += (k, id) => { lock (clicks) clicks.Add((k, id)); };
                box.Clicked += (k, id) => { lock (clicks) clicks.Add((k, id)); };
                banner.Apply(HotspotGeometry.Banner(SceneRect, 96, 300));
                box.Apply(HotspotGeometry.SupplyBox(SceneRect, 96, 1060));
                // scene hidden: the request is held and a stray message does nothing
                banner.Show(11);
                PInvoke.SendMessage(new HWND(banner.Handle), PInvoke.WM_LBUTTONUP, 0, Pt(20, 20));
                scene.Show();
                box.Show(22);
                PInvoke.SendMessage(new HWND(banner.Handle), PInvoke.WM_LBUTTONUP, 0, Pt(2, 2));    // anel
                PInvoke.SendMessage(new HWND(banner.Handle), PInvoke.WM_LBUTTONUP, 0, Pt(100, 20)); // controle
                PInvoke.SendMessage(new HWND(box.Handle), PInvoke.WM_LBUTTONUP, 0, Pt(30, 20));
                PInvoke.SendMessage(new HWND(box.Handle), PInvoke.WM_LBUTTONUP, 0, Pt(60, 20));     // fora (52 de largura)
                return 0;
            });
            await Task.Delay(300, TestContext.Current.CancellationToken);
            lock (clicks)
                Assert.Equal([(HotspotKind.Banner, 11), (HotspotKind.Banner, 11), (HotspotKind.SupplyBox, 22)], clicks);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task SealIsClickThroughOpaqueAndDestroyedWithTheScene() // RF-22.7, ADR-028, CA-22.6
    {
        var (game, errors) = await Start();
        try
        {
            var r = await OnGame(game, () =>
            {
                var scene = game.Scene;
                scene.SetBounds(SceneRect);
                var seal = scene.GetOrCreateSeal();
                seal.Apply(HotspotGeometry.Seal(SceneRect, 96, 500, 30));
                seal.Show();
                scene.Show();
                var h = new HWND(seal.Handle);
                var hit = PInvoke.SendMessage(h, PInvoke.WM_NCHITTEST, 0, Pt(10, 10)).Value;
                var act = PInvoke.SendMessage(h, PInvoke.WM_MOUSEACTIVATE, 0, 0).Value;
                seal.Surface.BeginFrame();
                seal.Surface.EndFrame(null);
                return (Ex(h), Ex(h) & (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT, hit, act, PInvoke.GetWindow(h, GET_WINDOW_CMD.GW_OWNER) == new HWND(scene.Handle),
                    seal.Surface.Opacity, PInvoke.IsWindowVisible(h), seal.Bounds);
            });
            Assert.Equal(Layered, r.Item1 & Layered);
            Assert.NotEqual(0u, r.Item2);
            Assert.Equal(-1, r.hit); // HTTRANSPARENT
            Assert.Equal((nint)PInvoke.MA_NOACTIVATE, r.act);
            Assert.True(r.Item5);
            Assert.Equal(255, r.Opacity);
            Assert.True(r.Item7);
            Assert.Equal(new PixelRect(470, 4, 970, 34), r.Bounds);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
        Assert.True(PInvoke.FindWindow("SideWar.SceneSeal", (string?)null).IsNull);
    }
}
