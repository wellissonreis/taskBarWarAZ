// Spike TA.1 (DESCARTAVEL, fora do produto e da SideWar.slnx).
// Compilar (Windows): csc.exe /nologo /platform:x64 /out:TA1.exe /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll Spike.cs
// Uso: TA1.exe discover | TA1.exe run <A|B|C|P|N>
// Variantes: A=gatilhos da spec + debounce 100 ms (trailing); B=idem com borda de subida imediata + trailing 100 ms; C=B + qualquer mudanca de foreground; P=C + checagem de z-order a cada 50 ms; N=sem reafirmacao (controle) <reps> [casos] [--towers x1,x2,x3] [--empty x]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

delegate IntPtr WndProcDelegate(IntPtr h, uint m, IntPtr w, IntPtr l);
delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

static class W
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLEND { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX { public int cbSize; public uint style; public WndProcDelegate proc; public int cbCls, cbWnd; public IntPtr hInst, hIcon, hCursor, hbr; public string menu, cls; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)] public struct MSG { public IntPtr h; public uint m; public IntPtr w, l; public uint t; public POINT pt; }
    [StructLayout(LayoutKind.Sequential)] public struct BMIH { public int size, w, h; public short planes, bpp; public int comp, sizeImg, xpm, ypm, used, imp; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] public struct IU { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public IU u; }

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string t);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] i, int size);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassEx(ref WNDCLASSEX c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr p, IntPtr m, IntPtr i, IntPtr prm);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pd, ref SIZE sz, IntPtr src, ref POINT ps, uint key, ref BLEND b, uint fl);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint fl);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SetTimer(IntPtr h, IntPtr id, uint ms, IntPtr f);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr h, IntPtr id);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG m, IntPtr h, uint a, uint b);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int c);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint a, uint b, IntPtr mod, WinEventDelegate d, uint pid, uint tid, uint fl);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BMIH bi, uint u, out IntPtr bits, IntPtr sec, uint off);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);
    [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string n);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);

    [DllImport("user32.dll")] public static extern bool GetWindowBand(IntPtr h, out uint band);
    public static string Cls(IntPtr h) { var sb = new StringBuilder(128); GetClassName(h, sb, 128); return sb.ToString(); }
    public static string Proc(IntPtr h)
    {
        try { uint pid; GetWindowThreadProcessId(h, out pid); return Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; }
    }
}

class Spike
{
    const uint WS_POPUP = 0x80000000;
    const int GWL_EXSTYLE = -20;
    const int EX_LAYERED = 0x80000, EX_TOPMOST = 0x8, EX_TOOL = 0x80, EX_NOACT = 0x08000000, EX_TRANSP = 0x20;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    const uint WM_APP1 = 0x8001, WM_APP2 = 0x8002, WM_TIMER = 0x113, WM_LBUTTONDOWN = 0x201, WM_MOUSEACTIVATE = 0x21, WM_RBUTTONDOWN = 0x204;

    static IntPtr hwnd, tray, hook, hwndFocus;
    static WndProcDelegate focusProc = (h, m, w, l) => W.DefWindowProc(h, m, w, l);
    static WndProcDelegate wndProc = WndProcImpl;
    static WinEventDelegate hookProc = HookImpl;
    static W.RECT bar;
    static int barW, barH;
    static double scale;
    static string variant = "A";
    static bool lead, wide, noReassert, poll;
    static long swpCount, skipCount, evtCount, ownClicks;
    static bool timerArmed, trailingNeeded;
    static readonly object logLock = new object();
    static List<string> evLog = new List<string>();
    static long lastApplyTicks;
    static List<int[]> towers = new List<int[]>(); // x,y,w,h (px, relativo a tela)
    static uint[] towerColors = { 0xFFFF00FF, 0xFF00FFFF, 0xFFFFFF00 };
    static StreamWriter outLog;

    static double Ms(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }

    // ---------- janela ----------
    static IntPtr WndProcImpl(IntPtr h, uint m, IntPtr w, IntPtr l)
    {
        switch (m)
        {
            case WM_LBUTTONDOWN: Interlocked.Increment(ref ownClicks); return IntPtr.Zero;
            case WM_MOUSEACTIVATE: return new IntPtr(3);
            case WM_APP1: // modo: w=1 passivo
                {
                    int ex = W.GetWindowLong(h, GWL_EXSTYLE);
                    ex = (w.ToInt32() == 1) ? (ex | EX_TRANSP) : (ex & ~EX_TRANSP);
                    W.SetWindowLong(h, GWL_EXSTYLE, ex);
                    return IntPtr.Zero;
                }
            case WM_APP2: ApplyZ("forced", true); return IntPtr.Zero;
            case WM_TIMER:
                if (w.ToInt64() == 2) { if (!noReassert) ApplyZ("poll", false); return IntPtr.Zero; }
                OnTimer(); return IntPtr.Zero;
        }
        return W.DefWindowProc(h, m, w, l);
    }

    static bool OursAboveTray()
    {
        IntPtr p = W.GetWindow(tray, 3); // GW_HWNDPREV
        int guard = 0;
        while (p != IntPtr.Zero && guard++ < 5000) { if (p == hwnd) return true; p = W.GetWindow(p, 3); }
        return false;
    }

    static void ApplyZ(string why, bool force)
    {
        if (!force && OursAboveTray()) { Interlocked.Increment(ref skipCount); return; }
        W.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Interlocked.Increment(ref swpCount);
        lastApplyTicks = Stopwatch.GetTimestamp();
        Log("swp(" + why + ")");
    }

    static void Log(string s) { lock (logLock) { evLog.Add(Stopwatch.GetTimestamp() + "|" + s); } }

    static void Trigger(string why)
    {
        evtCount++;
        if (noReassert) return;
        if (lead)
        {
            if (!timerArmed) { ApplyZ(why, false); timerArmed = true; trailingNeeded = false; W.SetTimer(hwnd, new IntPtr(1), 100, IntPtr.Zero); }
            else trailingNeeded = true;
        }
        else
        {
            W.KillTimer(hwnd, new IntPtr(1));
            W.SetTimer(hwnd, new IntPtr(1), 100, IntPtr.Zero);
            timerArmed = true;
        }
    }

    static void OnTimer()
    {
        W.KillTimer(hwnd, new IntPtr(1));
        if (lead)
        {
            timerArmed = false;
            if (trailingNeeded) { trailingNeeded = false; ApplyZ("trail", false); timerArmed = true; W.SetTimer(hwnd, new IntPtr(1), 100, IntPtr.Zero); }
        }
        else { timerArmed = false; ApplyZ("debounce", false); }
    }

    static void HookImpl(IntPtr hk, uint evt, IntPtr h, int idObject, int idChild, uint thread, uint time)
    {
        if (h == hwnd) return;
        string c = W.Cls(h);
        bool isTray = c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd";
        if (evt == 0x3) // foreground
        {
            Log("fg:" + c + "/" + W.Proc(h));
            if (isTray || wide) Trigger("fg:" + c);
        }
        else if (idObject == 0 && isTray)
        {
            Log((evt == 0x8004 ? "reorder:" : "loc:") + c);
            Trigger(evt == 0x8004 ? "reorder" : "loc");
        }
    }

    static void CreateFocusWindow()
    {
        var wc = new W.WNDCLASSEX();
        wc.cbSize = Marshal.SizeOf(typeof(W.WNDCLASSEX)); wc.proc = focusProc; wc.hInst = W.GetModuleHandle(null); wc.cls = "TA1Focus"; wc.hbr = new IntPtr(6);
        W.RegisterClassEx(ref wc);
        hwndFocus = W.CreateWindowEx(0, "TA1Focus", "TA1 foco (spike)", 0x10CA0000, 760, 280, 400, 120, IntPtr.Zero, IntPtr.Zero, wc.hInst, IntPtr.Zero); // OVERLAPPED|CAPTION|SYSMENU|VISIBLE
    }
    static void FocusOther()
    {   // clique real na janela auxiliar: outro app em primeiro plano, barra sai do estado do shell
        Move(960, 330); Thread.Sleep(60); Click(); Thread.Sleep(180);
    }

    static void CreateScene(bool interactive)
    {
        W.RECT r; W.GetWindowRect(tray, out r); bar = r; barW = r.R - r.L; barH = r.B - r.T;
        uint dpi = W.GetDpiForWindow(tray); scale = dpi / 96.0;
        var wc = new W.WNDCLASSEX();
        wc.cbSize = Marshal.SizeOf(typeof(W.WNDCLASSEX)); wc.proc = wndProc; wc.hInst = W.GetModuleHandle(null); wc.cls = "TA1SpikeScene";
        W.RegisterClassEx(ref wc);
        int ex = EX_LAYERED | EX_TOPMOST | EX_TOOL | EX_NOACT | (interactive ? 0 : EX_TRANSP);
        hwnd = W.CreateWindowEx((uint)ex, "TA1SpikeScene", "TA1", WS_POPUP, bar.L, bar.T, barW, barH, IntPtr.Zero, IntPtr.Zero, wc.hInst, IntPtr.Zero);
        // bitmap
        var bi = new W.BMIH(); bi.size = Marshal.SizeOf(typeof(W.BMIH)); bi.w = barW; bi.h = -barH; bi.planes = 1; bi.bpp = 32;
        IntPtr bits; IntPtr dc = W.CreateCompatibleDC(IntPtr.Zero);
        IntPtr dib = W.CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
        W.SelectObject(dc, dib);
        int[] px = new int[barW * barH]; // alfa 0 = transparente
        int railH = (int)Math.Round(14 * scale);
        for (int y = 0; y < railH; y++) for (int x = 0; x < barW; x++) px[y * barW + x] = unchecked((int)0xFFD05000); // trilho laranja opaco
        int ts = (int)Math.Round(24 * scale);
        int ty = railH + (barH - railH - ts) / 2;
        for (int i = 0; i < towers.Count; i++)
        {
            int tx = towers[i][0] - bar.L;
            towers[i][1] = bar.T + ty; towers[i][2] = ts; towers[i][3] = ts;
            for (int y = ty; y < ty + ts; y++) for (int x = tx; x < tx + ts; x++) px[y * barW + x] = unchecked((int)towerColors[i]);
        }
        Marshal.Copy(px, 0, bits, px.Length);
        var pd = new W.POINT { X = bar.L, Y = bar.T }; var sz = new W.SIZE { cx = barW, cy = barH }; var ps = new W.POINT();
        var bl = new W.BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
        if (!W.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref pd, ref sz, dc, ref ps, 0, ref bl, 2)) Console.WriteLine("ULW falhou " + Marshal.GetLastWin32Error());
        W.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    // ---------- captura de pixels ----------
    static IntPtr scrDc, memDc, grabBits; static int gx, gy, gw, gh;
    static void InitGrab()
    {
        int minx = towers.Min(t => t[0]);
        int maxx = towers.Max(t => t[0]) + (int)Math.Round(24 * scale);
        gx = minx; gw = maxx - minx; gy = bar.T; gh = Math.Min(barH, (int)Math.Round(14 * scale) + (int)Math.Round(24 * scale) + 12);
        scrDc = W.GetDC(IntPtr.Zero); memDc = W.CreateCompatibleDC(scrDc);
        var bi = new W.BMIH(); bi.size = Marshal.SizeOf(typeof(W.BMIH)); bi.w = gw; bi.h = -gh; bi.planes = 1; bi.bpp = 32;
        IntPtr d = W.CreateDIBSection(scrDc, ref bi, 0, out grabBits, IntPtr.Zero, 0);
        W.SelectObject(memDc, d);
    }
    static byte[] GrabBytes() { byte[] b = new byte[gw * gh * 4]; Marshal.Copy(grabBits, b, 0, b.Length); return b; }
    static int Px(int sx, int sy) { W.BitBlt(memDc, 0, 0, gw, gh, scrDc, gx, gy, 0xCC0020 | 0x40000000); return 0; }
    static int Read(int sx, int sy) { return Marshal.ReadInt32(grabBits, ((sy - gy) * gw + (sx - gx)) * 4); }
    static bool Near(int a, uint b)
    {
        int ar = (a >> 16) & 255, ag = (a >> 8) & 255, ab = a & 255;
        int br = (int)((b >> 16) & 255), bg = (int)((b >> 8) & 255), bb = (int)(b & 255);
        return Math.Abs(ar - br) < 40 && Math.Abs(ag - bg) < 40 && Math.Abs(ab - bb) < 40;
    }
    // true se as 3 torres e o trilho estao visiveis
    static bool Visible()
    {
        Px(0, 0);
        for (int i = 0; i < towers.Count; i++)
        {
            int cx = towers[i][0] + towers[i][2] / 2, cy = towers[i][1] + towers[i][3] / 2;
            if (!Near(Read(cx, cy), towerColors[i])) return false;
        }
        int rx = towers[0][0], ry = bar.T + 3;
        return Near(Read(rx, ry), 0xFFD05000);
    }

    // ---------- entrada ----------
    static int sw, sh;
    static void Move(int x, int y)
    {
        var i = new W.INPUT[1]; i[0].type = 0;
        i[0].u.mi.dx = (int)Math.Ceiling(x * 65535.0 / (sw - 1)); i[0].u.mi.dy = (int)Math.Ceiling(y * 65535.0 / (sh - 1)); i[0].u.mi.flags = 0x8001; // MOVE|ABSOLUTE
        W.SendInput(1, i, Marshal.SizeOf(typeof(W.INPUT)));
    }
    static void Click()
    {
        var i = new W.INPUT[2]; i[0].type = 0; i[0].u.mi.flags = 0x2; i[1].type = 0; i[1].u.mi.flags = 0x4;
        W.SendInput(2, i, Marshal.SizeOf(typeof(W.INPUT)));
    }
    static void Key(ushort vk)
    {
        var i = new W.INPUT[2]; i[0].type = 1; i[0].u.ki.vk = vk; i[1].type = 1; i[1].u.ki.vk = vk; i[1].u.ki.flags = 2;
        W.SendInput(2, i, Marshal.SizeOf(typeof(W.INPUT)));
    }

    // ---------- alvos (UIA) ----------
    class Target { public string Name, Cls, Id; public int L, T, R, B; public int CX { get { return (L + R) / 2; } }
        public int CY { get { return (T + B) / 2; } } }
    static List<Target> Scan()
    {
        var res = new List<Target>();
        var root = AutomationElement.FromHandle(tray);
        foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
        {
            try
            {
                var r = e.Current.BoundingRectangle; if (r.IsEmpty) continue;
                res.Add(new Target { Name = e.Current.Name, Cls = e.Current.ClassName, Id = e.Current.AutomationId, L = (int)r.Left, T = (int)r.Top, R = (int)r.Right, B = (int)r.Bottom });
            }
            catch { }
        }
        return res;
    }

    static void Out(string s) { Console.WriteLine(s); if (outLog != null) { outLog.WriteLine(s); outLog.Flush(); } }

    // ---------- tentativa ----------
    class TrialResult
    {
        public string Case; public int Rep; public double CoverStart = -1, RecoverPx = 0, RecoverZ = 0, CoveredMs; public int Episodes, ZEpisodes, Swp, Skip, Evts, Samples; public double MaxGap, AvgGap; public bool Recovered = true; public string Fg = "", Events = "", Note = "", Band = "";
    }
    static Action prep;
    static List<TrialResult> results = new List<TrialResult>();

    static void Reset()
    {
        FocusOther();
        W.SendMessage(hwnd, WM_APP2, IntPtr.Zero, IntPtr.Zero); // forca topo
        for (int k = 0; k < 40; k++) { Thread.Sleep(25); if (Visible() && OursAboveTray()) break; }
        Thread.Sleep(150);
        Interlocked.Exchange(ref swpCount, 0); Interlocked.Exchange(ref skipCount, 0); evtCount = 0;
        lock (logLock) evLog.Clear();
    }

    // acao ja deve emitir o clique; t0 e tomado logo antes
    static TrialResult Trial(string name, int rep, Action act, int windowMs, Action after) { return Trial(name, rep, act, windowMs, after, false); }
    static TrialResult Trial(string name, int rep, Action act, int windowMs, Action after, bool noReset)
    {
        if (noReset) { Interlocked.Exchange(ref swpCount, 0); Interlocked.Exchange(ref skipCount, 0); evtCount = 0; lock (logLock) evLog.Clear(); }
        else Reset();
        if (prep != null) { var pp = prep; prep = null; pp(); }
        var samples = new List<long>(); var vis = new List<bool>(); var zab = new List<bool>();
        bool async = name.StartsWith("burst");
        string bandSeq = ""; string lastBand = null;
        long t0 = Stopwatch.GetTimestamp();
        Thread at = null;
        if (async) { at = new Thread(() => act()); at.Start(); } else act();
        long end = t0 + (long)(windowMs / 1000.0 * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < end)
        {
            bool v = Visible(); bool z = OursAboveTray(); string b = Band(tray);
            if (b != lastBand) { bandSeq += (bandSeq.Length > 0 ? ">" : "") + b + "@" + Math.Round(Ms(Stopwatch.GetTimestamp() - t0)) + "ms"; lastBand = b; }
            samples.Add(Stopwatch.GetTimestamp()); vis.Add(v); zab.Add(z);
        }
        if (at != null) at.Join();
        if (samples.Count == 0) { samples.Add(t0); vis.Add(false); zab.Add(false); }
        var r = new TrialResult { Case = name, Rep = rep, Samples = samples.Count, Band = bandSeq };
        r.Swp = (int)swpCount; r.Skip = (int)skipCount; r.Evts = (int)evtCount;
        Analyze(r, t0, samples, vis, true); var r2 = new TrialResult(); Analyze(r2, t0, samples, zab, false);
        r.RecoverZ = r2.RecoverPx; r.ZEpisodes = r2.Episodes;
        double gapMax = 0, sum = 0; for (int i = 1; i < samples.Count; i++) { double g = Ms(samples[i] - samples[i - 1]); sum += g; if (g > gapMax) gapMax = g; }
        r.MaxGap = gapMax; r.AvgGap = samples.Count > 1 ? sum / (samples.Count - 1) : 0;
        IntPtr fg = W.GetForegroundWindow(); r.Fg = W.Proc(fg) + "/" + W.Cls(fg);
        lock (logLock) { r.Events = string.Join("; ", evLog.Take(14).Select(s => { var p = s.Split('|'); return Math.Round(Ms(long.Parse(p[0]) - t0)) + "ms " + p[1]; }).ToArray()); }
        if (!r.Recovered)
        {
            var sb = new StringBuilder(); IntPtr q = W.GetWindow(tray, 0); // GW_HWNDFIRST
            int k = 0; while (q != IntPtr.Zero && k < 12) { if (W.IsWindowVisible(q)) { W.RECT qr; W.GetWindowRect(q, out qr); sb.Append(W.Proc(q) + "/" + W.Cls(q) + (q == hwnd ? "(OURS)" : "") + (q == tray ? "(TRAY)" : "") + "[" + qr.L + "," + qr.T + "," + qr.R + "," + qr.B + "] > "); k++; } q = W.GetWindow(q, 2); }
            r.Note = "zorder: " + sb.ToString();
            Visible(); try { File.WriteAllBytes("out\\cover-" + name + "-" + rep + ".raw", GrabBytes()); } catch { }
        }
        if (after != null) after();
        results.Add(r);
        Out(string.Format("  [{0} #{1}] cover@{2:F0}ms recPx={3:F0}ms recZ={4:F0}ms coveredTot={5:F0}ms ep={6} zep={7} swp={8} skip={9} evt={10} n={11} gapAvg={12:F1} gapMax={13:F1} ok={14} fg={15} band={17} | {16}",
            name, rep, r.CoverStart, r.RecoverPx, r.RecoverZ, r.CoveredMs, r.Episodes, r.ZEpisodes, r.Swp, r.Skip, r.Evts, r.Samples, r.AvgGap, r.MaxGap, r.Recovered, r.Fg, r.Events + " || " + r.Note, r.Band));
        return r;
    }

    static void Analyze(TrialResult r, long t0, List<long> t, List<bool> ok, bool full)
    {
        int n = t.Count; double covered = 0; int ep = 0; double first = -1, last = 0; bool prev = true;
        for (int i = 0; i < n; i++)
        {
            if (!ok[i]) { if (prev) { ep++; if (first < 0) first = Ms(t[i] - t0); } if (i > 0) covered += Ms(t[i] - t[i - 1]); last = Ms(t[i] - t0); }
            prev = ok[i];
        }
        if (full) { r.CoverStart = first; r.CoveredMs = covered; r.Recovered = ok[n - 1]; }
        r.Episodes = ep;
        // recuperacao = primeira amostra ok depois da ultima amostra nao-ok
        int lastBad = -1; for (int i = 0; i < n; i++) if (!ok[i]) lastBad = i;
        r.RecoverPx = lastBad < 0 ? 0 : (lastBad + 1 < n ? Ms(t[lastBad + 1] - t0) : double.PositiveInfinity);
    }

    static string Band(IntPtr h) { uint b; return W.GetWindowBand(h, out b) ? b.ToString() : "?"; }
    static void SetMode(bool passive) { W.SendMessage(hwnd, WM_APP1, new IntPtr(passive ? 1 : 0), IntPtr.Zero); Thread.Sleep(100); }
    static void Neutral() { Move(sw / 2, sh / 3); Thread.Sleep(250); }

    static Target ByName(List<Target> ts, Func<Target, bool> f) { return ts.FirstOrDefault(f); }

    // ---------- plano ----------
    static void RunPlan(int reps, HashSet<string> cases, int emptyX)
    {
        var ts = Scan();
        for (int tries = 0; tries < 8 && !ts.Any(t => t.Id == "StartButton"); tries++) { Out("UIA vazio, repetindo..."); Thread.Sleep(2000); ts = Scan(); }
        var chrome = ByName(ts, t => (t.Id ?? "").Contains("MSTeams")); // Teams: 1 janela, nao elevado (UIPI bloqueia SendInput com app elevado em foco); alterna minimizar/restaurar
        var start = ByName(ts, t => t.Id == "StartButton");
        var search = ByName(ts, t => t.Id == "SearchButton");
        var chevron = ByName(ts, t => t.Cls == "SystemTray.NormalButton" && t.Id == "SystemTrayIcon");
        Out("alvos: chrome=" + (chrome != null ? chrome.Name : "-") + " start=" + (start != null) + " search=" + (search != null) + " chevron=" + (chevron != null ? chevron.Name : "-"));
        IntPtr origFg = W.GetForegroundWindow();
        W.POINT origPt; W.GetCursorPos(out origPt);
        IntPtr chromeHwnd = IntPtr.Zero; bool chromeWasIconic = false;
        foreach (var p in Process.GetProcessesByName("ms-teams")) if (p.MainWindowHandle != IntPtr.Zero) chromeHwnd = p.MainWindowHandle;
        if (chromeHwnd != IntPtr.Zero) chromeWasIconic = W.IsIconic(chromeHwnd);
        Out("fg inicial=" + W.Proc(origFg) + " chromeHwnd=" + chromeHwnd + " iconic=" + chromeWasIconic);

        Action<int, int> MoveClick = (x, y) => { prep = () => { Move(x, y); Thread.Sleep(80); }; };
        for (int rep = 1; rep <= reps; rep++)
        {
            if (cases.Contains("empty")) { MoveClick(emptyX, bar.T + barH / 2); Trial("empty", rep, () => Click(), 1500, () => Neutral()); }
            if (cases.Contains("icon") && chrome != null)
            {
                MoveClick(chrome.CX, chrome.CY);
                Trial("icon-1", rep, () => Click(), 1500, () => Thread.Sleep(200));
                Trial("icon-2", rep, () => Click(), 1500, () => Thread.Sleep(300), true);
            }
            if (cases.Contains("start") && start != null)
            {
                MoveClick(start.CX, start.CY);
                Trial("start-open", rep, () => Click(), 1500, null);
                Trial("start-close", rep, () => Key(0x1B), 1500, null, true);
                Trial("start-focus", rep, () => FocusOther(), 1500, null, true);
            }
            if (cases.Contains("search") && search != null)
            {
                MoveClick(search.CX, search.CY);
                Trial("search-open", rep, () => Click(), 1500, null);
                Trial("search-close", rep, () => Key(0x1B), 1500, null, true);
                Trial("search-focus", rep, () => FocusOther(), 1500, null, true);
            }
            if (cases.Contains("tray") && chevron != null)
            {
                MoveClick(chevron.CX, chevron.CY);
                Trial("tray-open", rep, () => Click(), 1500, null);
                Trial("tray-close", rep, () => Key(0x1B), 1500, null, true);
                Trial("tray-focus", rep, () => FocusOther(), 1500, null, true);
            }
            if (cases.Contains("thumbhover") && chrome != null)
            {
                MoveClick(chrome.CX, chrome.CY);
                Trial("thumbhover", rep, () => { Move(chrome.CX + 2, chrome.CY + 1); }, 1800, null);
                Trial("thumbhover-leave", rep, () => Move(sw / 2, sh / 3), 1500, null, true);
            }
            if (cases.Contains("thumbclick") && chrome != null)
            {
                prep = () =>
                {
                    Move(chrome.CX, chrome.CY); Thread.Sleep(1300);
                    IntPtr th = W.FindWindow("TaskListThumbnailWnd", null); W.RECT tr; int tx = chrome.CX, ty = bar.T - 90;
                    if (th != IntPtr.Zero && W.GetWindowRect(th, out tr)) { tx = (tr.L + tr.R) / 2; ty = (tr.T + tr.B) / 2 + 20; Out("  thumb rect " + tr.L + "," + tr.T + "," + tr.R + "," + tr.B); }
                    else Out("  thumb window nao encontrada (usando posicao estimada " + tx + "," + ty + ")");
                    Move(tx, ty); Thread.Sleep(250);
                };
                Trial("thumbclick", rep, () => Click(), 1500, () => { Thread.Sleep(300); Neutral(); });
            }
            if (cases.Contains("burst"))
            {
                MoveClick(emptyX, bar.T + barH / 2);
                Trial("burst20", rep, () => { for (int k = 0; k < 20; k++) { Click(); Thread.Sleep(50); } }, 2200, () => Neutral());
            }
        }
        if (cases.Contains("diag") && start != null)
        {
            Out("== DIAG: Iniciar -> Esc, amostrando 5 s (banda = GetWindowBand) ==");
            for (int rep = 1; rep <= 2; rep++)
            {
                Reset(); Move(start.CX, start.CY); Thread.Sleep(80); Click(); Thread.Sleep(1200);
                Out("  aberto: trayBand=" + Band(tray) + " oursBand=" + Band(hwnd) + " fg=" + W.Proc(W.GetForegroundWindow()) + " vis=" + Visible() + " acima=" + OursAboveTray());
                Key(0x1B); long t0 = Stopwatch.GetTimestamp(); string last = "";
                while (Ms(Stopwatch.GetTimestamp() - t0) < 5000)
                {
                    string cur = "trayBand=" + Band(tray) + " oursBand=" + Band(hwnd) + " fg=" + W.Proc(W.GetForegroundWindow()) + "/" + W.Cls(W.GetForegroundWindow()) + " vis=" + Visible() + " acima=" + OursAboveTray();
                    if (cur != last) { Out(string.Format("  +{0:F0}ms {1}", Ms(Stopwatch.GetTimestamp() - t0), cur)); last = cur; }
                    Thread.Sleep(5);
                }
                Out("  tentando SetWindowPos TOPMOST manual..."); W.SendMessage(hwnd, WM_APP2, IntPtr.Zero, IntPtr.Zero); Thread.Sleep(200);
                Out("  apos SWP manual: trayBand=" + Band(tray) + " oursBand=" + Band(hwnd) + " vis=" + Visible() + " acima=" + OursAboveTray());
                string[] steps = { "clique em area vazia da barra", "clique em janela de app (area neutra)" };
                for (int st = 0; st < 2; st++)
                {
                    if (st == 0) Move(1600, 1056); else Move(sw / 2, sh / 3);
                    Thread.Sleep(100); Click(); long t1 = Stopwatch.GetTimestamp(); string l2 = "";
                    Out("  -> " + steps[st]);
                    while (Ms(Stopwatch.GetTimestamp() - t1) < 1500)
                    {
                        string cur = "trayBand=" + Band(tray) + " fg=" + W.Proc(W.GetForegroundWindow()) + "/" + W.Cls(W.GetForegroundWindow()) + " vis=" + Visible() + " acima=" + OursAboveTray();
                        if (cur != l2) { Out(string.Format("     +{0:F0}ms {1}", Ms(Stopwatch.GetTimestamp() - t1), cur)); l2 = cur; }
                        Thread.Sleep(5);
                    }
                }
                Neutral();
            }
        }
        // ---- clique passante ----
        if (cases.Contains("pass"))
        {
            Out("== Passivo (WS_EX_TRANSPARENT) ==");
            SetMode(true);
            for (int rep = 1; rep <= Math.Max(reps, 5); rep++)
            {
                Reset();
                int tx = towers[0][0] + towers[0][2] / 2, ty = towers[0][1] + towers[0][3] / 2;
                Move(tx, ty); Thread.Sleep(100);
                W.POINT p = new W.POINT { X = tx, Y = ty }; IntPtr hit = W.WindowFromPoint(p);
                long c0 = Interlocked.Read(ref ownClicks); Click(); Thread.Sleep(300);
                long c1 = Interlocked.Read(ref ownClicks);
                Out(string.Format("  [pass-torre #{0}] WindowFromPoint={1}/{2} ours={3} cliquesRecebidosPelaJanela={4}", rep, W.Proc(hit), W.Cls(hit), hit == hwnd, c1 - c0));
                if (chrome != null && chromeHwnd != IntPtr.Zero)
                {
                    bool before = W.IsIconic(chromeHwnd); IntPtr fb = W.GetForegroundWindow();
                    Move(chrome.CX, chrome.CY); Thread.Sleep(100);
                    hit = W.WindowFromPoint(new W.POINT { X = chrome.CX, Y = chrome.CY });
                    long d0 = Interlocked.Read(ref ownClicks); Click(); Thread.Sleep(600);
                    long d1 = Interlocked.Read(ref ownClicks);
                    IntPtr fa = W.GetForegroundWindow();
                    Out(string.Format("  [pass-icone #{0}] WindowFromPoint={1}/{2} cliquesNaJanela={3} fgAntes={4} fgDepois={5} chromeIconicAntes={6} depois={7}", rep, W.Proc(hit), W.Cls(hit), d1 - d0, W.Proc(fb), W.Proc(fa), before, W.IsIconic(chromeHwnd)));
                }
                Neutral();
            }
        }
        if (cases.Contains("inter"))
        {
            Out("== Interativo (so pixels de torre) ==");
            SetMode(false);
            for (int rep = 1; rep <= Math.Max(reps, 5); rep++)
            {
                Reset();
                int tx = towers[1][0] + towers[1][2] / 2, ty = towers[1][1] + towers[1][3] / 2;
                Move(tx, ty); Thread.Sleep(100);
                IntPtr hit = W.WindowFromPoint(new W.POINT { X = tx, Y = ty });
                long c0 = Interlocked.Read(ref ownClicks); Click(); Thread.Sleep(300);
                Out(string.Format("  [inter-torre #{0}] WindowFromPoint={1}/{2} ours={3} cliquesNaJanela={4}", rep, W.Proc(hit), W.Cls(hit), hit == hwnd, Interlocked.Read(ref ownClicks) - c0));
                Neutral();
                // espaco livre (alfa 0), fora do trilho
                int ex = emptyX, ey = bar.T + barH - 6;
                Move(ex, ey); Thread.Sleep(100);
                hit = W.WindowFromPoint(new W.POINT { X = ex, Y = ey });
                c0 = Interlocked.Read(ref ownClicks); Click(); Thread.Sleep(300);
                Out(string.Format("  [inter-livre #{0}] WindowFromPoint={1}/{2} ours={3} cliquesNaJanela={4}", rep, W.Proc(hit), W.Cls(hit), hit == hwnd, Interlocked.Read(ref ownClicks) - c0));
                Neutral();
                if (chrome != null && chromeHwnd != IntPtr.Zero)
                {
                    bool before = W.IsIconic(chromeHwnd); IntPtr fb = W.GetForegroundWindow();
                    Move(chrome.CX, chrome.CY + 8); Thread.Sleep(100);
                    hit = W.WindowFromPoint(new W.POINT { X = chrome.CX, Y = chrome.CY + 8 });
                    long d0 = Interlocked.Read(ref ownClicks); Click(); Thread.Sleep(600);
                    IntPtr fa = W.GetForegroundWindow();
                    Out(string.Format("  [inter-icone #{0}] WindowFromPoint={1}/{2} cliquesNaJanela={3} fgAntes={4} fgDepois={5} chromeIconicAntes={6} depois={7}", rep, W.Proc(hit), W.Cls(hit), Interlocked.Read(ref ownClicks) - d0, W.Proc(fb), W.Proc(fa), before, W.IsIconic(chromeHwnd)));
                    Neutral();
                }
            }
        }
        // ---- restauracao ----
        Key(0x1B); Thread.Sleep(200);
        if (chromeHwnd != IntPtr.Zero)
        {
            bool nowIconic = W.IsIconic(chromeHwnd);
            if (chromeWasIconic && !nowIconic) W.ShowWindow(chromeHwnd, 6); // SW_MINIMIZE
            else if (!chromeWasIconic && nowIconic) W.ShowWindow(chromeHwnd, 4); // SW_SHOWNOACTIVATE
        }
        // devolve foco ao terminal original
        var alt = new W.INPUT[2]; alt[0].type = 1; alt[0].u.ki.vk = 0x12; alt[1].type = 1; alt[1].u.ki.vk = 0x12; alt[1].u.ki.flags = 2; W.SendInput(2, alt, Marshal.SizeOf(typeof(W.INPUT)));
        W.SetForegroundWindow(origFg); Thread.Sleep(300);
        W.SetCursorPos(origPt.X, origPt.Y);
        Out("restaurado: fg=" + W.Proc(W.GetForegroundWindow()) + " chromeIconic=" + (chromeHwnd != IntPtr.Zero ? W.IsIconic(chromeHwnd).ToString() : "?") + " (inicial " + chromeWasIconic + ")");
    }

    static void Summary()
    {
        Out("\n== RESUMO (variante " + variant + ") ==");
        Out("caso | n | cobriu(n) | recuperacao px mediana/p95/max (ms) | recZ max | episodios max | coberto total max | swp med/max | maxGap(ms)");
        foreach (var g in results.GroupBy(r => r.Case))
        {
            var l = g.ToList(); var rp = l.Select(r => r.RecoverPx).OrderBy(x => x).ToList();
            double med = rp[rp.Count / 2], p95 = rp[Math.Min(rp.Count - 1, (int)Math.Ceiling(rp.Count * 0.95) - 1)];
            Out(string.Format("{0} | {1} | {2} | {3:F0}/{4:F0}/{5:F0} | {6:F0} | {7} | {8:F0} | {9:F1}/{10} | {11:F1}  notRecovered={12}",
                g.Key, l.Count, l.Count(r => r.CoverStart >= 0), med, p95, rp.Last(), l.Max(r => r.RecoverZ), l.Max(r => r.Episodes), l.Max(r => r.CoveredMs), l.Average(r => r.Swp), l.Max(r => r.Swp), l.Max(r => r.MaxGap), l.Count(r => !r.Recovered)));
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        W.SetProcessDpiAwarenessContext(new IntPtr(-4));
        W.timeBeginPeriod(1);
        tray = W.FindWindow("Shell_TrayWnd", null);
        sw = W.GetSystemMetrics(0); sh = W.GetSystemMetrics(1);
        W.RECT r; W.GetWindowRect(tray, out r);
        Console.WriteLine("Shell_TrayWnd rect=" + r.L + "," + r.T + "," + r.R + "," + r.B + " dpi=" + W.GetDpiForWindow(tray) + " screen=" + sw + "x" + sh + " os=" + Environment.OSVersion.Version);
        string mode = args.Length > 0 ? args[0] : "discover";
        if (mode == "discover")
        {
            foreach (var t in Scan()) Console.WriteLine(string.Format("{0} | cls={1} | id={2} | [{3},{4},{5},{6}]", t.Name, t.Cls, t.Id, t.L, t.T, t.R, t.B));
            return 0;
        }
        variant = args[1]; int reps = int.Parse(args[2]);
        var cases = new HashSet<string>((args.Length > 3 && !args[3].StartsWith("--") ? args[3] : "empty,icon,start,search,tray,thumbhover,thumbclick,burst").Split(','));
        string[] tx = null; int emptyX = 0;
        for (int i = 0; i < args.Length - 1; i++) { if (args[i] == "--towers") tx = args[i + 1].Split(','); if (args[i] == "--empty") emptyX = int.Parse(args[i + 1]); }
        lead = variant == "B" || variant == "C" || variant == "P"; wide = variant == "C" || variant == "P"; noReassert = variant == "N"; poll = variant == "P";
        Directory.CreateDirectory("out"); outLog = new StreamWriter("out\\run-" + variant + "-" + DateTime.Now.ToString("HHmmss") + ".log", false, new UTF8Encoding(false));
        Out("variante=" + variant + " lead=" + lead + " wide=" + wide + " noReassert=" + noReassert + " reps=" + reps + " casos=" + string.Join(",", cases.ToArray()) + " " + DateTime.Now.ToString("s"));
        foreach (var s in tx) towers.Add(new int[] { int.Parse(s), 0, 0, 0 });
        bool interactive = true;
        var ready = new ManualResetEvent(false);
        Thread worker = new Thread(() =>
        {
            ready.WaitOne();
            try
            {
                InitGrab();
                Thread.Sleep(300);
                Out("baseline visivel=" + Visible() + " acima=" + OursAboveTray() + " scale=" + scale + " bar=" + barW + "x" + barH);
                RunPlan(reps, cases, emptyX);
                Summary();
                Out("SWP total=" + swpCount);
            }
            catch (Exception e) { Out("ERRO: " + e); }
            W.PostMessage(hwndFocus, 0x10, IntPtr.Zero, IntPtr.Zero); W.PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero);
            W.PostMessage(hwnd, 0x12, IntPtr.Zero, IntPtr.Zero); // WM_QUIT
        });
        CreateScene(interactive); CreateFocusWindow();
        hook = W.SetWinEventHook(0x3, 0x3, IntPtr.Zero, hookProc, 0, 0, 0);
        IntPtr hook2 = W.SetWinEventHook(0x8004, 0x8004, IntPtr.Zero, hookProc, 0, 0, 0);
        IntPtr hook3 = W.SetWinEventHook(0x800B, 0x800B, IntPtr.Zero, hookProc, 0, 0, 0);
        if (poll) W.SetTimer(hwnd, new IntPtr(2), 50, IntPtr.Zero); // verificacao de z-order a cada 50 ms (variante P)
        worker.IsBackground = true; worker.Start(); ready.Set();
        W.MSG m;
        while (W.GetMessage(out m, IntPtr.Zero, 0, 0) > 0) { W.TranslateMessage(ref m); W.DispatchMessage(ref m); }
        W.UnhookWinEvent(hook); W.UnhookWinEvent(hook2); W.UnhookWinEvent(hook3);
        return 0;
    }
}
