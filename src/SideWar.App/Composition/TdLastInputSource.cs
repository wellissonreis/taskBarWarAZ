using System.Runtime.InteropServices;
using SideWar.Runtime.Td;

namespace SideWar.App.Composition;

/// <summary>
/// Sinal "presente agora" da caixa-bomba (RF-28.19): segundos desde a última entrada de mouse/teclado do sistema
/// (<c>GetLastInputInfo</c>). Só um sinal binário no host; não gera crédito nem acumula tempo.
/// </summary>
internal sealed class TdLastInputSource : ITdInputSource
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }

#pragma warning disable SYSLIB1054 // DllImport simples: struct blittable, sem necessidade de gerador
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
#pragma warning restore SYSLIB1054

    public double SecondsSinceLastInput()
    {
        var info = new LastInputInfo { CbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return double.PositiveInfinity; // sem sinal: ausente
        uint now = unchecked((uint)Environment.TickCount); // mesma base de 32 bits que dwTime
        return unchecked(now - info.DwTime) / 1000.0;
    }
}
