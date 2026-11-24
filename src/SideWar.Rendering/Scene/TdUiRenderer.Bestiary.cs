using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

public sealed partial class TdUiRenderer
{
    private readonly Dictionary<string, SKImage> _bestiaryImages = [];

    private void BestiaryImage(SKCanvas c, string id, DipRect r, int frame, bool known)
    {
        if (!_bestiaryImages.TryGetValue(id, out var img))
        {
            using var stream = typeof(TdUiRenderer).Assembly.GetManifestResourceStream("SideWar.Rendering.Assets.Bestiary." + id + ".png");
            if (stream is null) return;
            using var bitmap = SKBitmap.Decode(stream);
            img = SKImage.FromBitmap(bitmap);
            _bestiaryImages[id] = img;
        }
        float available = Math.Min(_p.Px(r.W), _p.Px(r.H));
        float size = Math.Max(32, MathF.Floor(available / 32) * 32);
        float x = MathF.Round(_p.Px(r.X) + (_p.Px(r.W) - size) / 2);
        float y = MathF.Round(_p.Px(r.Y) + (_p.Px(r.H) - size) / 2);
        using var filter = known ? null : SKColorFilter.CreateBlendMode(Palette.Td.BorderStrong, SKBlendMode.SrcIn);
        using var paint = new SKPaint { IsAntialias = false, ColorFilter = filter };
        c.DrawImage(img, SKRect.Create(Math.Clamp(frame, 0, 3) * 32, 0, 32, 32), SKRect.Create(x, y, size, size), Nearest, paint);
    }

    private bool DrawBestiaryItem(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        if (it.Action == TdUiAction.BestiarySelect)
        {
            RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, hover ? Palette.Td.Button : Palette.Td.Card, 6);
            _ = int.TryParse(it.Right, out int frame);
            BestiaryImage(c, it.SArg ?? "", new DipRect(r.X + 2, r.Y + 2, r.W - 4, 48), frame, it.Fill > 0);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 9, new DipRect(r.X + 2, r.Y + 55, r.W - 4, 16), it.Fill > 0 ? Palette.Td.Text : Palette.Td.Muted, TextAlign.Center);
            return true;
        }
        if (it.SArg is not { } tag || !tag.StartsWith("best-", StringComparison.Ordinal)) return false;
        if (tag == "best-panel") RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.Card, 8);
        else if (tag == "best-popup")
        {
            RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.Surface, 6);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 9, new DipRect(r.X + 6, r.Y + 4, r.W - 12, 14), Palette.Td.Muted);
        }
        else if (tag.StartsWith("best-image:", StringComparison.Ordinal))
        {
            RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.Background, 6);
            BestiaryImage(c, tag[11..], r, it.Arg, true);
        }
        else if (tag.StartsWith("best-variant:", StringComparison.Ordinal))
        {
            RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.Background, 4);
            BestiaryImage(c, tag[13..], r, it.Arg, it.Fill > 0);
            if (it.Fill == 0) DrawIcon(c, TdIcon.Lock, r.X + 27, r.Y + 27, 12, Palette.Td.Muted);
        }
        else if (tag.StartsWith("best-tower:", StringComparison.Ordinal))
        {
            RoundBox(c, r, Palette.Td.BorderStrong, Palette.Td.Background, 4);
            DrawTowerIcon(c, tag[11..], r.X + 2, r.Y + 2, 26, 255);
        }
        else if (tag == "best-locked")
        {
            DrawIcon(c, TdIcon.Lock, r.X + (r.W - 22) / 2, r.Y + 16, 22, Palette.Td.Muted);
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, new DipRect(r.X, r.Y + 54, r.W, 22), Palette.Td.Muted, TextAlign.Center);
        }
        else if (tag == "best-heading")
        {
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, r, Palette.Td.Muted);
            _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 10, r, Palette.Td.Amber, TextAlign.Right);
        }
        else if (tag == "best-stat")
        {
            _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, new DipRect(r.X, r.Y, 96, r.H), Palette.Td.Muted);
            for (int i = 0; i < 5; i++)
                RoundBox(c, new DipRect(r.X + 104 + i * 17, r.Y + 3, 13, 10), Palette.Td.BorderStrong,
                    i < it.Arg ? Palette.Td.Amber : Palette.Td.Background, 2);
        }
        else DrawWrappedSized(c, it.Text, r, tag == "best-title" ? Palette.Td.Text : tag == "best-weak" ? Palette.Td.Green : tag == "best-resist" ? Palette.Td.Red : Palette.Td.Muted, tag == "best-title" ? 15 : 10);
        return true;
    }
}
