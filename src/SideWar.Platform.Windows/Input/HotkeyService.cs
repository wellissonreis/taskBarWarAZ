using System.Diagnostics;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Runtime;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace SideWar.Platform.Windows.Input;

/// <summary>
/// Registers the global hotkeys on the SceneWindow (T-Game, RF-15.6). Chords are the fixed defaults
/// Ctrl+Alt+G / Ctrl+Alt+H; textual parsing is HotkeyParser (T5.7). Conflicts mark the action
/// Unavailable and raise <see cref="Conflict"/> once per action per session.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const uint KeyG = 0x47;
    private const uint KeyH = 0x48;
    private const int FirstId = 0x5701;
    internal static int IdFor(HotkeyAction action) => FirstId + (int)action;
    private static readonly HotkeyAction[] Actions = [HotkeyAction.ToggleInteractive, HotkeyAction.ToggleVisibility];

    private readonly GameThread owner;
    private readonly SceneWindow scene;
    private readonly IGameCommandSink sink;
    private readonly Action<string> log;
    private readonly HotkeyStatus[] status = new HotkeyStatus[Actions.Length];
    private readonly bool[] notified = new bool[Actions.Length];
    private bool disposed;

    public HotkeyService(GameThread owner, SceneWindow scene, IGameCommandSink sink, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sink);
        this.owner = owner;
        this.scene = scene;
        this.sink = sink;
        this.log = log ?? (m => Trace.WriteLine(m));
        owner.VerifyAccess();
    }

    /// <summary>Raised on T-Game, once per action per session, when registration hits a conflict.</summary>
    public event Action<HotkeyAction, string>? Conflict;

    public static string Describe(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleInteractive => "Ctrl+Alt+G",
        HotkeyAction.ToggleVisibility => "Ctrl+Alt+H",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    public HotkeyStatus GetStatus(HotkeyAction action) => status[(int)action];

    /// <summary>Registers every action not yet registered; returns whether all are registered.</summary>
    public bool Register()
    {
        owner.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        scene.HotkeyHandler = OnHotkey;
        var hwnd = new HWND(scene.Handle);
        bool all = true;
        foreach (var action in Actions)
        {
            int i = (int)action;
            if (status[i] == HotkeyStatus.Registered) continue;
            uint vk = action == HotkeyAction.ToggleInteractive ? KeyG : KeyH;
            if (PInvoke.RegisterHotKey(hwnd, FirstId + i,
                HOT_KEY_MODIFIERS.MOD_CONTROL | HOT_KEY_MODIFIERS.MOD_ALT | HOT_KEY_MODIFIERS.MOD_NOREPEAT, vk))
            {
                status[i] = HotkeyStatus.Registered;
                continue;
            }
            all = false;
            status[i] = HotkeyStatus.Unavailable;
            var error = Win32Error.LastError("RegisterHotKey");
            var message = $"Atalho {Describe(action)} em uso por outro app";
            log($"{message} ({error.Message})");
            if (!notified[i])
            {
                notified[i] = true;
                try { Conflict?.Invoke(action, message); } catch (Exception e) { owner.Report(e); }
            }
        }
        return all;
    }

    private void OnHotkey(int id)
    {
        int i = id - FirstId;
        if (i < 0 || i >= Actions.Length || status[i] != HotkeyStatus.Registered) return;
        sink.Post(new HotkeyCommand(Actions[i]));
    }

    public void Dispose()
    {
        owner.VerifyAccess();
        if (disposed) return;
        disposed = true;
        scene.HotkeyHandler = null;
        var hwnd = new HWND(scene.Handle);
        for (int i = 0; i < Actions.Length; i++)
        {
            if (status[i] != HotkeyStatus.Registered) continue;
            if (!PInvoke.UnregisterHotKey(hwnd, FirstId + i)) log($"UnregisterHotKey failed for {Actions[i]}");
            status[i] = HotkeyStatus.NotRegistered;
        }
    }
}
