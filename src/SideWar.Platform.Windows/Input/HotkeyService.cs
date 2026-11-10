using System.Diagnostics;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Runtime;
using SideWar.Runtime.Input;
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
    private const int FirstId = 0x5701;
    internal static int IdFor(HotkeyAction action) => FirstId + (int)action;
    private static readonly HotkeyAction[] Actions = [HotkeyAction.ToggleInteractive, HotkeyAction.ToggleVisibility, HotkeyAction.TogglePause];

    private readonly GameThread owner;
    private readonly SceneWindow scene;
    private readonly IGameCommandSink sink;
    private readonly Action<string> log;
    private readonly HotkeyStatus[] status = new HotkeyStatus[Actions.Length];
    private readonly string[] text = new string[Actions.Length];
    private readonly bool[] notified = new bool[Actions.Length];
    private bool disposed;

    public HotkeyService(GameThread owner, SceneWindow scene, IGameCommandSink sink, Action<string>? log = null,
        IReadOnlyDictionary<HotkeyAction, string>? bindings = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sink);
        this.owner = owner;
        this.scene = scene;
        this.sink = sink;
        this.log = log ?? (m => Trace.WriteLine(m));
        owner.VerifyAccess();
        foreach (var a in Actions)
            text[(int)a] = bindings is not null && bindings.TryGetValue(a, out var t) ? t : Describe(a);
    }

    /// <summary>Raised on T-Game, once per action per session, when registration hits a conflict.</summary>
    public event Action<HotkeyAction, string>? Conflict;

    /// <summary>Default binding text (RF-15.6).</summary>
    public static string Describe(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleInteractive => "Ctrl+Alt+G",
        HotkeyAction.ToggleVisibility => "Ctrl+Alt+H",
        HotkeyAction.TogglePause => "Ctrl+Alt+P",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>Configured binding text for an action (as given, or the default).</summary>
    public string GetBinding(HotkeyAction action) => text[(int)action];

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
            string message;
            if (!HotkeyParser.TryParse(text[i], out var chord, out var parseError))
            {
                all = false;
                status[i] = HotkeyStatus.Unavailable;
                message = $"Atalho inválido: {text[i]} ({parseError})";
                log(message);
                Notify(i, action, message);
                continue;
            }
            var mods = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            if ((chord.Modifiers & HotkeyModifiers.Ctrl) != 0) mods |= HOT_KEY_MODIFIERS.MOD_CONTROL;
            if ((chord.Modifiers & HotkeyModifiers.Alt) != 0) mods |= HOT_KEY_MODIFIERS.MOD_ALT;
            if ((chord.Modifiers & HotkeyModifiers.Shift) != 0) mods |= HOT_KEY_MODIFIERS.MOD_SHIFT;
            if ((chord.Modifiers & HotkeyModifiers.Win) != 0) mods |= HOT_KEY_MODIFIERS.MOD_WIN;
            if (PInvoke.RegisterHotKey(hwnd, FirstId + i, mods, chord.VirtualKey))
            {
                status[i] = HotkeyStatus.Registered;
                continue;
            }
            all = false;
            status[i] = HotkeyStatus.Unavailable;
            var error = Win32Error.LastError("RegisterHotKey");
            message = $"Atalho {HotkeyParser.Format(chord)} em uso por outro app";
            log($"{message} ({error.Message})");
            Notify(i, action, message);
        }
        return all;
    }

    private void Notify(int i, HotkeyAction action, string message)
    {
        if (notified[i]) return;
        notified[i] = true;
        try { Conflict?.Invoke(action, message); } catch (Exception e) { owner.Report(e); }
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
