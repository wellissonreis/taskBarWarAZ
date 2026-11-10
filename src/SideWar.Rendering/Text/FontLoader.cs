using System.Reflection;
using SkiaSharp;

namespace SideWar.Rendering.Text;

/// <summary>
/// Carrega os TTF embutidos como <see cref="SKTypeface"/> (SDD-20 RF-20.2, RNF-20.5: sem rede).
/// Ausencia do recurso ou TTF invalido e falha cedo (excecao de inicializacao).
/// </summary>
public static class FontLoader
{
    /// <summary>Nome do recurso embutido da face.</summary>
    public static string ResourceName(FontRole role) => "SideWar.Rendering.Fonts." + role switch
    {
        FontRole.SilkscreenRegular => "Silkscreen-Regular.ttf",
        FontRole.SilkscreenBold => "Silkscreen-Bold.ttf",
        FontRole.JetBrainsMonoRegular => "JetBrainsMono-Regular.ttf",
        FontRole.JetBrainsMonoSemiBold => "JetBrainsMono-SemiBold.ttf",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>Cria o typeface (o chamador e dono e deve descartar).</summary>
    public static SKTypeface Load(FontRole role)
    {
        string name = ResourceName(role);
        using Stream stream = typeof(FontLoader).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Recurso de fonte ausente: {name}");
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data)
            ?? throw new InvalidOperationException($"TTF invalido: {name}");
    }
}
