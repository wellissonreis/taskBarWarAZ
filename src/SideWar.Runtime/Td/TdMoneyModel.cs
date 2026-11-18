using System.Globalization;

namespace SideWar.Runtime.Td;

/// <summary>Número flutuante "+N" já pronto para desenhar: subida em DIP (0 a <see cref="TdMoneyModel.FloatRiseDip"/>) e alfa (âmbar translúcido que some).</summary>
public readonly record struct TdMoneyFloat(string Text, float Rise, byte Alpha);

/// <summary>
/// TD.33d (SDD-33 peça E): modelo de APRESENTAÇÃO do dinheiro. Só lê créditos, o <c>Frame</c> da simulação (relógio de jogo da renda) e o
/// relógio de apresentação; não tem estado no Core, nada no hash, nem comandos (RF-33.23). Contagem animada do valor exibido (ease-out,
/// 0,4 a 0,6 s; queda imediata), "+N" agregados em 0,4 s (no máximo 3 vivos), renda (média de 300 ticks), brilho "dá para comprar" com
/// histerese, marcos de patrimônio (×10) e modo discreto. O relógio interno só anda quando <c>frozen</c> é falso (pausa congela). Puro, testável.
/// </summary>
public sealed class TdMoneyModel
{
    public const double CountSeconds = 0.4, MaxCountSeconds = 0.6, FloatSeconds = 0.7, AggregateSeconds = 0.4, BuyGlowSeconds = 0.8, MilestoneGlowSeconds = 1.0;
    public const int MaxFloats = 3, IncomeWindowTicks = 300;
    public const long BigGainCredits = 50, FirstMilestone = 1000;
    public const float FloatRiseDip = 12f;
    private const double MaxStep = 0.25;
    private const double RefundGrace = 1.5;

    private struct Fl { public double Start; public long Amount; }

    private readonly List<Fl> _floats = new(MaxFloats);
    private readonly Queue<(long Frame, long Amount)> _gains = new();
    private long _incomeSum;
    private double _clock, _lastNow;
    private bool _hasNow;
    private long _frame = -1;
    private long _real;
    private long _animFrom, _animTo;
    private double _animStart, _animDur;
    private bool _counting;
    private long _threshold;
    private bool _affordable;
    private double _buyStart = double.NegativeInfinity, _mileStart = double.NegativeInfinity;
    private long _nextMilestone = FirstMilestone;
    private double _refundUntil = double.NegativeInfinity;
    private bool _animating, _frozen;

    /// <summary>Modo discreto (RF-33.21): valor salta, sem brilhos; "+N" só nos ganhos grandes.</summary>
    public bool Discreet { get; set; }

    /// <summary>Renda "+X/s" ligada (RF-33.20). Desligada: nada é acumulado nem calculado.</summary>
    public bool ShowIncome { get; set; }

    /// <summary>Valor real (o do Core): é ele que vale para comprar.</summary>
    public long Real => _real;

    /// <summary>Valor desenhado (sobe suavemente até <see cref="Real"/>).</summary>
    public long Display { get; private set; }

    /// <summary>Há algo a animar agora (contagem, "+N" visíveis ou brilho) e o relógio está andando. Falso = zero redesenho agendado pela peça.</summary>
    public bool Animating => _animating;

    public string? IncomeText { get; private set; }

    /// <summary>Intensidade 0..1 do brilho do botão Torres (dá para comprar).</summary>
    public float BuyGlow { get; private set; }

    /// <summary>Intensidade 0..1 do brilho do contador (marco de patrimônio).</summary>
    public float MilestoneGlow { get; private set; }

    public IReadOnlyList<TdMoneyFloat> Floats { get; private set; } = [];

    /// <summary>Recomeça no valor real, sem animar nem gerar "+N" (nova partida, volta da boss key).</summary>
    public void Reset(long credits, long frame, long threshold, double now)
    {
        _real = credits; Display = credits; _counting = false; _frozen = false;
        _frame = frame; _lastNow = now; _hasNow = true;
        _floats.Clear(); _gains.Clear(); _incomeSum = 0;
        _buyStart = _mileStart = double.NegativeInfinity;
        _refundUntil = double.NegativeInfinity;
        _threshold = threshold; _affordable = threshold > 0 && credits >= threshold;
        _nextMilestone = FirstMilestone;
        while (_nextMilestone <= credits) _nextMilestone *= 10;
        Recompute();
    }

    /// <summary>Venda: o reembolso não conta como ganho (sem "+N" nem renda).</summary>
    public void NoteRefund() => _refundUntil = _clock + RefundGrace;

    /// <param name="threshold">Custo da torre selecionada (ou da mais barata desbloqueada); 0 = sem limiar.</param>
    public void Update(long credits, long frame, double now, bool frozen, long threshold)
    {
        if (!_hasNow || frame < _frame) { Reset(credits, frame, threshold, now); return; }
        double dt = Math.Clamp(now - _lastNow, 0.0, MaxStep);
        _lastNow = now;
        _frozen = frozen;
        if (!frozen) _clock += dt;
        _frame = frame;

        if (threshold != _threshold) { _threshold = threshold; _affordable = threshold > 0 && credits >= threshold; }

        if (credits > _real) OnGain(credits - _real, credits);
        else if (credits < _real) { Display = credits; _counting = false; }
        _real = credits;
        if (threshold > 0 && credits < threshold) _affordable = false;

        if (Discreet) { Display = credits; _counting = false; }
        if (_counting)
        {
            double t = (_clock - _animStart) / _animDur;
            if (t >= 1.0) { Display = _animTo; _counting = false; }
            else
            {
                double e = 1.0 - Math.Pow(1.0 - Math.Max(0.0, t), 3);
                Display = Math.Min(_animTo, _animFrom + (long)((_animTo - _animFrom) * e));
            }
        }
        Recompute();
    }

    private void OnGain(long gain, long credits)
    {
        bool refund = _clock <= _refundUntil;
        if (refund) _refundUntil = double.NegativeInfinity;
        else
        {
            AddFloat(gain);
            if (ShowIncome) { _gains.Enqueue((_frame, gain)); _incomeSum += gain; }
        }
        if (Discreet) Display = credits;
        else
        {
            _animFrom = Display; _animTo = credits; _animStart = _clock;
            _animDur = Math.Clamp(CountSeconds + (MaxCountSeconds - CountSeconds) * gain / 500.0, CountSeconds, MaxCountSeconds);
            _counting = true;
        }
        if (!Discreet && _threshold > 0 && !_affordable && credits >= _threshold) _buyStart = _clock;
        if (_threshold > 0 && credits >= _threshold) _affordable = true;
        if (credits >= _nextMilestone)
        {
            if (!Discreet) _mileStart = _clock;
            while (_nextMilestone <= credits) _nextMilestone *= 10;
        }
    }

    private void AddFloat(long gain)
    {
        int n = _floats.Count;
        if (n > 0 && (_clock - _floats[n - 1].Start <= AggregateSeconds || n >= MaxFloats))
        {
            var last = _floats[n - 1];
            last.Amount += gain;
            _floats[n - 1] = last;
        }
        else _floats.Add(new Fl { Start = _clock, Amount = gain });
    }

    private void Recompute()
    {
        for (int i = _floats.Count - 1; i >= 0; i--)
            if (_clock - _floats[i].Start >= FloatSeconds) _floats.RemoveAt(i);

        List<TdMoneyFloat>? view = null;
        foreach (var f in _floats)
        {
            if (Discreet && f.Amount < BigGainCredits) continue;
            double p = Math.Clamp((_clock - f.Start) / FloatSeconds, 0.0, 1.0);
            double rise = 1.0 - (1.0 - p) * (1.0 - p);
            (view ??= []).Add(new TdMoneyFloat(FormatGain(f.Amount), (float)(FloatRiseDip * rise), (byte)Math.Round(210 * (1.0 - p * p))));
        }
        Floats = view ?? (IReadOnlyList<TdMoneyFloat>)[];

        BuyGlow = Discreet ? 0f : Glow(_buyStart, BuyGlowSeconds);
        MilestoneGlow = Discreet ? 0f : Glow(_mileStart, MilestoneGlowSeconds);

        if (ShowIncome)
        {
            long limit = _frame - IncomeWindowTicks;
            while (_gains.Count > 0 && _gains.Peek().Frame <= limit) _incomeSum -= _gains.Dequeue().Amount;
            IncomeText = FormatIncome(_incomeSum / (IncomeWindowTicks / 30.0));
        }
        else { IncomeText = null; if (_gains.Count > 0) { _gains.Clear(); _incomeSum = 0; } }

        _animating = !_frozen && (_counting || Floats.Count > 0 || BuyGlow > 0f || MilestoneGlow > 0f);
    }

    private float Glow(double start, double seconds)
    {
        double p = (_clock - start) / seconds;
        return p is >= 0.0 and < 1.0 ? (float)(1.0 - p) : 0f;
    }

    /// <summary>Texto do "+N": "+500"; de 10.000 em diante "+12k".</summary>
    public static string FormatGain(long amount) =>
        amount < 10000 ? "+" + amount.ToString(CultureInfo.InvariantCulture) : "+" + (amount / 1000).ToString(CultureInfo.InvariantCulture) + "k";

    /// <summary>Renda por segundo: "+30/s"; abaixo de 10, uma casa ("+3.5/s"); zero = sem texto.</summary>
    public static string? FormatIncome(double perSecond)
    {
        if (perSecond < 0.05) return null;
        return perSecond >= 9.95
            ? "+" + ((long)Math.Round(perSecond)).ToString(CultureInfo.InvariantCulture) + "/s"
            : "+" + perSecond.ToString("0.0", CultureInfo.InvariantCulture) + "/s";
    }
}
