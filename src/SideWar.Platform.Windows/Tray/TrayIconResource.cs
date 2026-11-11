using System.Reflection;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;
using Windows.Win32.Foundation;

namespace SideWar.Platform.Windows.Tray;

/// <summary>Loads the embedded PNG-in-ICO at the size closest to the requested one.</summary>
internal static class TrayIconResource
{
    internal static IReadOnlyList<int> EmbeddedSizes()
    {
        var data = ReadIco();
        var sizes = new List<int>();
        if (data is null) return sizes;
        int count = BitConverter.ToUInt16(data, 4);
        for (int i = 0; i < count; i++) sizes.Add(data[6 + i * 16] is 0 ? 256 : data[6 + i * 16]);
        return sizes;
    }

    private static byte[]? ReadIco()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SideWar.Tray.ico");
        if (stream is null) return null;
        var buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>Returns a created icon handle (caller destroys it) or default if unavailable.</summary>
    internal static unsafe HICON Load(int wanted)
    {
        var data = ReadIco();
        if (data is null || data.Length < 6) return default;
        int count = BitConverter.ToUInt16(data, 4);
        int best = -1, bestSize = 0;
        for (int i = 0; i < count; i++)
        {
            int size = data[6 + i * 16] is 0 ? 256 : data[6 + i * 16];
            if (best < 0 || (size >= wanted && (bestSize < wanted || size < bestSize)) || (bestSize < wanted && size > bestSize))
            {
                best = i;
                bestSize = size;
            }
        }
        if (best < 0) return default;
        int length = BitConverter.ToInt32(data, 6 + best * 16 + 8), offset = BitConverter.ToInt32(data, 6 + best * 16 + 12);
        if (offset < 0 || length <= 0 || offset + length > data.Length) return default;
        fixed (byte* bits = &data[offset])
            return PInvoke.CreateIconFromResourceEx(bits, (uint)length, true, 0x00030000, bestSize, bestSize, IMAGE_FLAGS.LR_DEFAULTCOLOR);
    }
}
