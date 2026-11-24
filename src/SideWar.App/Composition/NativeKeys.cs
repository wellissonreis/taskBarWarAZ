using System.Runtime.InteropServices;

namespace SideWar.App.Composition;

/// <summary>Estado dos modificadores no instante da tecla (captura de atalho dentro do menu).</summary>
internal static class NativeKeys
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    public static (bool Ctrl, bool Alt, bool Shift, bool Win) Modifiers() => (Down(0x11), Down(0x12), Down(0x10), Down(0x5B) || Down(0x5C));
}
