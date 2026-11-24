using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>
/// TD.40c: cartão de alerta no cantinho (SDD-38). Crítico (proteção contra derrota): fica até o jogador escolher "Salvar (voltar ao jogo)" ou
/// "Deixar cair", não recolhe ao clicar fora e não fecha com ESC. Aviso/dica: "Entendi" ou "Não pausar por este tipo"; some sozinho após
/// <see cref="AlertCardSeconds"/> s e recolhe ao clicar fora. Nunca ativa janela, não move o mouse; com a boss key nada aparece.
/// </summary>
public sealed partial class TdUiController
{
    public const double AlertCardSeconds = 2.5, AlertPulseSeconds = 3.0;
    public const float AlertWidth = 250f, AlertTitleFont = 11f, AlertFont = 10.5f, AlertButtonH = 20f;

    private int _acRaised;
    private double _acUntil, _acPulseUntil;
    private bool _acHidden;

    private TdAlertGuard? AlertGuard => _host.Alerts;

    /// <summary>Alerta crítico retido (a partida espera a escolha do jogador): nunca recolhe sozinho nem ao clicar fora.</summary>
    public bool AlertCritical => !BossHidden && !ModalOpen && AlertGuard is { Holding: true, HoldAlert: not null } && !_match().Shadow && AlertPhaseOk(_match());

    /// <summary>Cartão de alerta em exibição (crítico ou aviso/dica ainda dentro do tempo).</summary>
    public bool AlertCardOpen => AlertCritical || AlertWarningOpen;

    private bool AlertWarningOpen
    {
        get
        {
            if (BossHidden || _acHidden || ModalOpen || BuildActive || _match().Shadow || AlertGuard is not { Holding: false, Current: not null } g) return false;
            return AlertPhaseOk(_match()) && g.Raised == _acRaised && _now() < _acUntil && (Mode == TdUiMode.None || QuickTowersOpen);
        }
    }

    /// <summary>Pulso discreto (0..1) nos primeiros segundos de um alerta novo; 0 com o modo discreto (sem animação) ou fora do período.</summary>
    public float AlertPulse
    {
        get
        {
            if (!AlertCardOpen || _host.GameOptions.Discreet) return 0f;
            double left = _acPulseUntil - _now();
            if (left <= 0.0) return 0f;
            return (float)(0.5 + 0.5 * Math.Sin((AlertPulseSeconds - left) * 2.0 * Math.PI * 1.25));
        }
    }

    public bool AlertPulsing => AlertCardOpen && !_host.GameOptions.Discreet && _now() < _acPulseUntil;

    /// <summary>Alerta atual (crítico retido ou o último exibido) quando o cartão está aberto.</summary>
    public TdAlert? AlertShown => AlertCritical ? AlertGuard!.HoldAlert : AlertWarningOpen ? AlertGuard!.Current : null;

    private static bool AlertPhaseOk(TdMatchState m) => m.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    private void SyncAlertCard()
    {
        if (AlertGuard is not { } g) return;
        if (g.Raised == _acRaised) return;
        _acRaised = g.Raised;
        _acHidden = false;
        _outsideCollapsed = false; // alerta novo chama o jogador de volta mesmo após um clique fora
        double now = _now();
        _acUntil = now + AlertCardSeconds;
        _acPulseUntil = now + AlertPulseSeconds;
        Changed?.Invoke();
    }

    private static (string Title, TdTone Tone) AlertStyle(TdAlertSeverity s) => s switch
    {
        TdAlertSeverity.Critical => ("PERIGO", TdTone.Bad),
        TdAlertSeverity.Warning => ("AVISO", TdTone.Warn),
        _ => ("DICA", TdTone.Accent),
    };

    private void ActivateAlert(TdUiAction action)
    {
        var g = AlertGuard;
        if (g is null) return;
        var m = _match();
        switch (action)
        {
            case TdUiAction.AlertSave: _host.SaveTower(); _acHidden = true; break;
            case TdUiAction.AlertFall: _host.LetTowerFall(); _acHidden = true; break;
            case TdUiAction.AlertDismiss:
                if (g.Holding) g.Dismiss(m, false);
                _acHidden = true;
                break;
            case TdUiAction.AlertMute:
                var type = AlertShown?.Type;
                if (g.Holding) g.Dismiss(m, true);
                else if (type is { } t) g.Settings.SetMuted(t, true);
                _acHidden = true;
                _host.AlertsMutedChanged(g.Settings.MutedMask);
                break;
        }
        Changed?.Invoke();
    }

    private TdPopoverView BuildAlertCard()
    {
        var alert = AlertShown!.Value;
        var (title, tone) = AlertStyle(alert.Severity);
        bool fatal = alert.Severity == TdAlertSeverity.Critical && TdAlertSettings.IsProtection(alert.Type);
        var s = new Stack(AlertWidth);
        QuizText(s, alert.Message, AlertTitleFont, tone, TdIcon.Alert);
        QuizText(s, "O que fazer: " + alert.Action, AlertFont, TdTone.Dim);
        if (fatal) QuizText(s, "A partida está pausada. Só cai se você escolher.", AlertFont);
        float bw = s.Width - 2 * Pad;
        void Button(string text, TdUiAction act, TdTone t)
        {
            s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, bw, AlertButtonH), TdUiKind.Button, text, act, Tone: t, FontSize: AlertFont));
            s.Y += AlertButtonH + 3f;
        }
        if (fatal)
        {
            Button("Salvar (voltar ao jogo)", TdUiAction.AlertSave, TdTone.Good);
            Button("Deixar cair", TdUiAction.AlertFall, TdTone.Bad);
        }
        else
        {
            Button("Entendi", TdUiAction.AlertDismiss, TdTone.Accent);
            Button("Não pausar por este tipo", TdUiAction.AlertMute, TdTone.Normal);
        }
        var items = new List<TdUiItem>(s.Items.Count + 1);
        if (!fatal) items.Add(new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.AlertDismiss));
        items.AddRange(s.Items);
        return new TdPopoverView(title, [.. items], s.Width, s.Y + Pad, 0f, AlertTone: tone, Pulse: AlertPulse);
    }
}
