using System.Diagnostics;
using SideWar.Platform.Windows.Interop;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Rendering;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Rendering;

/// <summary>
/// One DIB-backed layered surface per window (SDD-08 RF-08.1, RF-08.10, ADR-033). Exposes the DIB bits as a
/// BGRA8888 premultiplied <see cref="OverlayFrame"/> and presents with UpdateLayeredWindowIndirect.
/// Single-threaded: all members run on T-Game.
/// </summary>
public sealed class LayeredSurface : IOverlaySurface, IDisposable
{
    private readonly HWND window;
    private readonly Action<string> log;
    private HDC memoryDc;
    private HBITMAP bitmap;
    private HGDIOBJ previous;
    private nint bits;
    private bool inFrame;
    private bool dirtyFallbackLogged;
    private bool disposed;
    private byte opacity = 255;

    public LayeredSurface(nint hwnd, float scale = 1f, Action<string>? log = null)
    {
        if (hwnd == 0) throw new ArgumentException("A window handle is required.", nameof(hwnd));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
        window = new HWND(hwnd);
        Scale = scale;
        this.log = log ?? (m => Trace.WriteLine(m));
    }

    public PixelSize Size { get; private set; }
    public float Scale { get; set; }
    public byte Opacity => opacity;
    public bool HasBuffer => bits != 0;

    /// <summary>Sets the surface size; the DIB is (re)created lazily on the next <see cref="BeginFrame"/>.</summary>
    public void Resize(PixelSize size)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (inFrame) throw new InvalidOperationException("Resize inside a frame.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height);
        if (size == Size && bits != 0) return;
        Release();
        Size = size;
    }

    public unsafe OverlayFrame BeginFrame()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (inFrame) throw new InvalidOperationException("BeginFrame called twice.");
        if (Size.IsEmpty) throw new InvalidOperationException("Surface has no size; call Resize first.");
        if (bits == 0) Create();
        inFrame = true;
        return new OverlayFrame(bits, Size.Width, Size.Height, checked(Size.Width * 4));
    }

    public unsafe void EndFrame(PixelRect? dirty)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!inFrame) throw new InvalidOperationException("EndFrame without BeginFrame.");
        inFrame = false;
        PInvoke.GdiFlush();
        if (dirty is { } rect)
        {
            if (Update(new RECT { left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom })) return;
            if (!dirtyFallbackLogged)
            {
                dirtyFallbackLogged = true;
                log("UpdateLayeredWindowIndirect with prcDirty failed; retrying without it.");
            }
        }
        if (!Update(null)) throw Win32Error.LastError("UpdateLayeredWindowIndirect");
    }

    public void SetOpacity(byte constantAlpha)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        opacity = constantAlpha;
        if (bits == 0 || inFrame) return; // Applied by the next EndFrame.
        if (!Update(null)) throw Win32Error.LastError("UpdateLayeredWindowIndirect(opacity)");
    }

    /// <summary>Frees DIB and DC; the next <see cref="BeginFrame"/> recreates them (SDD-07 ReleaseCaches).</summary>
    public void Release()
    {
        inFrame = false;
        if (!previous.IsNull && !memoryDc.IsNull) PInvoke.SelectObject(memoryDc, previous);
        previous = default;
        if (!bitmap.IsNull) PInvoke.DeleteObject(bitmap);
        bitmap = default;
        if (!memoryDc.IsNull) PInvoke.DeleteDC(memoryDc);
        memoryDc = default;
        bits = 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        Release();
        disposed = true;
    }

    private unsafe void Create()
    {
        var info = new BITMAPINFO();
        info.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = Size.Width;
        info.bmiHeader.biHeight = -Size.Height; // Negative = top-down.
        info.bmiHeader.biPlanes = 1;
        info.bmiHeader.biBitCount = 32;
        info.bmiHeader.biCompression = (uint)BI_COMPRESSION.BI_RGB;
        HDC dc = default;
        HBITMAP dib = default;
        try
        {
            dc = new HDC(PInvoke.CreateCompatibleDC(default).Value);
            if (dc.IsNull) throw Win32Error.LastError("CreateCompatibleDC");
            void* pixels;
            dib = PInvoke.CreateDIBSection(default, &info, DIB_USAGE.DIB_RGB_COLORS, &pixels, default, 0);
            if (dib.IsNull || pixels is null) throw Win32Error.LastError("CreateDIBSection");
            var old = PInvoke.SelectObject(dc, dib);
            if (old.IsNull) throw Win32Error.LastError("SelectObject");
            memoryDc = dc;
            bitmap = dib;
            previous = old;
            bits = (nint)pixels;
        }
        catch
        {
            if (!dib.IsNull) PInvoke.DeleteObject(dib);
            if (!dc.IsNull) PInvoke.DeleteDC(dc);
            throw;
        }
    }

    private unsafe bool Update(RECT? dirty)
    {
        var size = new SIZE { cx = Size.Width, cy = Size.Height };
        var source = new System.Drawing.Point();
        var blend = new BLENDFUNCTION
        {
            BlendOp = (byte)PInvoke.AC_SRC_OVER,
            BlendFlags = 0,
            SourceConstantAlpha = opacity,
            AlphaFormat = (byte)PInvoke.AC_SRC_ALPHA,
        };
        var rect = dirty.GetValueOrDefault();
        var update = new UPDATELAYEREDWINDOWINFO
        {
            cbSize = (uint)sizeof(UPDATELAYEREDWINDOWINFO),
            hdcDst = default,
            pptDst = null, // Position is owned by SceneWindow.SetBounds.
            psize = &size,
            hdcSrc = memoryDc,
            pptSrc = &source,
            crKey = default,
            pblend = &blend,
            dwFlags = UPDATE_LAYERED_WINDOW_FLAGS.ULW_ALPHA,
            prcDirty = dirty.HasValue ? &rect : null,
        };
        return PInvoke.UpdateLayeredWindowIndirect(window, in update);
    }
}
