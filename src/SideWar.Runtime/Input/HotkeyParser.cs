using System.Globalization;

namespace SideWar.Runtime.Input;

/// <summary>
/// Pure parser for the textual hotkey format <c>Ctrl+Alt+Shift+Win+&lt;Key&gt;</c> (SDD-15 RF-15.6).
/// Case-insensitive; modifiers in any order on input, canonical order on output. At least one modifier is required.
/// </summary>
public static class HotkeyParser
{
    private static readonly (string Name, uint Vk)[] NamedKeys =
    [
        ("Space", 0x20), ("Enter", 0x0D), ("Tab", 0x09), ("Esc", 0x1B), ("Backspace", 0x08),
        ("Insert", 0x2D), ("Delete", 0x2E), ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
    ];

    private static readonly Dictionary<string, uint> KeyByName = BuildKeyByName();

    private static Dictionary<string, uint> BuildKeyByName()
    {
        var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, vk) in NamedKeys) d[name] = vk;
        d["Escape"] = 0x1B; d["PgUp"] = 0x21; d["PgDn"] = 0x22; d["Del"] = 0x2E; d["Ins"] = 0x2D; d["Return"] = 0x0D;
        for (char c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) d[c.ToString()] = c;
        for (uint f = 1; f <= 24; f++) d["F" + f.ToString(CultureInfo.InvariantCulture)] = 0x6F + f;
        return d;
    }

    public static bool TryParse(string? text, out HotkeyChord chord, out string error)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) { error = "Atalho vazio"; return false; }
        var parts = text.Split('+');
        if (parts.Length < 2) { error = "O atalho precisa de ao menos um modificador e uma tecla"; return false; }

        var mods = HotkeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var token = parts[i].Trim();
            HotkeyModifiers m;
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || token.Equals("Control", StringComparison.OrdinalIgnoreCase)) m = HotkeyModifiers.Ctrl;
            else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) m = HotkeyModifiers.Alt;
            else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) m = HotkeyModifiers.Shift;
            else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase)) m = HotkeyModifiers.Win;
            else { error = token.Length == 0 ? "Modificador vazio" : $"Modificador desconhecido: {token}"; return false; }
            if ((mods & m) != 0) { error = $"Modificador repetido: {token}"; return false; }
            mods |= m;
        }

        var key = parts[^1].Trim();
        if (key.Length == 0) { error = "Tecla ausente"; return false; }
        if (!KeyByName.TryGetValue(key, out var vk)) { error = $"Tecla desconhecida: {key}"; return false; }

        chord = new HotkeyChord(mods, vk);
        error = string.Empty;
        return true;
    }

    /// <summary>Canonical text, e.g. <c>Ctrl+Alt+G</c>. Throws for a key without a name.</summary>
    public static string Format(HotkeyChord chord)
    {
        var sb = new System.Text.StringBuilder();
        if ((chord.Modifiers & HotkeyModifiers.Ctrl) != 0) sb.Append("Ctrl+");
        if ((chord.Modifiers & HotkeyModifiers.Alt) != 0) sb.Append("Alt+");
        if ((chord.Modifiers & HotkeyModifiers.Shift) != 0) sb.Append("Shift+");
        if ((chord.Modifiers & HotkeyModifiers.Win) != 0) sb.Append("Win+");
        sb.Append(KeyName(chord.VirtualKey));
        return sb.ToString();
    }

    private static string KeyName(uint vk)
    {
        if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString();
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
        foreach (var (name, code) in NamedKeys) if (code == vk) return name;
        throw new ArgumentOutOfRangeException(nameof(vk), $"Tecla sem nome: 0x{vk:X}");
    }
}
