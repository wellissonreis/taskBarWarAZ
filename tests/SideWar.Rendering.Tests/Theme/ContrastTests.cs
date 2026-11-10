using SideWar.Rendering.Theme;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Theme;

public class ContrastTests
{
    // Pares de SDD-20 RF-20.8: (texto, fundo, razao publicada, minimo, nome).
    private static readonly (SKColor Fg, SKColor Bg, double Published, double Min, string Name)[] Pairs =
    [
        (Palette.TxtPrincipal, Palette.BgPainel, 15.26, Contrast.MinText, "txt-principal / bg-painel"),
        (Palette.Ambar, Palette.BgPainel, 9.47, Contrast.MinText, "ambar / bg-painel"),
        (Palette.Menta, Palette.BgPainel, 12.11, Contrast.MinText, "menta / bg-painel"),
        (Palette.TxtSecundario, Palette.BgItem, 9.76, Contrast.MinText, "txt-secundario / bg-item"),
        (Palette.TxtSecundario, Palette.BgCard, 9.19, Contrast.MinText, "txt-secundario / bg-card"),
        (Palette.TxtTerciario, Palette.BgCard, 7.21, Contrast.MinText, "txt-terciario / bg-card"),
        (Palette.TxtRotulo, Palette.BgItem, 6.21, Contrast.MinText, "txt-rotulo / bg-item"),
        (Palette.TxtRotulo, Palette.BgPainel, 6.55, Contrast.MinText, "txt-rotulo / bg-painel"),
        (Palette.TxtRotulo, Palette.BgCard, 5.84, Contrast.MinText, "txt-rotulo / bg-card"),
        (Palette.TxtRotulo, Palette.BgTrilho, 5.03, Contrast.MinText, "txt-rotulo / bg-trilho"),
        (Palette.TxtWidgetRotulo, Palette.BgPainel, 8.50, Contrast.MinText, "txt-widget-rotulo / bg-painel"),
        (Palette.AzulClaro, Palette.BgCard, 9.83, Contrast.MinText, "azul-claro / bg-card"),
        (Palette.PerigoClaro, Palette.BgPainel, 8.80, Contrast.MinText, "perigo-claro / bg-painel"),
        (Palette.PerigoTexto, Palette.PerigoFundo, 12.54, Contrast.MinText, "perigo-texto / perigo-fundo"),
        (Palette.PerigoClaro, Palette.PerigoFundo, 8.67, Contrast.MinText, "perigo-claro / perigo-fundo"),
        (Palette.BgTrilhoFundo, Palette.Ambar, 10.10, Contrast.MinText, "bg-trilho-fundo / ambar"),
        (Palette.BgTrilhoFundo, Palette.Menta, 12.91, Contrast.MinText, "bg-trilho-fundo / menta"),
        (Palette.MentaTexto, Palette.MentaFundo, 9.92, Contrast.MinText, "menta-texto / menta-fundo"),
        (Palette.MentaTexto, Palette.MentaFaixa, 7.54, Contrast.MinText, "menta-texto / menta-faixa"),
        (Palette.Menta, Palette.MentaFundo, 10.50, Contrast.MinText, "menta / menta-fundo"),
        (Palette.PerigoTinta, Palette.Perigo, 6.49, Contrast.MinText, "perigo-tinta / perigo"),
        (Palette.PerigoClaro, Palette.PerigoTinta, 9.81, Contrast.MinText, "perigo-claro / perigo-tinta"),
        (Palette.Ambar, Palette.BgTrilhoFundo, 10.10, Contrast.MinText, "ambar / bg-trilho-fundo"),
        (Palette.PrestigioTinta, Palette.Prestigio, 5.73, Contrast.MinText, "prestigio-tinta / prestigio"),
        (Palette.PrestigioClaro, Palette.PrestigioFundo, 9.72, Contrast.MinText, "prestigio-claro / prestigio-fundo"),
        (Palette.PrestigioTexto, Palette.PrestigioFundo, 10.70, Contrast.MinText, "prestigio-texto / prestigio-fundo"),
        (Palette.TxtBotaoNeutro, Palette.BgPainel, 11.89, Contrast.MinText, "txt-botao-neutro / bg-painel"),
        (Palette.TxtPrincipal, Palette.BgItem, 14.46, Contrast.MinText, "txt-principal / bg-item"),
        (Palette.TxtPrincipal, Palette.BgCard, 13.62, Contrast.MinText, "txt-principal / bg-card"),
        (Palette.TxtPrincipal, Palette.MentaFundo, 13.24, Contrast.MinText, "txt-principal / menta-fundo"),
        (Palette.TxtSecundario, Palette.BgAusente, 10.10, Contrast.MinText, "txt-secundario / bg-ausente"),
        (Palette.TxtRotulo, Palette.BgAusente, 6.42, Contrast.MinText, "txt-rotulo / bg-ausente"),
        (Palette.TxtWidgetRotulo, Palette.BgAusente, 8.33, Contrast.MinText, "txt-widget-rotulo / bg-ausente"),
        (Palette.Perigo, Palette.BgPainel, 5.82, Contrast.MinGraphic, "perigo / bg-painel"),
        (Palette.Azul, Palette.BgTrilho, 4.89, Contrast.MinGraphic, "azul / bg-trilho"),
        (Palette.Ambar, Palette.BgTrilho, 7.28, Contrast.MinGraphic, "ambar / bg-trilho"),
    ];

    public static TheoryData<int> Indexes()
    {
        var d = new TheoryData<int>();
        for (int i = 0; i < Pairs.Length; i++) d.Add(i);
        return d;
    }

    [Theory]
    [MemberData(nameof(Indexes))]
    public void Pair_MeetsMinimum_AndMatchesPublishedRatio(int i)
    {
        var (fg, bg, published, min, name) = Pairs[i];
        double r = Contrast.Ratio(fg, bg);
        Assert.True(r >= min, $"{name}: {r:F2} < {min}");
        Assert.InRange(r, published - 0.02, published + 0.02);
    }

    [Fact]
    public void Ratio_IsSymmetric_AndBlackWhiteIs21()
    {
        Assert.Equal(21.0, Contrast.Ratio(SKColors.Black, SKColors.White), 6);
        Assert.Equal(Contrast.Ratio(Palette.Ambar, Palette.BgPainel), Contrast.Ratio(Palette.BgPainel, Palette.Ambar), 12);
        Assert.Equal(1.0, Contrast.Ratio(Palette.Ambar, Palette.Ambar), 12);
    }

    [Fact]
    public void ForbiddenPair_TrilhoFundoOverMentaFaixa_IsRejected()
    {
        double r = Contrast.Ratio(Palette.BgTrilhoFundo, Palette.MentaFaixa);
        Assert.InRange(r, 1.60, 1.64);
        Assert.True(r < Contrast.MinGraphic);
    }

    [Fact]
    public void WidgetAlpha230_WorstCase_StaysAboveMinimum()
    {
        // Fundo efetivo do widget sobre branco (#233042) conforme RF-20.8.
        var bg = new SKColor(0x23, 0x30, 0x42);
        foreach (var fg in new[] { Palette.Ambar, Palette.Menta, Palette.TxtWidgetRotulo, Palette.TxtPrincipal })
            Assert.True(Contrast.Ratio(fg, bg) >= Contrast.MinText);
    }
}
