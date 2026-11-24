using SideWar.Runtime.Panel;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Td;
using SkiaSharp;

namespace SideWar.Rendering.Panel;

/// <summary>Vista Configurações dentro do menu (engrenagem, lista de seções, linhas de controle); ver TdUiController.Config.</summary>
public sealed partial class TdUiRenderer
{
    /// <summary>Desenha o item se for da vista Configurações (ou a engrenagem/link que a abre); false = não é daqui.</summary>
    private bool DrawConfigItem(SKCanvas c, in TdUiItem it, bool hover)
    {
        var r = it.Rect;
        float cy = r.Y + (r.H - 14) / 2f;
        switch (it.SArg)
        {
            case "shadow-tile":
            {
                var edge = it.Selected ? Palette.Td.Amber : Palette.Td.EdgeSoft;
                RoundBox(c, r, edge, it.Selected ? Palette.Td.Amber.WithAlpha(36) : hover && it.Enabled ? Palette.Td.BorderStrong : Palette.Td.Card, 6);
                DrawIcon(c, TdIcon.Moon, r.X + 12, r.Y + (r.H - 14) / 2f, 14, it.Selected ? Palette.Td.Amber : Palette.Td.Muted);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 11, new DipRect(r.X + 34, cy, r.W - 38, 14), it.Selected ? Palette.Td.AmberLight : Palette.Td.Text);
                return true;
            }
            case "cfg-gear":
                DrawIcon(c, TdIcon.Gear, r.X + 10, r.Y + 10, 16, it.Selected ? Palette.Td.Amber : hover ? Palette.Td.Text : Palette.Td.Muted);
                return true;
            case "cfg-link":
            {
                RoundBox(c, r, Palette.Td.EdgeSoft, hover ? Palette.Td.BorderStrong : Palette.Td.Card, 6);
                DrawIcon(c, TdIcon.Gear, r.X + 12, r.Y + (r.H - 14) / 2f, 14, Palette.Td.Amber);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 11, new DipRect(r.X + 34, cy, r.W - 40, 14), Palette.Td.Text);
                return true;
            }
            case "cfg-section":
                RoundBox(c, r, it.Selected ? Palette.Td.BorderStrong : Palette.Td.EdgeSoft, it.Selected ? Palette.Td.BorderStrong : hover ? Palette.Td.Surface : Palette.Td.Card, 6);
                if (it.Selected) _p.Fill(c, new DipRect(r.X, r.Y + 6, 3, r.H - 12), Palette.Td.Amber);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 12, new DipRect(r.X + 14, r.Y + (r.H - 16) / 2f, r.W - 20, 16), it.Selected ? Palette.Td.BrightText : Palette.Td.Muted);
                return true;
            case "cfg-group": _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, r, Palette.Td.Muted); return true;
            case "cfg-info": _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 10, r, Palette.Td.Dim); return true;
            case "cfg-label":
                RoundBox(c, r, Palette.Td.EdgeSoft, Palette.Td.Card, 6);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 11, new DipRect(r.X + 12, cy, TdSettingsLayout.CtrlX - r.X - 20, 14), Palette.Td.Text);
                return true;
            case "cfg-button":
                RoundBox(c, r, Palette.Td.BorderStrong, hover ? Palette.Td.BorderStrong : Palette.Td.Card, 6);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 11, new DipRect(r.X, cy, r.W, 14), Palette.Td.Text, TextAlign.Center);
                return true;
        }
        switch (it.Action)
        {
            case TdUiAction.SettingToggle:
            {
                bool on = it.Selected;
                var fg = it.Enabled ? Palette.Td.Text : Palette.Td.Dim;
                RoundBox(c, r, Palette.Td.EdgeSoft, hover && it.Enabled ? Palette.Td.Surface : Palette.Td.Card, 6);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 11, new DipRect(r.X + 12, cy, TdSettingsLayout.CtrlX - r.X - 20, 14), fg);
                var track = on && it.Enabled ? Palette.Td.Amber : Palette.Td.WavePending;
                RoundBox(c, new DipRect(TdSettingsLayout.CtrlX, r.Y + (r.H - 14) / 2f, 26, 14), track, track, 7);
                RoundBox(c, new DipRect(TdSettingsLayout.CtrlX + (on ? 14 : 2), r.Y + (r.H - 10) / 2f, 10, 10), on ? Palette.Td.Background : Palette.Td.Muted, on ? Palette.Td.Background : Palette.Td.Muted, 5);
                _p.Text(c, on ? "Ligado" : "Desligado", PanelFont.SpaceGrotesk, 10, new DipRect(TdSettingsLayout.CtrlX + 34, cy, 90, 14), on && it.Enabled ? Palette.Td.Amber : Palette.Td.Dim);
                return true;
            }
            case TdUiAction.SettingChoice:
                RoundBox(c, r, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, it.Selected ? Palette.Td.Amber : hover ? Palette.Td.BorderStrong : Palette.Td.Background, 5);
                _p.Text(c, it.Text, PanelFont.JetBrainsMono, 10, new DipRect(r.X, r.Y + (r.H - 13) / 2f, r.W, 13), it.Selected ? Palette.Td.Background : Palette.Td.Muted, TextAlign.Center);
                return true;
            case TdUiAction.SettingSlider:
            {
                var fg = it.Enabled ? Palette.Td.Text : Palette.Td.Dim;
                RoundBox(c, r, Palette.Td.EdgeSoft, hover && it.Enabled ? Palette.Td.Surface : Palette.Td.Card, 6);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 11, new DipRect(r.X + 12, cy, TdSettingsLayout.CtrlX - r.X - 20, 14), fg);
                _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 10, new DipRect(TdSettingsLayout.CtrlX, cy, 46, 14), it.Enabled ? Palette.Td.BrightText : Palette.Td.Dim);
                float ty = r.Y + r.H / 2f - 2, fill = Math.Clamp(it.Fill, 0f, 1f);
                RoundBox(c, new DipRect(TdSettingsLayout.TrackX, ty, TdSettingsLayout.TrackW, 4), Palette.Td.BorderStrong, Palette.Td.BorderStrong, 2);
                if (fill > 0) _p.Fill(c, new DipRect(TdSettingsLayout.TrackX, ty, TdSettingsLayout.TrackW * fill, 4), it.Enabled ? Palette.Td.Amber : Palette.Td.Dim);
                var thumb = it.Enabled ? Palette.Td.Amber : Palette.Td.Dim;
                RoundBox(c, new DipRect(TdSettingsLayout.TrackX + TdSettingsLayout.TrackW * fill - 4, ty - 2, 8, 8), thumb, thumb, 4);
                return true;
            }
            case TdUiAction.SettingHotkey:
            {
                RoundBox(c, r, it.Tone == TdTone.Bad ? Palette.Td.Red : Palette.Td.EdgeSoft, hover ? Palette.Td.Surface : Palette.Td.Card, 6);
                bool err = !string.IsNullOrEmpty(it.Sub);
                _p.Text(c, it.Text, PanelFont.SpaceGrotesk, 11, new DipRect(r.X + 12, err ? r.Y + 5 : cy, TdSettingsLayout.CtrlX - r.X - 20, 14), Palette.Td.Text);
                if (err) _p.Text(c, it.Sub!, PanelFont.SpaceGrotesk, 9, new DipRect(r.X + 12, r.Y + 22, TdSettingsLayout.CtrlX - r.X - 20, 12), Palette.Td.Red);
                var chip = new DipRect(TdSettingsLayout.CtrlX, r.Y + 6, TdSettingsLayout.CtrlW, r.H - 12);
                RoundBox(c, chip, it.Selected ? Palette.Td.Amber : Palette.Td.BorderStrong, Palette.Td.Background, 5);
                _p.Text(c, it.Right ?? "", PanelFont.JetBrainsMono, 10, new DipRect(chip.X, chip.Y + (chip.H - 13) / 2f, chip.W, 13), it.Selected ? Palette.Td.Amber : Palette.Td.BrightText, TextAlign.Center);
                return true;
            }
        }
        return false;
    }
}
