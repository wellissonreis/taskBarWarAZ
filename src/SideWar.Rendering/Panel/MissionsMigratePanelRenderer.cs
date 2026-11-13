using SideWar.Rendering.Icons;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>
/// Renderer Skia puro do painel nas abas Missões e Migrar (SDD-24 RF-24.3, RF-24.7, RF-24.8, RF-24.13), para plugar em
/// <c>OverlayWindow.Renderer(OverlayFrame, OverlayRenderContext)</c> (T5.3). Desenha o frame completo da janela
/// (caixa 400 × 548 + margem de sombra de 6 DIP) em pixel físico: sombra, borda, fundo, corpo com clip, barra de
/// rolagem e cabeçalho por último. Não anima. O cromo aqui é o mínimo para as duas abas; a integração pode unificá-lo
/// com o das outras abas (T5.4).
/// </summary>
public sealed class MissionsMigratePanelRenderer(Typography typography, IconCatalog icons, int dpi) : IDisposable
{
    private readonly PanelPainter p = new(typography, dpi);
    private readonly PanelTextMetricsSkia tm = new(typography);
    private readonly SKPaint iconPaint = new() { IsAntialias = false };

    /// <summary>Tamanho físico da janela: <c>round(400 × s) + round(6 × s)</c> × <c>round(548 × s) + round(6 × s)</c> (RF-24.2).</summary>
    public (int W, int H) WindowSize => ((int)(p.Px(PanelGeometry.PanelWidthDip) + p.Px(6)), (int)(p.Px(PanelGeometry.PanelHeightDip) + p.Px(6)));

    public void DrawMissions(SKCanvas canvas, MissionsTabModel model, MissionsLayout layout, PanelViewState view, TabBodyHit hover)
    {
        BeginChrome(canvas, view);
        MissionsBody(canvas, model, layout, hover);
        EndChrome(canvas, MissionsTabModel.TabTitle, view);
    }

    public void DrawMigrate(SKCanvas canvas, MigrateTabModel model, MigrateLayout layout, PanelViewState view, TabBodyHit hover)
    {
        BeginChrome(canvas, view);
        MigrateBody(canvas, model, layout, hover);
        EndChrome(canvas, MigrateTabModel.TabTitle, view);
    }

    // Sem delegate para o corpo (um closure por frame alocaria, RF-08.8): o chamador intercala Begin/Corpo/End.
    private void BeginChrome(SKCanvas c, PanelViewState view)
    {
        c.Clear(SKColors.Transparent);
        var box = p.Rect(0, 0, PanelGeometry.PanelWidthDip, PanelGeometry.PanelHeightDip);
        p.Shadow(c, box, Palette.SombraPainel);
        p.Fill(c, box, Palette.BordaPainel);
        var b = p.Px(2);
        p.Fill(c, new SKRect(box.Left + b, box.Top + b, box.Right - b, box.Bottom - b), Palette.BgPainel);

        // Corpo rolável, recortado ao viewport (x 2..398, y 55..55+viewport).
        c.Save();
        c.ClipRect(p.Rect(2, PanelScroll.ViewportTopDip, 396, view.Viewport));
        p.OffsetY = view.Offset;
    }

    private void EndChrome(SKCanvas c, string title, PanelViewState view)
    {
        p.OffsetY = 0;
        c.Restore();

        Scrollbar(c, view);

        // Cabeçalho por último: filete de 1 DIP em y = 54 e título + fechar.
        p.Fill(c, new DipRect(2, 54, 396, 1), Palette.BordaPainel);
        var lh = tm.LineHeight(PanelFont.Silkscreen, 14);
        p.Text(c, title, PanelFont.Silkscreen, 14, new DipRect(18, 2 + (52 - lh) / 2f, 330, lh), Palette.Ambar, letterSpacing: 1f);
        iconPaint.Color = Palette.TxtRotulo;
        icons.DrawTinted(c, IconId.Fechar, dpi, p.Px(PanelChromeHit.CloseX + 22), p.Px(PanelChromeHit.CloseY + 22), iconPaint);
    }

    private void Scrollbar(SKCanvas c, PanelViewState view)
    {
        if (PanelScroll.Thumb(view.Content, view.Viewport, view.Offset) is not { } t) return;
        var trackH = PanelScroll.TrackHeight(view.Viewport);
        p.Fill(c, new DipRect(PanelScroll.TrackXDip, PanelScroll.TrackTopDip, PanelScroll.TrackWidthDip, trackH), Palette.BgTrilho);
        var hot = view.Dragging || view.Hover == PanelHit.ScrollThumb;
        p.Fill(c, new DipRect(PanelScroll.TrackXDip, PanelScroll.TrackTopDip + t.Top, PanelScroll.TrackWidthDip, t.Height),
            hot ? Palette.TxtSecundario : Palette.TxtRotulo);
    }

    private static bool Visible(DipRect r, float offset, float viewport) =>
        r.Y + r.H - offset > PanelScroll.ViewportTopDip && r.Y - offset < PanelScroll.ViewportTopDip + viewport;

    private void MissionsBody(SKCanvas c, MissionsTabModel m, MissionsLayout l, TabBodyHit hover)
    {
        // 7.1 card de presença
        p.Box(c, l.PresenceCard, Palette.MentaBorda, Palette.MentaFundo);
        p.Text(c, m.Title, PanelFont.Silkscreen, 12, l.TitleBox, Palette.Menta);
        p.Text(c, m.Multiplier, PanelFont.Silkscreen, 16, l.MultiplierBox, m.MultiplierMuted ? Palette.TxtWidgetRotulo : Palette.Menta, TextAlign.Right);
        var lh11 = tm.LineHeight(PanelFont.JetBrainsMono, 11);
        for (var i = 0; i < l.ParagraphLines.Length; i++)
            p.Text(c, l.ParagraphLines[i], PanelFont.JetBrainsMono, 11, new DipRect(l.ParagraphBox.X, l.ParagraphBox.Y + i * lh11, l.ParagraphBox.W, lh11), Palette.MentaTexto);
        var lh10 = tm.LineHeight(PanelFont.JetBrainsMono, 10);
        for (var i = 0; i < l.Bands.Length && i < m.Bands.Count; i++)
        {
            var band = m.Bands[i];
            // Par proibido bg-trilho-fundo / menta-faixa (RF-20.8): faixa inativa usa menta-texto.
            p.Fill(c, l.Bands[i], band.Current ? Palette.Menta : Palette.MentaFaixa);
            var fg = band.Current ? Palette.BgTrilhoFundo : Palette.MentaTexto;
            var r = l.Bands[i];
            p.Text(c, band.Range, PanelFont.JetBrainsMono, 10, new DipRect(r.X, r.Y + 6, r.W, lh10), fg, TextAlign.Center);
            p.Text(c, band.Multiplier, PanelFont.JetBrainsMono, 10, new DipRect(r.X, r.Y + 6 + lh10, r.W, lh10), fg, TextAlign.Center);
        }
        // 7.2 rótulo
        p.Text(c, m.SectionLabel, PanelFont.JetBrainsMono, 10, l.SectionBox, Palette.TxtRotulo, letterSpacing: 1f);
        // 7.3 linhas
        for (var i = 0; i < l.Rows.Length; i++)
        {
            var r = l.Rows[i];
            if (!Visible(r.Row, p.OffsetY, 491f)) continue;
            var mission = m.Missions[i];
            p.Box(c, r.Row, Palette.BordaPainel, Palette.BgItem);
            p.Text(c, r.Title, PanelFont.JetBrainsMono, 12, r.TitleBox, Palette.TxtPrincipal);
            p.Fill(c, r.BarTrack, Palette.BgTrilho);
            if (r.BarFill > 0) p.Fill(c, new DipRect(r.BarTrack.X, r.BarTrack.Y, r.BarFill, r.BarTrack.H), Palette.Ambar);
            p.Text(c, mission.ProgressText, PanelFont.JetBrainsMono, 10, r.ProgressBox, mission.Collected ? Palette.Menta : Palette.TxtRotulo);
            if (r.Collect is { } btn)
            {
                var hot = hover.Kind == TabBodyHitKind.MissionCollect && hover.Index == i;
                p.Fill(c, btn, hot ? Palette.AmbarHover : Palette.Ambar);
                var lh = tm.LineHeight(PanelFont.Silkscreen, 10);
                p.Text(c, "COLETAR", PanelFont.Silkscreen, 10, new DipRect(btn.X, btn.Y + (btn.H - lh) / 2f, btn.W, lh), Palette.BgTrilhoFundo, TextAlign.Center);
            }
        }
    }

    private void MigrateBody(SKCanvas c, MigrateTabModel m, MigrateLayout l, TabBodyHit hover)
    {
        p.Box(c, l.Card, Palette.PrestigioBorda, Palette.PrestigioFundo);
        p.Text(c, MigrateTabModel.CardTitle, PanelFont.Silkscreen, 13, l.CardTitleBox, Palette.PrestigioClaro);
        var lh = tm.LineHeight(PanelFont.JetBrainsMono, 11);
        for (var i = 0; i < l.CardBodyLines.Length; i++)
        {
            var top = l.CardBodyBox.Y + i * MigrateLayout.CardBodyLineHeight + (MigrateLayout.CardBodyLineHeight - lh) / 2f;
            p.Text(c, l.CardBodyLines[i], PanelFont.JetBrainsMono, 11, new DipRect(l.CardBodyBox.X, top, l.CardBodyBox.W, lh), Palette.PrestigioTexto);
        }
        for (var i = 0; i < 2; i++)
        {
            p.Box(c, l.Tiles[i], Palette.BordaPainel, Palette.BgItem);
            p.Text(c, i == 0 ? MigrateTabModel.TileCurrentLabel : MigrateTabModel.TileGainLabel, PanelFont.JetBrainsMono, 10, l.TileLabelBoxes[i], Palette.TxtRotulo);
            p.Text(c, i == 0 ? m.CurrentReserves : m.GainNow, PanelFont.Silkscreen, 18, l.TileNumberBoxes[i], i == 0 ? Palette.TxtPrincipal : Palette.PrestigioClaro);
            p.Text(c, i == 0 ? m.CurrentProduction : m.TotalProduction, PanelFont.JetBrainsMono, 10, l.TileSubBoxes[i], Palette.Menta);
        }
        p.Text(c, MigrateTabModel.RegionsLabel, PanelFont.JetBrainsMono, 10, l.RegionsLabelBox, Palette.TxtRotulo, letterSpacing: 1f);
        for (var i = 0; i < l.Regions.Length; i++)
        {
            var tone = m.Regions[i].Tone;
            var color = tone switch { RegionTone.Principal => Palette.TxtPrincipal, RegionTone.Secundario => Palette.TxtSecundario, _ => Palette.TxtRotulo };
            p.Text(c, l.Regions[i].Text, PanelFont.JetBrainsMono, 11, l.Regions[i].TextBox, color);
        }
        var hot = hover.Kind == TabBodyHitKind.Migrate && m.Button == MigrateButtonState.Normal;
        var (bg, fg) = m.Button switch
        {
            MigrateButtonState.Armed => (Palette.Perigo, Palette.PerigoTinta),
            MigrateButtonState.Unavailable => (Palette.BgTrilho, Palette.TxtRotulo),
            _ => (hot ? Palette.PrestigioHover : Palette.Prestigio, Palette.PrestigioTinta),
        };
        p.Fill(c, l.Button, bg);
        var blh = tm.LineHeight(PanelFont.Silkscreen, 12);
        p.Text(c, m.ButtonLabel, PanelFont.Silkscreen, 12, new DipRect(l.Button.X, l.Button.Y + (l.Button.H - blh) / 2f, l.Button.W, blh), fg, TextAlign.Center, 1f);
    }

    public void Dispose()
    {
        p.Dispose();
        iconPaint.Dispose();
    }
}
