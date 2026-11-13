using SkiaSharp;

namespace SideWar.Rendering.Theme;

/// <summary>
/// Tokens de cor de SDD-20 RF-20.1. Unica fonte de literais de cor do jogo.
/// <c>SKColor</c> pre-criado (zero alocacao por frame). Alfa em escala 0-255
/// (rgba .35 -> 89, .45 -> 115, .7 -> 179, .08 -> 20, .12 -> 31).
/// <c>bg-barra-so</c> nao e token de jogo (RF-20.1) e nao existe aqui.
/// </summary>
public static class Palette
{
    /// <summary>Token <c>bg-abismo</c> (#050e1b).</summary>
    public static readonly SKColor BgAbismo = new(0x05, 0x0e, 0x1b);

    /// <summary>Token <c>bg-trilho-fundo</c> (#071323).</summary>
    public static readonly SKColor BgTrilhoFundo = new(0x07, 0x13, 0x23);

    /// <summary>Token <c>bg-painel</c> (#0b1a2e).</summary>
    public static readonly SKColor BgPainel = new(0x0b, 0x1a, 0x2e);

    /// <summary>Token <c>bg-item</c> (#0d1f37).</summary>
    public static readonly SKColor BgItem = new(0x0d, 0x1f, 0x37);

    /// <summary>Token <c>bg-card</c> (#0f2440).</summary>
    public static readonly SKColor BgCard = new(0x0f, 0x24, 0x40);

    /// <summary>Token <c>bg-ausente</c> (#0d1c30).</summary>
    public static readonly SKColor BgAusente = new(0x0d, 0x1c, 0x30);

    /// <summary>Token <c>bg-trilho</c> (#16304d).</summary>
    public static readonly SKColor BgTrilho = new(0x16, 0x30, 0x4d);

    /// <summary>Token <c>borda-painel</c> (#1f3350).</summary>
    public static readonly SKColor BordaPainel = new(0x1f, 0x33, 0x50);

    /// <summary>Token <c>borda-forte</c> (#2c4468).</summary>
    public static readonly SKColor BordaForte = new(0x2c, 0x44, 0x68);

    /// <summary>Token <c>txt-principal</c> (#e8f0ff).</summary>
    public static readonly SKColor TxtPrincipal = new(0xe8, 0xf0, 0xff);

    /// <summary>Token <c>txt-secundario</c> (#b8c8e0).</summary>
    public static readonly SKColor TxtSecundario = new(0xb8, 0xc8, 0xe0);

    /// <summary>Token <c>txt-terciario</c> (#9fb2cc).</summary>
    public static readonly SKColor TxtTerciario = new(0x9f, 0xb2, 0xcc);

    /// <summary>Token <c>txt-rotulo</c> (#8aa0c0).</summary>
    public static readonly SKColor TxtRotulo = new(0x8a, 0xa0, 0xc0);

    /// <summary>Token <c>txt-widget-rotulo</c> (#a9b6c8).</summary>
    public static readonly SKColor TxtWidgetRotulo = new(0xa9, 0xb6, 0xc8);

    /// <summary>Token <c>txt-botao-neutro</c> (#c9d6ea).</summary>
    public static readonly SKColor TxtBotaoNeutro = new(0xc9, 0xd6, 0xea);

    /// <summary>Token <c>txt-branco</c> (#ffffff).</summary>
    public static readonly SKColor TxtBranco = new(0xff, 0xff, 0xff);

    /// <summary>Token <c>ambar</c> (#f5b335).</summary>
    public static readonly SKColor Ambar = new(0xf5, 0xb3, 0x35);

    /// <summary>Token <c>ambar-hover</c> (#ffc139).</summary>
    public static readonly SKColor AmbarHover = new(0xff, 0xc1, 0x39);

    /// <summary>Token <c>ambar-anel</c> (#f5b335, alfa 179).</summary>
    public static readonly SKColor AmbarAnel = new(0xf5, 0xb3, 0x35, 179);

    /// <summary>Token <c>menta</c> (#5cf0b0).</summary>
    public static readonly SKColor Menta = new(0x5c, 0xf0, 0xb0);

    /// <summary>Token <c>menta-fundo</c> (#0f2a2a).</summary>
    public static readonly SKColor MentaFundo = new(0x0f, 0x2a, 0x2a);

    /// <summary>Token <c>menta-borda</c> (#2f7a62).</summary>
    public static readonly SKColor MentaBorda = new(0x2f, 0x7a, 0x62);

    /// <summary>Token <c>menta-faixa</c> (#12403a).</summary>
    public static readonly SKColor MentaFaixa = new(0x12, 0x40, 0x3a);

    /// <summary>Token <c>menta-texto</c> (#b8d8cc).</summary>
    public static readonly SKColor MentaTexto = new(0xb8, 0xd8, 0xcc);

    /// <summary>Token <c>azul</c> (#3aa0ff).</summary>
    public static readonly SKColor Azul = new(0x3a, 0xa0, 0xff);

    /// <summary>Token <c>azul-claro</c> (#9fd3ff).</summary>
    public static readonly SKColor AzulClaro = new(0x9f, 0xd3, 0xff);

    /// <summary>Token <c>gelo</c> (#7fe7ff).</summary>
    public static readonly SKColor Gelo = new(0x7f, 0xe7, 0xff);

    /// <summary>Token <c>perigo</c> (#ff5a7a).</summary>
    public static readonly SKColor Perigo = new(0xff, 0x5a, 0x7a);

    /// <summary>Token <c>perigo-claro</c> (#ff9bb0).</summary>
    public static readonly SKColor PerigoClaro = new(0xff, 0x9b, 0xb0);

    /// <summary>Token <c>perigo-texto</c> (#ffd0da).</summary>
    public static readonly SKColor PerigoTexto = new(0xff, 0xd0, 0xda);

    /// <summary>Token <c>perigo-fundo</c> (#2a1420).</summary>
    public static readonly SKColor PerigoFundo = new(0x2a, 0x14, 0x20);

    /// <summary>Token <c>perigo-borda</c> (#7a2f48).</summary>
    public static readonly SKColor PerigoBorda = new(0x7a, 0x2f, 0x48);

    /// <summary>Token <c>perigo-trilho</c> (#3a0d1d).</summary>
    public static readonly SKColor PerigoTrilho = new(0x3a, 0x0d, 0x1d);

    /// <summary>Token <c>perigo-tinta</c> (#1a0610).</summary>
    public static readonly SKColor PerigoTinta = new(0x1a, 0x06, 0x10);

    /// <summary>Token <c>prestigio</c> (#8f74ff).</summary>
    public static readonly SKColor Prestigio = new(0x8f, 0x74, 0xff);

    /// <summary>Token <c>prestigio-hover</c> (#9a7dff).</summary>
    public static readonly SKColor PrestigioHover = new(0x9a, 0x7d, 0xff);

    /// <summary>Token <c>prestigio-claro</c> (#c9b8ff).</summary>
    public static readonly SKColor PrestigioClaro = new(0xc9, 0xb8, 0xff);

    /// <summary>Token <c>prestigio-fundo</c> (#1a1636).</summary>
    public static readonly SKColor PrestigioFundo = new(0x1a, 0x16, 0x36);

    /// <summary>Token <c>prestigio-borda</c> (#5d4ba0).</summary>
    public static readonly SKColor PrestigioBorda = new(0x5d, 0x4b, 0xa0);

    /// <summary>Token <c>prestigio-texto</c> (#cfc6ee).</summary>
    public static readonly SKColor PrestigioTexto = new(0xcf, 0xc6, 0xee);

    /// <summary>Token <c>prestigio-tinta</c> (#0b0720).</summary>
    public static readonly SKColor PrestigioTinta = new(0x0b, 0x07, 0x20);

    /// <summary>Token <c>borda-presente</c> (#f5b335).</summary>
    public static readonly SKColor BordaPresente = new(0xf5, 0xb3, 0x35);

    /// <summary>Token <c>borda-ausente</c> (#3a4558).</summary>
    public static readonly SKColor BordaAusente = new(0x3a, 0x45, 0x58);

    /// <summary>Token <c>borda-chefe</c> (#ff5a7a).</summary>
    public static readonly SKColor BordaChefe = new(0xff, 0x5a, 0x7a);

    /// <summary>Token <c>selo-presente</c> (#5cf0b0).</summary>
    public static readonly SKColor SeloPresente = new(0x5c, 0xf0, 0xb0);

    /// <summary>Token <c>selo-ausente</c> (#a9b6c8).</summary>
    public static readonly SKColor SeloAusente = new(0xa9, 0xb6, 0xc8);

    /// <summary>Token <c>ausente-marca</c> (#8aa0c0).</summary>
    public static readonly SKColor AusenteMarca = new(0x8a, 0xa0, 0xc0);

    /// <summary>Token <c>hover-nav</c> (#ffffff, alfa 20).</summary>
    public static readonly SKColor HoverNav = new(0xff, 0xff, 0xff, 26);

    /// <summary>Token <c>ativo-nav</c> (#ffffff, alfa 31).</summary>
    public static readonly SKColor AtivoNav = new(0xff, 0xff, 0xff, 31);

    /// <summary>Token <c>sombra-painel</c> (#000000, alfa 89).</summary>
    public static readonly SKColor SombraPainel = new(0x00, 0x00, 0x00, 89);

    /// <summary>Token <c>sombra-card</c> (#000000, alfa 115).</summary>
    public static readonly SKColor SombraCard = new(0x00, 0x00, 0x00, 115);

    /// <summary>Token <c>sombra-texto</c> (#000000).</summary>
    public static readonly SKColor SombraTexto = new(0x00, 0x00, 0x00);

    /// <summary>Cores do <c>PlaceholderAtlas</c> (T4.3): provisorias ate o atlas embutido definitivo; fora dos tokens de SDD-20.</summary>
    public static class Placeholder
    {
        public static readonly SKColor TowerBolt = new(0x53, 0xd9, 0xf2);
        public static readonly SKColor TowerFrost = new(0x9d, 0xea, 0xff);
        public static readonly SKColor TowerCannon = new(0xff, 0xb3, 0x6d);
        public static readonly SKColor TowerScaleSet = new(0xb4, 0x8c, 0xff);
        public static readonly SKColor SlotEmpty = new(0x3a, 0x43, 0x58);
        public static readonly SKColor Core = new(0x6a, 0x76, 0x90);
        public static readonly SKColor Portal = new(0x8a, 0x5c, 0xff);
        public static readonly SKColor SupplyCrate = new(0xd9, 0xa4, 0x41);
        public static readonly SKColor Enemy = new(0xc0, 0x4a, 0x56);
        public static readonly SKColor Edge = new(0x10, 0x14, 0x20);
    }

    /// <summary>Hover: canal x 1,08, arredondado, teto 255 (RF-20.1).</summary>
    public static SKColor Hover(SKColor c) =>
        new(Scale(c.Red), Scale(c.Green), Scale(c.Blue), c.Alpha);

    private static byte Scale(byte v) => (byte)Math.Min(255, (int)Math.Round(v * 1.08, MidpointRounding.AwayFromZero));
}
