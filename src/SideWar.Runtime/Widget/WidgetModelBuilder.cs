using System.Globalization;
using SideWar.Core.Economy;
using SideWar.Runtime.Presence;

namespace SideWar.Runtime.Widget;

/// <summary>Valores brutos de entrada do widget (créditos/taxa ainda não formatados).</summary>
public readonly record struct WidgetInputs(
    double Credits,
    double Rate,
    int Wave,
    int WaveTotal,
    PresenceState Presence,
    double Multiplier,
    int BonusSecondsLeft,
    double Pending,
    int MissionBadge,
    WidgetTab? ActiveTab,
    int HoverIndex);

/// <summary>
/// Constrói o <see cref="WidgetModel"/> (SDD-23 RF-23.9). A versão sobe só quando um texto exibido muda;
/// créditos e taxa são coalescidos em no máximo 2 atualizações por segundo (<see cref="MinTextInterval"/>).
/// </summary>
public sealed class WidgetModelBuilder
{
    public static readonly TimeSpan MinTextInterval = TimeSpan.FromMilliseconds(500);

    private WidgetModel _model = WidgetModel.Empty;
    private TimeSpan? _lastNumbers;

    public WidgetModel Current => _model;

    /// <summary>Atualiza o modelo e devolve o resultado; <paramref name="now"/> é monotônico (IClock).</summary>
    public WidgetModel Update(in WidgetInputs i, TimeSpan now)
    {
        string credits = _model.CreditsText, rate = _model.RateText;
        if (_lastNumbers is null || now - _lastNumbers >= MinTextInterval)
        {
            credits = CreditFormatter.Format(i.Credits);
            rate = "+" + CreditFormatter.Format(i.Rate) + "/s";
            _lastNumbers = now;
        }

        var next = _model with
        {
            CreditsText = credits,
            RateText = rate,
            WaveText = i.Wave.ToString(CultureInfo.InvariantCulture) + "/" + i.WaveTotal.ToString(CultureInfo.InvariantCulture),
            IsPresent = i.Presence == PresenceState.Presente,
            MultiplierText = FormatMultiplier(i.Multiplier),
            BonusChipText = i.BonusSecondsLeft > 0 ? FormatBonus(i.BonusSecondsLeft) : null,
            PendingText = i.Pending > 0 ? "+" + CreditFormatter.Format(i.Pending) : null,
            MissionBadge = Math.Max(0, i.MissionBadge),
            ActiveTab = i.ActiveTab,
            HoverIndex = i.HoverIndex is >= 0 and < WidgetModel.NavButtonCount ? i.HoverIndex : -1,
        };
        if (!SameDisplay(_model, next))
            _model = next with { Version = _model.Version + 1 };
        return _model;
    }

    /// <summary>"×2,0", "×1,5", "×2,5", "×0,25": vírgula decimal (ADR-029), 1 casa quando exata, senão 2.</summary>
    public static string FormatMultiplier(double m)
    {
        double r1 = Math.Round(m, 1, MidpointRounding.AwayFromZero);
        string s = Math.Abs(m - r1) < 1e-9
            ? r1.ToString("0.0", CultureInfo.InvariantCulture)
            : m.ToString("0.##", CultureInfo.InvariantCulture);
        return "×" + s.Replace('.', ',');
    }

    /// <summary>"×3 0:59" (ADR-032).</summary>
    public static string FormatBonus(int secondsLeft)
    {
        int s = Math.Clamp(secondsLeft, 0, 5999);
        return "×3 " + (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
    }

    private static bool SameDisplay(in WidgetModel a, in WidgetModel b) =>
        a.CreditsText == b.CreditsText && a.RateText == b.RateText && a.WaveText == b.WaveText &&
        a.IsPresent == b.IsPresent && a.MultiplierText == b.MultiplierText && a.BonusChipText == b.BonusChipText &&
        a.PendingText == b.PendingText && a.MissionBadge == b.MissionBadge && a.ActiveTab == b.ActiveTab &&
        a.HoverIndex == b.HoverIndex;
}
