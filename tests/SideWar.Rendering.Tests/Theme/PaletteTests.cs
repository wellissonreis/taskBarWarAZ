using System.Reflection;
using System.Text.RegularExpressions;
using SideWar.Rendering.Theme;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Theme;

public class PaletteTests
{
    // (token, campo, ARGB) transcritos de SDD-20 RF-20.1; alfa rgba convertido para 0-255.
    private static readonly (string Token, string Field, uint Argb)[] Tokens =
    [
        ("bg-abismo", "BgAbismo", 0xFF050E1Bu),
        ("bg-trilho-fundo", "BgTrilhoFundo", 0xFF071323u),
        ("bg-painel", "BgPainel", 0xFF0B1A2Eu),
        ("bg-item", "BgItem", 0xFF0D1F37u),
        ("bg-card", "BgCard", 0xFF0F2440u),
        ("bg-ausente", "BgAusente", 0xFF0D1C30u),
        ("bg-trilho", "BgTrilho", 0xFF16304Du),
        ("borda-painel", "BordaPainel", 0xFF1F3350u),
        ("borda-forte", "BordaForte", 0xFF2C4468u),
        ("txt-principal", "TxtPrincipal", 0xFFE8F0FFu),
        ("txt-secundario", "TxtSecundario", 0xFFB8C8E0u),
        ("txt-terciario", "TxtTerciario", 0xFF9FB2CCu),
        ("txt-rotulo", "TxtRotulo", 0xFF8AA0C0u),
        ("txt-widget-rotulo", "TxtWidgetRotulo", 0xFFA9B6C8u),
        ("txt-botao-neutro", "TxtBotaoNeutro", 0xFFC9D6EAu),
        ("txt-branco", "TxtBranco", 0xFFFFFFFFu),
        ("ambar", "Ambar", 0xFFF5B335u),
        ("ambar-hover", "AmbarHover", 0xFFFFC139u),
        ("ambar-anel", "AmbarAnel", 0xB3F5B335u),
        ("menta", "Menta", 0xFF5CF0B0u),
        ("menta-fundo", "MentaFundo", 0xFF0F2A2Au),
        ("menta-borda", "MentaBorda", 0xFF2F7A62u),
        ("menta-faixa", "MentaFaixa", 0xFF12403Au),
        ("menta-texto", "MentaTexto", 0xFFB8D8CCu),
        ("azul", "Azul", 0xFF3AA0FFu),
        ("azul-claro", "AzulClaro", 0xFF9FD3FFu),
        ("gelo", "Gelo", 0xFF7FE7FFu),
        ("perigo", "Perigo", 0xFFFF5A7Au),
        ("perigo-claro", "PerigoClaro", 0xFFFF9BB0u),
        ("perigo-texto", "PerigoTexto", 0xFFFFD0DAu),
        ("perigo-fundo", "PerigoFundo", 0xFF2A1420u),
        ("perigo-borda", "PerigoBorda", 0xFF7A2F48u),
        ("perigo-trilho", "PerigoTrilho", 0xFF3A0D1Du),
        ("perigo-tinta", "PerigoTinta", 0xFF1A0610u),
        ("prestigio", "Prestigio", 0xFF8F74FFu),
        ("prestigio-hover", "PrestigioHover", 0xFF9A7DFFu),
        ("prestigio-claro", "PrestigioClaro", 0xFFC9B8FFu),
        ("prestigio-fundo", "PrestigioFundo", 0xFF1A1636u),
        ("prestigio-borda", "PrestigioBorda", 0xFF5D4BA0u),
        ("prestigio-texto", "PrestigioTexto", 0xFFCFC6EEu),
        ("prestigio-tinta", "PrestigioTinta", 0xFF0B0720u),
        ("borda-presente", "BordaPresente", 0xFFF5B335u),
        ("borda-ausente", "BordaAusente", 0xFF3A4558u),
        ("borda-chefe", "BordaChefe", 0xFFFF5A7Au),
        ("selo-presente", "SeloPresente", 0xFF5CF0B0u),
        ("selo-ausente", "SeloAusente", 0xFFA9B6C8u),
        ("ausente-marca", "AusenteMarca", 0xFF8AA0C0u),
        ("hover-nav", "HoverNav", 0x1AFFFFFFu),
        ("ativo-nav", "AtivoNav", 0x1FFFFFFFu),
        ("sombra-painel", "SombraPainel", 0x59000000u),
        ("sombra-card", "SombraCard", 0x73000000u),
        ("sombra-texto", "SombraTexto", 0xFF000000u),
    ];

    [Fact]
    public void EveryToken_ExistsWithExactHex()
    {
        foreach (var (token, field, argb) in Tokens)
        {
            var f = typeof(Palette).GetField(field, BindingFlags.Public | BindingFlags.Static);
            Assert.True(f is not null, $"token {token} ausente");
            Assert.Equal((SKColor)argb, (SKColor)f!.GetValue(null)!);
        }
    }

    [Fact]
    public void Palette_HasNoExtraTokens()
    {
        var fields = typeof(Palette).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.Equal(Tokens.Length, fields.Length);
        Assert.All(fields, f => Assert.True(f.IsInitOnly && f.FieldType == typeof(SKColor)));
    }

    [Fact]
    public void Aliases_EqualTheirTargets()
    {
        Assert.Equal(Palette.Ambar, Palette.BordaPresente);
        Assert.Equal(Palette.Perigo, Palette.BordaChefe);
        Assert.Equal(Palette.Menta, Palette.SeloPresente);
        Assert.Equal(Palette.TxtWidgetRotulo, Palette.SeloAusente);
        Assert.Equal(Palette.TxtRotulo, Palette.AusenteMarca);
    }

    [Fact]
    public void Hover_MatchesPrecomputedTokens()
    {
        Assert.Equal(Palette.AmbarHover, Palette.Hover(Palette.Ambar));
        Assert.Equal(Palette.PrestigioHover, Palette.Hover(Palette.Prestigio));
        Assert.Equal(new SKColor(255, 255, 255), Palette.Hover(SKColors.White));
    }

    [Fact]
    public void NoColorLiteralsOutsidePalette()
    {
        var root = FindRoot();
        var rx = new Regex(@"new\s+SKColor\s*\(|SKColor\.Parse|0x[0-9A-Fa-f]{6,8}u?\b|#[0-9A-Fa-f]{6}\b");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "SideWar.Rendering"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (Path.GetFileName(file) == "Palette.cs") continue;
            foreach (var line in File.ReadLines(file))
                if (rx.IsMatch(line)) offenders.Add($"{Path.GetFileName(file)}: {line.Trim()}");
        }
        Assert.Empty(offenders);
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "SideWar.Rendering"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("raiz do repo nao encontrada");
    }
}
