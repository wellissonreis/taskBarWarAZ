using SideWar.Runtime.Input;
using Xunit;

namespace SideWar.Runtime.Tests;

public sealed class HotkeyParserTests
{
    private const HotkeyModifiers C = HotkeyModifiers.Ctrl, A = HotkeyModifiers.Alt, S = HotkeyModifiers.Shift, W = HotkeyModifiers.Win;

    public static TheoryData<string, HotkeyModifiers, uint, string> Valid => new()
    {
        { "Ctrl+Alt+G", C | A, 0x47, "Ctrl+Alt+G" },
        { "ctrl+alt+h", C | A, 0x48, "Ctrl+Alt+H" },
        { "Alt+Ctrl+P", C | A, 0x50, "Ctrl+Alt+P" },
        { "Control+Alt+P", C | A, 0x50, "Ctrl+Alt+P" },
        { " Ctrl + Shift + F12 ", C | S, 0x7B, "Ctrl+Shift+F12" },
        { "Win+Shift+Alt+Ctrl+5", C | A | S | W, 0x35, "Ctrl+Alt+Shift+Win+5" },
        { "Ctrl+Alt+Space", C | A, 0x20, "Ctrl+Alt+Space" },
        { "Ctrl+Escape", C, 0x1B, "Ctrl+Esc" },
        { "Alt+F1", A, 0x70, "Alt+F1" },
        { "Alt+F24", A, 0x87, "Alt+F24" },
        { "Ctrl+PgDn", C, 0x22, "Ctrl+PageDown" },
        { "Shift+Left", S, 0x25, "Shift+Left" },
    };

    [Theory, MemberData(nameof(Valid))]
    public void ParsesAndFormatsCanonically(string text, HotkeyModifiers mods, uint vk, string canonical)
    {
        Assert.True(HotkeyParser.TryParse(text, out var chord, out var error), error);
        Assert.Equal(new HotkeyChord(mods, vk), chord);
        Assert.Equal(canonical, HotkeyParser.Format(chord));
        Assert.True(HotkeyParser.TryParse(canonical, out var again, out _));
        Assert.Equal(chord, again);
    }

    [Theory]
    [InlineData(null, "Atalho vazio")]
    [InlineData("", "Atalho vazio")]
    [InlineData("   ", "Atalho vazio")]
    [InlineData("G", "ao menos um modificador")]
    [InlineData("Ctrl+Alt", "Tecla desconhecida: Alt")]
    [InlineData("Ctrl+Alt+", "Tecla ausente")]
    [InlineData("Ctrl++G", "Modificador vazio")]
    [InlineData("Ctrl+Ctrl+G", "Modificador repetido")]
    [InlineData("Meta+G", "Modificador desconhecido: Meta")]
    [InlineData("Ctrl+Alt+Ç", "Tecla desconhecida")]
    [InlineData("Ctrl+F25", "Tecla desconhecida")]
    [InlineData("Ctrl+Alt+GG", "Tecla desconhecida")]
    [InlineData("G+Ctrl", "Modificador desconhecido: G")]
    public void RejectsInvalidWithPortugueseError(string? text, string expectedPart)
    {
        Assert.False(HotkeyParser.TryParse(text, out var chord, out var error));
        Assert.Equal(default, chord);
        Assert.Contains(expectedPart, error);
    }

    [Fact]
    public void FormatRejectsUnnamedKey() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HotkeyParser.Format(new HotkeyChord(C, 0xE7)));

    [Fact]
    public void ModifierValuesMatchWin32()
    {
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Ctrl);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Win);
    }
}
