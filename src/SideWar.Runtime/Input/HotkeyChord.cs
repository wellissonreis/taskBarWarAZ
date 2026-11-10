namespace SideWar.Runtime.Input;

/// <summary>Modifier flags of a global hotkey. Values match Win32 MOD_ALT/CONTROL/SHIFT/WIN.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>A parsed hotkey: modifiers plus a Windows virtual-key code.</summary>
public readonly record struct HotkeyChord(HotkeyModifiers Modifiers, uint VirtualKey);
