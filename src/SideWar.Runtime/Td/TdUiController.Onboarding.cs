using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Passo do tutorial da 1ª partida (TD.15, ampliado na TD.17). Calculado a partir do estado real (partida e UI), nunca por tempo.</summary>
public enum TdTutorialStep : byte
{
    None,
    /// <summary>HUD nasce recolhido: "Clique no chip para abrir" (seta pulsante).</summary>
    HudOpen,
    /// <summary>"Clique na seta para recolher".</summary>
    HudCollapse,
    /// <summary>Núcleo e integridade (informativo, OK).</summary>
    Core,
    /// <summary>Créditos (informativo, OK).</summary>
    Credits,
    /// <summary>Portal e trilho (informativo, OK).</summary>
    Portal,
    /// <summary>"Clique em Construir".</summary>
    Build,
    /// <summary>"Clique num ponto da faixa".</summary>
    Point,
    /// <summary>"Escolha uma torre".</summary>
    Tower,
    /// <summary>Alcance da torre (informativo, OK).</summary>
    Range,
    /// <summary>"Clique em Iniciar onda".</summary>
    Wave,
    /// <summary>Reforço ao tomar dano: o que fazer, apontando o alvo (limitado por tempo).</summary>
    Hurt,
    /// <summary>"Chame a próxima: ganhe bônus" (em Onda, quando a chamada está liberada).</summary>
    CallNext,
    /// <summary>Quando houver pergunta-bônus: exemplo guiado.</summary>
    Quiz,
    /// <summary>"Venda em Menu, Torres" (Preparo, depois da 1ª onda); a faixa é click-through, a venda é pelo HUD (TD.16b).</summary>
    Sell,
    /// <summary>"Use uma habilidade" (liberada e pronta).</summary>
    Ability,
    /// <summary>"Clique em Coletar caixa" (na 1ª caixa): botão do HUD ou atalho, com a regra de ausência (TD.16b).</summary>
    Crate,
}

/// <summary>
/// Persistência do onboarding (TD.15): tutorial concluído/pulado e dicas de 1ª vez já mostradas. Implementada no App sobre as
/// configurações (<c>td.tutorialDone</c>, <c>td.tipsSeen</c>). Estado de UI: nunca vai ao log nem ao hash da simulação.
/// </summary>
public interface ITdOnboardingStore
{
    bool TutorialDone { get; }
    void SetTutorialDone(bool done);
    bool TipSeen(string id);
    void MarkTipSeen(string id);

    /// <summary>TD.17: pergunta-bônus V/F ligada (td.bonusQuestions). Padrão das implementações de teste: desligada.</summary>
    bool BonusQuestions => false;
}

/// <summary>
/// Onboarding do <see cref="TdUiController"/> (TD.15, ampliado na TD.17, SDD-28 RF-28.22): tutorial guiado da 1ª partida (fase 1), cartão "Como jogar"
/// e dicas de 1ª vez. Tudo é UI: lê o estado da partida e envia só os comandos que o próprio jogador dispara (nenhum comando novo no log
/// por causa do tutorial; a fase 1 espera a 1ª torre pelo botão Iniciar onda desabilitado e pelo portão do host).
/// Texto: no máximo ~8 palavras por linha, ícone antes do texto, verbos no imperativo.
/// </summary>
public sealed partial class TdUiController
{
    public const double TipSeconds = 6.0;
    public const double HurtSeconds = 6.0;
    public const double HurtMinGapSeconds = 15.0;
    public const double HudHintSeconds = 15.0;
    public const float CalloutWidth = 250f;

    [Flags]
    private enum Ack : byte { None = 0, Core = 1, Credits = 2, Portal = 4, Range = 8, Call = 16, Sell = 32 }

    private Ack _ack;
    private bool _abilityDone, _crateDone, _crateSeen;
    private int _hudPhase; // 0 abrir, 1 recolher, 2 abrir de novo, 3 pronto
    private readonly Queue<string> _tipQueue = [];
    private readonly HashSet<string> _tipKnown = [];
    private string? _tipText;
    private double _tipUntil;
    private TdMatchState? _hurtMatch;
    private int _lastIntegrity = -1;
    private double _hurtUntil = double.NegativeInfinity, _hurtNextAt = double.NegativeInfinity;
    private int _hurtKind; // 0 construir perto do portal, 1 habilidade, 2 construir mais

    /// <summary>Há onboarding ligado (armazenamento fornecido pelo App).</summary>
    public bool OnboardingEnabled => _onb is not null;

    /// <summary>Texto da dica de 1ª vez em exibição (null = nenhuma). Some sozinha em <see cref="TipSeconds"/> ou ao clicar.</summary>
    public string? CurrentTip => _tipText;

    /// <summary>Passo atual do tutorial (None = tutorial concluído, pulado ou fora da fase 1).</summary>
    public TdTutorialStep Step => ComputeStep(_match());

    private bool TutorialOn(TdMatchState m) =>
        _onb is { TutorialDone: false } && m.StageId == 1 && m.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    /// <summary>Fase 1 sem nenhuma torre: a onda 1 não começa (Iniciar onda desabilitado com a dica "Coloque 1 torre primeiro").</summary>
    public bool StartBlocked => StartBlockedFor(_match());

    private bool StartBlockedFor(TdMatchState m) =>
        _onb is { TutorialDone: false } && m.StageId == 1 && m.Phase == MatchPhase.Preparo && m.WaveNumber == 0 && m.Towers.Count == 0;

    private static int PlacedTowers(TdMatchState m)
    {
        int n = 0;
        for (int i = 0; i < m.Towers.Count; i++) if (m.Towers[i].Placed) n++;
        return n;
    }

    private static int FirstPlacedTower(TdMatchState m)
    {
        for (int i = 0; i < m.Towers.Count; i++) if (m.Towers[i].Placed) return i;
        return -1;
    }

    private TdTutorialStep ComputeStep(TdMatchState m)
    {
        if (!TutorialOn(m)) return TdTutorialStep.None;
        bool started = m.WaveNumber > 0 || m.Phase == MatchPhase.Onda;
        if (!started)
        {
            if (_hudPhase is 0 or 2) return TdTutorialStep.HudOpen;
            if (_hudPhase == 1) return TdTutorialStep.HudCollapse;
            if ((_ack & Ack.Core) == 0) return TdTutorialStep.Core;
            if ((_ack & Ack.Credits) == 0) return TdTutorialStep.Credits;
            if ((_ack & Ack.Portal) == 0) return TdTutorialStep.Portal;
            if (m.Towers.Count == 0)
                return Mode == TdUiMode.Build ? (_chosenX >= 0 && _moveTowerId == 0 ? TdTutorialStep.Tower : TdTutorialStep.Point) : TdTutorialStep.Build;
            if ((_ack & Ack.Range) == 0) return TdTutorialStep.Range;
            return TdTutorialStep.Wave;
        }
        if (_now() < _hurtUntil) return TdTutorialStep.Hurt;
        if (m.CrateActive && !_crateDone) return TdTutorialStep.Crate;
        if (m.Phase == MatchPhase.Onda && (_ack & Ack.Call) == 0 && CallReady(m)) return TdTutorialStep.CallNext;
        if (m.Phase == MatchPhase.Preparo && _quizQ is not null) return TdTutorialStep.Quiz;
        if (m.Phase == MatchPhase.Preparo && (_ack & Ack.Sell) == 0 && PlacedTowers(m) > 0 && m.WaveNumber >= 1) return TdTutorialStep.Sell;
        if (!_abilityDone && ReadyAbility(m) >= 0) return TdTutorialStep.Ability;
        return TdTutorialStep.None;
    }

    private static bool CallReady(TdMatchState m)
    {
        if (m.NextWaveNumber == 0) return false;
        foreach (var a in m.ActiveWaves) if (!a.SpawnsDone) return false;
        return true;
    }

    private static int ReadyAbility(TdMatchState m)
    {
        if (m.Phase != MatchPhase.Onda) return -1;
        var list = m.AbilityStatuses;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Unlocked && list[i].CooldownTicks == 0 && list[i].UsesLeft != 0) return i;
        return -1;
    }

    /// <summary>X (DIP do espaço de desenho da faixa) do alvo a destacar na faixa, ou -1.</summary>
    private float HintX()
    {
        if (Mode != TdUiMode.None || _onb is null) return -1f;
        var m = _match();
        int w = TdBand.WidthDip(m);
        switch (ComputeStep(m))
        {
            case TdTutorialStep.Portal:
                return w - 24f;
            case TdTutorialStep.Hurt when _hurtKind == 0:
                return w - 30f;
            case TdTutorialStep.Range:
                int ti = FirstPlacedTower(m);
                return ti >= 0 ? TdBand.TowerCenterDip(m, m.Towers[ti], w) : -1f;
            default: return -1f;
        }
    }

    // ================================================================ eventos

    private void NoteAction(TdUiAction action)
    {
        if (_onb is null) return;
        if (action == TdUiAction.Ability) _abilityDone = true;
        else if (action == TdUiAction.Sell) _ack |= Ack.Sell;
        else if (action == TdUiAction.StartWave && _match().Phase == MatchPhase.Onda) _ack |= Ack.Call;
        else if (action == TdUiAction.TutorialNext) AckStep();
    }

    /// <summary>OK de um passo informativo: marca o passo como entendido.</summary>
    private void AckStep()
    {
        switch (ComputeStep(_match()))
        {
            case TdTutorialStep.Core: _ack |= Ack.Core; break;
            case TdTutorialStep.Credits: _ack |= Ack.Credits; break;
            case TdTutorialStep.Portal: _ack |= Ack.Portal; break;
            case TdTutorialStep.Range: _ack |= Ack.Range; break;
            case TdTutorialStep.CallNext: _ack |= Ack.Call; break;
            case TdTutorialStep.Sell: _ack |= Ack.Sell; break;
        }
        Changed?.Invoke();
    }

    private void SkipTutorial()
    {
        if (_onb is null) return;
        FinishTutorial();
    }

    private void FinishTutorial()
    {
        if (_onb is null || _onb.TutorialDone) return;
        _onb.SetTutorialDone(true);
        Changed?.Invoke();
    }

    /// <summary>"Refazer tutorial": zera os passos, marca o tutorial como não concluído e recomeça a fase 1 (partida limpa).</summary>
    private void RedoTutorial()
    {
        if (_onb is null) return;
        _ack = Ack.None;
        _abilityDone = _crateDone = _crateSeen = false;
        _quizExampleDone = false;
        _hudPhase = _host.HudMinimized ? 0 : 1;
        _hurtUntil = _hurtNextAt = double.NegativeInfinity;
        _onb.SetTutorialDone(false);
        Close();
        _host.StartStage(1);
    }

    /// <summary>Abre o cartão "Como jogar" (item da bandeja e do menu do HUD).</summary>
    public void OpenHowTo() => SetMode(TdUiMode.Help);

    /// <summary>Clique na dica (ou em qualquer ação do jogador): a dica some.</summary>
    public void DismissTip()
    {
        if (_tipText is null) return;
        _tipText = null;
        Changed?.Invoke();
    }

    private void SyncOnboarding(TdMatchState m)
    {
        if (_onb is null) return;
        ScanTips(m);
        UpdateTip(m);
        SyncHurt(m);
        if (_onb.TutorialDone || m.StageId != 1) return;
        bool min = _host.HudMinimized;
        if (_hudPhase == 0 && !min) _hudPhase = 1;
        if (_hudPhase == 1 && min) _hudPhase = 2;
        if (_hudPhase == 2 && !min) _hudPhase = 3;
        if (m.CrateActive) _crateSeen = true;
        else if (_crateSeen) _crateDone = true;
        bool started = m.WaveNumber > 0 || m.Phase == MatchPhase.Onda;
        bool ended = m.Phase is MatchPhase.Vitoria or MatchPhase.Derrota;
        bool quizDone = _quizExampleDone || !QuizEnabled;
        if (started && (ended || (_abilityDone && _crateDone && quizDone && (_ack & Ack.Sell) != 0 && (_ack & Ack.Call) != 0))) FinishTutorial();
    }

    /// <summary>Reforço ao tomar dano (1ª partida): dica curta do que fazer, apontando o alvo; no máximo uma a cada <see cref="HurtMinGapSeconds"/>.</summary>
    private void SyncHurt(TdMatchState m)
    {
        if (!ReferenceEquals(_hurtMatch, m)) { _hurtMatch = m; _lastIntegrity = m.Integrity; _hurtUntil = _hurtNextAt = double.NegativeInfinity; }
        int prev = _lastIntegrity;
        _lastIntegrity = m.Integrity;
        if (m.Integrity >= prev || m.Phase != MatchPhase.Onda || !TutorialOn(m)) return;
        double now = _now();
        if (now < _hurtNextAt) return;
        int near = 0;
        for (int i = 0; i < m.Towers.Count; i++) if (m.Towers[i].Placed && m.Towers[i].X >= TdDefeat.NearPortalX) near++;
        _hurtKind = near < TdDefeat.MinTowersNearPortal ? 0 : ReadyAbility(m) >= 0 ? 1 : 2;
        _hurtUntil = now + HurtSeconds;
        _hurtNextAt = now + HurtMinGapSeconds;
        Changed?.Invoke();
    }

    // ================================================================ dicas de 1ª vez

    private void ScanTips(TdMatchState m)
    {
        if (m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) return;
        var towers = m.Content.Towers;
        for (int i = 0; i < m.Towers.Count; i++) Offer("tower:" + towers[m.Towers[i].DefIndex].Id);
        if (m.Enemies.Count == 0) return;
        var enemies = m.Content.Enemies;
        for (int i = 0; i < m.Enemies.Count; i++)
        {
            var e = m.Enemies[i];
            if (e.IsBoss) { int b = e.DefIndex - enemies.Enemies.Length; if (b >= 0 && b < enemies.Bosses.Length) Offer("boss:" + enemies.Bosses[b].Id); }
            else if (e.DefIndex >= 0 && e.DefIndex < enemies.Enemies.Length) Offer("enemy:" + enemies.Enemies[e.DefIndex].Id);
        }
    }

    private void Offer(string key)
    {
        if (!_tipKnown.Add(key) || _onb!.TipSeen(key)) return;
        _tipQueue.Enqueue(key);
    }

    private void UpdateTip(TdMatchState m)
    {
        double now = _now();
        bool free = Mode == TdUiMode.None && ComputeStep(m) == TdTutorialStep.None && m.Phase is MatchPhase.Preparo or MatchPhase.Onda;
        if (_tipText is not null && (now >= _tipUntil || !free)) { _tipText = null; Changed?.Invoke(); }
        if (_tipText is not null || !free) return;
        while (_tipQueue.Count > 0)
        {
            string key = _tipQueue.Dequeue();
            if (_onb!.TipSeen(key)) continue;
            string? text = TipTextFor(m, key);
            _onb.MarkTipSeen(key); // contado ao exibir: nunca repete, mesmo sem texto
            if (text is null) continue;
            _tipText = text;
            _tipUntil = now + TipSeconds;
            Changed?.Invoke();
            return;
        }
    }

    private static string TraitDescription(string[] traits)
    {
        var parts = new List<string>(3);
        foreach (var t in traits)
        {
            string? text = t switch
            {
                "basic" => "tráfego básico", "fast" => "rápido, pouca vida", "swarm" => "vem em enxame", "armored" => "blindado",
                "durable" => "muita vida", "flying" => "voador", "regen" => "regenera vida", "drain" => "drena torres", "boss" => "chefe", _ => null,
            };
            if (text is not null) parts.Add(text);
        }
        return string.Join(", ", parts);
    }

    private static string CounterNames(TdMatchState m, string[] counters)
    {
        var names = new List<string>(3);
        foreach (var c in counters)
        {
            string id = c.Split('.')[0];
            foreach (var d in m.Content.Towers)
                if (d.Id == id && !names.Contains(d.Name)) names.Add(d.Name);
        }
        return string.Join(" ou ", names);
    }

    private static string? TipTextFor(TdMatchState m, string key)
    {
        int colon = key.IndexOf(':');
        string kind = key[..colon], id = key[(colon + 1)..];
        switch (kind)
        {
            case "tower":
                foreach (var d in m.Content.Towers)
                    if (d.Id == id) return $"{d.Name}: {d.Role}.";
                return null;
            case "enemy":
                foreach (var e in m.Content.Enemies.Enemies)
                    if (e.Id == id) return EnemyTip(m, e.Name, e.Traits, e.Counters);
                return null;
            case "boss":
                foreach (var b in m.Content.Enemies.Bosses)
                    if (b.Id == id) return EnemyTip(m, b.Name, b.Traits, b.Counters);
                return null;
        }
        return null;
    }

    private static string EnemyTip(TdMatchState m, string name, string[] traits, string[] counters)
    {
        string t = TraitDescription(traits), c = CounterNames(m, counters);
        return $"{name}: {(t.Length > 0 ? t : "novo inimigo")}." + (c.Length > 0 ? $" Use {c}." : "");
    }

    // ================================================================ HUD e popover

    private static bool HudStep(TdTutorialStep s) =>
        s is TdTutorialStep.Core or TdTutorialStep.Credits or TdTutorialStep.Build or TdTutorialStep.Wave or TdTutorialStep.CallNext
            or TdTutorialStep.Ability or TdTutorialStep.HudCollapse or TdTutorialStep.HudOpen or TdTutorialStep.Sell or TdTutorialStep.Crate
            || s == TdTutorialStep.Hurt;

    private TdUiItem[] HighlightHud(List<TdUiItem> items)
    {
        var step = _onb is null ? TdTutorialStep.None : ComputeStep(_match());
        if (HudStep(step) && Mode == TdUiMode.None)
        {
            int target = HudTargetIndex(items, step);
            if (target >= 0) items[target] = items[target] with { Highlight = true };
        }
        return [.. items];
    }

    private int HudTargetIndex(IReadOnlyList<TdUiItem> items, TdTutorialStep step)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            switch (step)
            {
                case TdTutorialStep.HudOpen when it.Action == TdUiAction.HudRestore: return i;
                case TdTutorialStep.HudCollapse when it.Action == TdUiAction.HudMinimize: return i;
                case TdTutorialStep.Core when it.Icon == TdIcon.Heart && it.Kind == TdUiKind.Label: return i;
                case TdTutorialStep.Credits when it.Icon == TdIcon.Coin && it.Kind == TdUiKind.Label: return i;
                case TdTutorialStep.Build when it.Action == TdUiAction.BuildToggle: return i;
                case TdTutorialStep.Wave or TdTutorialStep.CallNext when it.Action == TdUiAction.StartWave: return i;
                case TdTutorialStep.Sell when it.Action == TdUiAction.Menu: return i;
                case TdTutorialStep.Crate when it.Action == TdUiAction.CollectCrate: return i;
                case TdTutorialStep.Ability when it.Action == TdUiAction.Ability && it.Enabled: return i;
                case TdTutorialStep.Hurt when _hurtKind == 1 && it.Action == TdUiAction.Ability && it.Enabled: return i;
                case TdTutorialStep.Hurt when _hurtKind == 2 && it.Action == TdUiAction.BuildToggle: return i;
            }
        }
        return -1;
    }

    private (TdIcon Icon, string Text, string? Sub, bool Ok) StepLines(TdTutorialStep step, TdMatchState m) => step switch
    {
        TdTutorialStep.HudOpen => (TdIcon.Expand, "Clique no chip para abrir o HUD", null, false),
        TdTutorialStep.HudCollapse => (TdIcon.Collapse, "Clique na seta para recolher", null, false),
        TdTutorialStep.Core => (TdIcon.Heart, "Proteja o núcleo!", "Chegar a 0 = derrota.", true),
        TdTutorialStep.Credits => (TdIcon.Coin, "Créditos compram torres.", "Cada onda rende mais.", true),
        TdTutorialStep.Portal => (TdIcon.Portal, "Inimigos saem do portal.", "Eles correm até o núcleo.", true),
        TdTutorialStep.Build => (TdIcon.Build, "Clique em Construir", null, false),
        TdTutorialStep.Point => (TdIcon.Build, "Clique num ponto da faixa", null, false),
        TdTutorialStep.Tower => (TdIcon.Build, "Escolha uma torre", null, false),
        TdTutorialStep.Range => (TdIcon.Target, "O círculo é o alcance.", "Ela só atira dentro dele.", true),
        TdTutorialStep.Wave => (TdIcon.Play, "Clique em Iniciar onda", null, false),
        TdTutorialStep.Hurt => (TdIcon.Alert, "Inimigo passou!", _hurtKind switch { 0 => "Construa perto do portal.", 1 => "Use uma habilidade.", _ => "Construa mais torres." }, false),
        TdTutorialStep.CallNext => (TdIcon.FastForward, "Chame a próxima: ganhe bônus", null, true),
        TdTutorialStep.Sell => (TdIcon.Coin, "Venda: Menu, depois Torres", $"Devolve {Math.Round(m.Content.Stages.Rules.SellRefund * 100):0}%.", true),
        TdTutorialStep.Ability => (TdIcon.Scale, "Use uma habilidade", null, false),
        TdTutorialStep.Crate => (TdIcon.Box, "Clique em Coletar caixa!", "Ou use o atalho. Longe do PC? Ela some.", false),
        _ => (TdIcon.None, "", null, false),
    };

    private TdPopoverView? DecoratePopover(TdMatchState m, TdPopoverView? view)
    {
        if (_onb is null) return view;
        var step = ComputeStep(m);
        if (step == TdTutorialStep.None) return view;
        if (view is null) return Mode == TdUiMode.None ? BuildCallout(m, step) : null;
        bool decorate = (Mode == TdUiMode.Build && step is TdTutorialStep.Point or TdTutorialStep.Tower)
            || (Mode is TdUiMode.Menu or TdUiMode.Towers && step == TdTutorialStep.Sell);
        return decorate ? AppendTutorial(view, step, m) : view;
    }

    private TdPopoverView BuildCallout(TdMatchState m, TdTutorialStep step)
    {
        var (icon, text, sub, ok) = StepLines(step, m);
        var s = new Stack(CalloutWidth);
        s.Add(TdUiKind.Label, text, 16f, tone: TdTone.Accent, icon: icon);
        if (sub is not null) s.Add(TdUiKind.Label, sub, 14f, tone: TdTone.Dim);
        if (ok) s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, 70f, 16f), TdUiKind.Button, "OK", TdUiAction.TutorialNext, Tone: TdTone.Good, Icon: TdIcon.Check));
        s.Items.Add(new TdUiItem(new DipRect(s.Width - Pad - 96f, s.Y, 96f, 16f), TdUiKind.Button, "Pular", TdUiAction.TutorialSkip, Tone: TdTone.Dim, Icon: TdIcon.Cross));
        s.Y += 18f;
        float anchor = 0f, target = -1f;
        switch (step)
        {
            case TdTutorialStep.Portal:
            case TdTutorialStep.Hurt when _hurtKind == 0:
                anchor = MathF.Max(1f, TdBand.WidthDip(m) - 24f);
                break;
            case TdTutorialStep.Range:
                int ti = FirstPlacedTower(m);
                if (ti >= 0) anchor = MathF.Max(1f, m.TowerDip(m.Towers[ti]));
                break;
            default:
                var hud = BuildHud();
                int ix = HudTargetIndex(hud.Items, step);
                if (ix >= 0) target = hud.Items[ix].Rect.X + hud.Items[ix].Rect.W / 2f;
                break;
        }
        bool arrow = target >= 0f || anchor > 0f;
        return new TdPopoverView("Tutorial", [.. s.Items], s.Width, s.Y + Pad + (arrow ? TdPopoverView.ArrowH : 0f), anchor, null,
            arrow ? s.Width / 2f : -1f, target);
    }

    private TdPopoverView AppendTutorial(TdPopoverView view, TdTutorialStep step, TdMatchState m)
    {
        var items = new List<TdUiItem>(view.Items.Length + 2);
        bool marked = false;
        foreach (var it in view.Items)
        {
            bool target = !marked && ((step == TdTutorialStep.Tower && it.Action == TdUiAction.ChooseTower && it.Enabled)
                || (step == TdTutorialStep.Sell && (it.Action == TdUiAction.Sell || (Mode == TdUiMode.Menu && it.Action == TdUiAction.TowersOpen)) && it.Enabled));
            if (target) marked = true;
            items.Add(target ? it with { Highlight = true } : it);
        }
        var (icon, text, _, _) = StepLines(step, m);
        float y = view.Height - Pad + 2f;
        items.Add(new TdUiItem(new DipRect(Pad, y, view.Width - 2 * Pad, 14f), TdUiKind.Label, text, Tone: TdTone.Accent, Icon: icon));
        y += 16f;
        items.Add(new TdUiItem(new DipRect(view.Width - Pad - 96f, y, 96f, 16f), TdUiKind.Button, "Pular", TdUiAction.TutorialSkip, Tone: TdTone.Dim, Icon: TdIcon.Cross));
        y += 18f;
        return view with { Items = [.. items], Height = y + Pad };
    }

    // ================================================================ Como jogar

    private TdPopoverView BuildHelp(TdMatchState m)
    {
        var s = new Stack(PopoverWidth);
        void Card(TdIcon icon, string text, TdTone tone = TdTone.Normal) => s.Add(TdUiKind.Label, text, 15f, tone: tone, icon: icon);
        Card(TdIcon.Heart, "Proteja o núcleo: 0 = derrota.");
        Card(TdIcon.Coin, "Créditos compram torres.");
        Card(TdIcon.Portal, "Inimigos saem do portal.");
        Card(TdIcon.Build, "Construir: depois clique num ponto.");
        Card(TdIcon.Target, "O círculo mostra o alcance.");
        Card(TdIcon.Play, "Inicie a onda: o preparo espera.");
        Card(TdIcon.FastForward, "Chame a próxima: ganhe bônus.");
        Card(TdIcon.Coin, $"Venda em Menu, Torres: devolve {Math.Round(m.Content.Stages.Rules.SellRefund * 100):0}%.");
        Card(TdIcon.Scale, "Use habilidades: recarregam sozinhas.");
        Card(TdIcon.Box, "Colete a caixa: botão ou atalho.");
        Card(TdIcon.Question, "Responda V ou F: ganhe bônus.");
        Card(TdIcon.Crown, "Vença sem perder núcleo: coroa.");
        Card(TdIcon.Medal, "Medalhas e patente: seus recordes.");
        if (_onb is not null) s.Add(TdUiKind.Button, "Refazer tutorial", RowH + 2, TdUiAction.RedoTutorial, tone: TdTone.Good, icon: TdIcon.Play);
        return Finish("Como jogar", s, 0);
    }

    // ================================================================ derrota ensina (RF-28.24) e guia da 1ª vitória

    private static string EnemyName(TdMatchState m, string? id)
    {
        if (id is null) return "Inimigo";
        foreach (var e in m.Content.Enemies.Enemies) if (e.Id == id) return e.Name;
        foreach (var b in m.Content.Enemies.Bosses) if (b.Id == id) return b.Name;
        return id;
    }

    /// <summary>Motivo (1 linha) e dica (1 linha) da derrota, derivados das estatísticas da partida.</summary>
    internal static (TdIcon ReasonIcon, string Reason, TdIcon TipIcon, string Tip) DefeatTexts(TdMatchState m)
    {
        var d = m.DefeatAnalysis();
        string counters = string.Join("/", d.CounterTowerIds.Select(id => m.Content.Towers.FirstOrDefault(t => t.Id == id)?.Name ?? id));
        string useCounters = counters.Length > 0 ? $"Use {counters}." : "Construa mais torres.";
        return d.Reason switch
        {
            TdDefeatReason.BossLeaked => (TdIcon.Alert, $"{EnemyName(m, d.EnemyId)} passou!", TdIcon.Scale, "Use habilidades no chefe."),
            TdDefeatReason.CrateBlast => (TdIcon.Alert, "A caixa explodiu suas torres.", TdIcon.Box, "Colete a caixa a tempo."),
            TdDefeatReason.FlyersLeaked => (TdIcon.Alert, "Voadores passaram.", TdIcon.Target, useCounters),
            TdDefeatReason.FewTowersNearPortal => (TdIcon.Alert, "Poucas torres perto do portal.", TdIcon.Build, "Construa perto do portal."),
            _ => (TdIcon.Alert, $"{EnemyName(m, d.EnemyId)} passou.", TdIcon.Target, useCounters),
        };
    }
}
