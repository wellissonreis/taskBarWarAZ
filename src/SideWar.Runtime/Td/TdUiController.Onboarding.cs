using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Estado público do tutorial; o fluxo guiado usa <see cref="TdTutorialStep.Tour"/>.</summary>
public enum TdTutorialStep : byte
{
    None,
    /// <summary>Sequência guiada acessível em qualquer estado da partida.</summary>
    Tour,
    /// <summary>HUD nasce recolhido: "Clique no chip para abrir" (seta pulsante).</summary>
    HudOpen,
    /// <summary>"Feche a modal com Esc".</summary>
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
    /// <summary>TD.29b: Pausar e velocidade 1x/2x/3x (informativo, OK).</summary>
    Controls,
    /// <summary>TD.29b: fragmentos despertam runas na aba Runas (informativo, OK).</summary>
    Runes,
    /// <summary>Patente e brasão: o progresso fica em Jogo (informativo, OK).</summary>
    Rank,
    /// <summary>Fusão: escolher uma torre na barra e soltar sobre outra torre (informativo, OK; só com a fusão liberada e 2 torres).</summary>
    Fusion,
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

    /// <summary>TD.30e: esquece uma dica de 1ª vez ("Refazer tutorial" reativa o cartão de runas). Padrão: nada (armazenamentos de teste).</summary>
    void ClearTip(string id) { }

    /// <summary>TD.17: pergunta-bônus V/F ligada (td.bonusQuestions). Padrão das implementações de teste: desligada.</summary>
    bool BonusQuestions => false;
}

/// <summary>
/// Tutorial guiado, ajuda e dicas de primeira vez. A sequência pode ser percorrida em qualquer fase;
/// o estado de conclusão é salvo nas configurações e a construção continua sujeita às regras da partida.
/// </summary>
public sealed partial class TdUiController
{
    public const double TipSeconds = 6.0;
    public const double HurtSeconds = 6.0;
    public const double HurtMinGapSeconds = 15.0;
    public const double HudHintSeconds = 15.0;
    public const float CalloutWidth = 250f;

    private readonly TdTutorial? _tutorial;
    private int _helpScroll;
    private const int HelpVisibleRows = 8;
    private readonly Queue<string> _tipQueue = [];
    private readonly HashSet<string> _tipKnown = [];
    private string? _tipText;
    private double _tipUntil;
    private TdMatchState? _hurtMatch;
    private long _lastIntegrity = -1;
    private double _hurtUntil = double.NegativeInfinity, _hurtNextAt = double.NegativeInfinity;
    private int _hurtKind; // 0 construir perto do portal, 1 habilidade, 2 construir mais

    /// <summary>Há onboarding ligado (armazenamento fornecido pelo App).</summary>
    public bool OnboardingEnabled => _onb is not null;

    /// <summary>Texto da dica de 1ª vez em exibição (null = nenhuma). Some sozinha em <see cref="TipSeconds"/> ou ao clicar.</summary>
    public string? CurrentTip => _outsideCollapsed ? null : Step == TdTutorialStep.HudOpen ? "Toque no chip para abrir" : _tipText;

    /// <summary>Estado atual do tutorial (None = concluído, pulado ou sem armazenamento).</summary>
    public TdTutorialStep Step => ComputeStep(_match());

    private bool TutorialOn(TdMatchState m) =>
        _onb is { TutorialDone: false } && m.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    /// <summary>Fase 1 sem nenhuma torre: a onda 1 não começa (Iniciar onda desabilitado com a dica "Coloque 1 torre primeiro").</summary>
    public bool StartBlocked => StartBlockedFor(_match());

    private static bool StartBlockedFor(TdMatchState m) =>
        m.Phase is MatchPhase.Preparo or MatchPhase.Onda && !m.HasPlacedTower;

    private static int FirstPlacedTower(TdMatchState m)
    {
        for (int i = 0; i < m.Towers.Count; i++) if (m.Towers[i].Placed) return i;
        return -1;
    }

    private TdTutorialStep ComputeStep(TdMatchState m) =>
        _tutorial is { Active: true } ? TdTutorialStep.Tour : TdTutorialStep.None;

    private void MoveTutorial(bool forward)
    {
        if (_tutorial is not { Active: true }) return;
        if (forward) _tutorial.Next(); else _tutorial.Previous();
        if (_tutorial.Active)
        {
            var page = _tutorial.Current;
            SetMode(page.Tab);
            _mapSeals = page.Seals;
        }
        Changed?.Invoke();
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
        if (action == TdUiAction.TutorialNext) MoveTutorial(true);
        else if (action == TdUiAction.TutorialPrevious) MoveTutorial(false);
    }

    private void SkipTutorial()
    {
        if (_onb is null) return;
        _tutorial?.Skip();
        Changed?.Invoke();
    }

    /// <summary>"Refazer tutorial": zera os passos de orientação sobre a partida atual.</summary>
    private void RedoTutorial()
    {
        if (_onb is null) return;
        _quizExampleDone = false;
        _hurtUntil = _hurtNextAt = double.NegativeInfinity;
        ResetRuneCard(clearSeen: true);
        ResetMutations(clearSeen: true);
        _tutorial!.Restart();
        Close();
    }

    /// <summary>Abre o cartão "Como jogar" (item da bandeja e do menu do HUD).</summary>
    public void OpenHowTo() { _outsideCollapsed = false; SetMode(TdUiMode.Help); }

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
    }

    /// <summary>Reforço ao tomar dano (1ª partida): dica curta do que fazer, apontando o alvo; no máximo uma a cada <see cref="HurtMinGapSeconds"/>.</summary>
    private void SyncHurt(TdMatchState m)
    {
        if (!ReferenceEquals(_hurtMatch, m)) { _hurtMatch = m; _lastIntegrity = HurtMeter(m); _hurtUntil = _hurtNextAt = double.NegativeInfinity; }
        long prev = _lastIntegrity, meter = HurtMeter(m);
        _lastIntegrity = meter;
        if (meter >= prev || m.Phase != MatchPhase.Onda || !TutorialOn(m)) return;
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
        s is TdTutorialStep.Tour or TdTutorialStep.Core or TdTutorialStep.Credits or TdTutorialStep.Build or TdTutorialStep.Wave or TdTutorialStep.CallNext
            or TdTutorialStep.Ability or TdTutorialStep.HudCollapse or TdTutorialStep.HudOpen or TdTutorialStep.Sell
            or TdTutorialStep.Controls or TdTutorialStep.Runes or TdTutorialStep.Tower or TdTutorialStep.Rank or TdTutorialStep.Fusion
            || s == TdTutorialStep.Hurt;

    private TdUiItem[] HighlightHud(List<TdUiItem> items)
    {
        var step = _onb is null ? TdTutorialStep.None : ComputeStep(_match());
        if (step == TdTutorialStep.Tour && _tutorial!.Current.HudTarget != TdUiAction.None)
        {
            int target = items.FindIndex(it => it.Action == _tutorial.Current.HudTarget);
            if (target < 0) target = items.FindIndex(it => it.Action == TdUiAction.HudExpand);
            if (target >= 0) items[target] = items[target] with { Highlight = true };
        }
        else if (HudStep(step) && Mode == TdUiMode.None)
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
                case TdTutorialStep.Tour when it.Action == TdUiAction.HudRestore: return i;
                case TdTutorialStep.HudOpen when it.Action == TdUiAction.HudRestore: return i;
                case TdTutorialStep.HudCollapse when it.Action == TdUiAction.HudMinimize: return i;
                case TdTutorialStep.Core when it.Icon == TdIcon.Medal && it.Kind == TdUiKind.Label && it.SArg == "core": return i;
                case TdTutorialStep.Credits when it.Icon == TdIcon.Coin && it.Kind == TdUiKind.Label: return i;
                case TdTutorialStep.Build when it.Action == TdUiAction.QuickTowers: return i;
                case TdTutorialStep.Controls when it.Action == TdUiAction.PauseToggle: return i;
                case TdTutorialStep.Runes when it.Action == TdUiAction.RuneWindow: return i;
                case TdTutorialStep.Wave or TdTutorialStep.CallNext when it.Action == TdUiAction.StartWave: return i;
                case TdTutorialStep.Sell when it.Action == TdUiAction.HudRestore: return i;
                case TdTutorialStep.Ability when it.Action == TdUiAction.Ability && it.Enabled: return i;
                case TdTutorialStep.Hurt when _hurtKind == 1 && it.Action == TdUiAction.Ability && it.Enabled: return i;
                case TdTutorialStep.Hurt when _hurtKind == 2 && it.Action == TdUiAction.QuickTowers: return i;
            }
        }
        return -1;
    }

    private (TdIcon Icon, string Text, string? Sub, bool Ok) StepLines(TdTutorialStep step, TdMatchState m) => step switch
    {
        TdTutorialStep.HudOpen => (TdIcon.Expand, "Toque no chip para abrir", null, false),
        TdTutorialStep.HudCollapse => (TdIcon.Collapse, "Recolha o HUD", "Use o botão de recolher.", false),
        TdTutorialStep.Core => (TdIcon.Medal, "Defenda a base! Sem game over.", "Vazar tira XP e pode rebaixar.", true),
        TdTutorialStep.Credits => (TdIcon.Coin, "Créditos compram torres.", "O Cofre ajuda a ganhar créditos.", true),
        TdTutorialStep.Portal => (TdIcon.Portal, "Inimigos saem do portal.", "Eles correm até a base.", true),
        TdTutorialStep.Build => (TdIcon.Castle, "Clique no castelo: Torres", null, false),
        TdTutorialStep.Point => (TdIcon.Build, "Clique num ponto da faixa", null, false),
        TdTutorialStep.Tower => (TdIcon.Build, "Escolha uma torre", null, false),
        TdTutorialStep.Controls => (TdIcon.Pause, "Pausar e velocidade 1x a 3x", "O botão ao lado pausa; 2x e 3x aceleram.", true),
        TdTutorialStep.Runes => (TdIcon.Question, "Fragmentos despertam runas permanentes", "Na teia, arraste e use zoom.", true),
        TdTutorialStep.Rank => (TdIcon.Medal, "Patente e brasão: seu progresso", "XP sobe e desce a patente; veja em Jogo.", true),
        TdTutorialStep.Fusion => (TdIcon.Star, "Funda: solte uma torre sobre outra", "Ela fica mais forte e ganha nome.", true),
        TdTutorialStep.Range => (TdIcon.Target, "O círculo é o alcance.", "Ela só atira dentro dele.", true),
        TdTutorialStep.Wave => (TdIcon.Play, "Clique em Iniciar onda", null, false),
        TdTutorialStep.Hurt => (TdIcon.Alert, "Inimigo passou!", _hurtKind switch { 0 => "Construa perto do portal.", 1 => "Use uma habilidade.", _ => "Construa mais torres." }, false),
        TdTutorialStep.CallNext => (TdIcon.FastForward, "Chame a próxima: ganhe bônus", null, true),
        TdTutorialStep.Sell => (TdIcon.Coin, "Venda: chip, aba Torres", $"Devolve {Math.Round(m.Content.Stages.Rules.SellRefund * 100):0}%.", true),
        TdTutorialStep.Ability => (TdIcon.Scale, "Use uma habilidade", null, false),
        _ => (TdIcon.None, "", null, false),
    };

    private TdPopoverView? DecoratePopover(TdMatchState m, TdPopoverView? view)
    {
        if (_onb is null) return view;
        var step = ComputeStep(m);
        if (step == TdTutorialStep.None) return view;
        if (step == TdTutorialStep.Tour) return view is null && Mode == TdUiMode.None ? BuildTourCallout() : view;
        if (view is null) return Mode == TdUiMode.None ? BuildCallout(m, step) : null;
        bool decorate = (Mode == TdUiMode.Build && step is TdTutorialStep.Point or TdTutorialStep.Tower)
            || (Mode is TdUiMode.Menu or TdUiMode.Towers && step == TdTutorialStep.Sell);
        return decorate ? MarkTutorialTargets(view, step) : view;
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

    /// <summary>Destaca o alvo do passo dentro da modal (o texto do passo vai na faixa inferior da modal: <see cref="AddModalTutorial"/>).</summary>
    private TdPopoverView MarkTutorialTargets(TdPopoverView view, TdTutorialStep step)
    {
        var items = new List<TdUiItem>(view.Items.Length);
        bool marked = false;
        foreach (var it in view.Items)
        {
            bool target = !marked && ((step == TdTutorialStep.Tower && it.Action == TdUiAction.ChooseTower && it.Enabled)
                || (step == TdTutorialStep.Sell && (it.Action == TdUiAction.Sell || (Mode == TdUiMode.Menu && it.Action == TdUiAction.TowersOpen)) && it.Enabled));
            if (target) marked = true;
            items.Add(target ? it with { Highlight = true } : it);
        }
        return view with { Items = [.. items] };
    }

    /// <summary>
    /// Faixa do tutorial no pé da modal (sem roubar foco: é só desenho): ícone + texto do passo e "Pular". Aparece quando um passo está ativo
    /// (ex.: "Feche a modal: o jogo começa" na abertura da 1ª execução).
    /// </summary>
    private TdPopoverView BuildTourCallout()
    {
        var page = _tutorial!.Current;
        var s = new Stack(440f);
        s.Add(TdUiKind.Label, page.Text, 18f, tone: TdTone.Accent, icon: page.Icon);
        s.Add(TdUiKind.Label, TourDetail(page), 18f, tone: TdTone.Dim);
        s.Items.Add(new(new DipRect(Pad, s.Y, 68, 20), TdUiKind.Button, "Anterior", TdUiAction.TutorialPrevious, Enabled: _tutorial.Index > 0));
        s.Items.Add(new(new DipRect(82, s.Y, 90, 20), TdUiKind.Button, "Próximo", TdUiAction.TutorialNext));
        s.Items.Add(new(new DipRect(180, s.Y, 72, 20), TdUiKind.Button, "Pular", TdUiAction.TutorialSkip));
        return new TdPopoverView($"Tutorial {_tutorial.Index + 1}/{TdTutorial.Pages.Count}", [.. s.Items], s.Width, s.Y + 26, 0);
    }

    private string TourDetail(TdTutorialPage page) => page.Icon == TdIcon.Minus
        ? $"Atalho: {_host.BossHotkey}. A partida continua."
        : page.Detail;

    private void AddModalTutorial(List<TdUiItem> items)
    {
        if (_tutorial is not { Active: true }) return;
        var page = _tutorial.Current;
        int target = items.FindIndex(it => Mode == page.Tab &&
            ((page.Target is not null && it.SArg == page.Target) ||
             (page.TargetAction != TdUiAction.None && it.Action == page.TargetAction && (page.TargetArg < 0 || it.Arg == page.TargetArg))));
        if (target < 0) target = items.FindIndex(it => it.Action == TdUiAction.ModalTab && it.Arg == (int)page.Tab);
        if (target >= 0) items[target] = items[target] with { Highlight = true };
        // Remove os controles sob a faixa para que o hit-test não atravesse o tutorial.
        items.RemoveAll(it => it.Clickable && it.Rect.Y < 472 && it.Rect.Y + it.Rect.H > 402 && it.Action is not (TdUiAction.ModalTab or TdUiAction.Close));
        items.Add(new(new DipRect(8, 402, 704, 70), TdUiKind.Label, "", SArg: "tour-background"));
        items.Add(new(new DipRect(20, 406, 465, 18), TdUiKind.Label, $"{_tutorial.Index + 1}/{TdTutorial.Pages.Count}  {page.Text}", Icon: page.Icon, SArg: "tut"));
        items.Add(new(new DipRect(42, 425, 600, 16), TdUiKind.Label, TourDetail(page), SArg: "tour-detail"));
        items.Add(new(new DipRect(390, 445, 96, 24), TdUiKind.Button, "Anterior", TdUiAction.TutorialPrevious, Enabled: _tutorial.Index > 0));
        items.Add(new(new DipRect(492, 445, 104, 24), TdUiKind.Button, _tutorial.Index == TdTutorial.Pages.Count - 1 ? "Concluir" : "Próximo", TdUiAction.TutorialNext));
        items.Add(new(new DipRect(602, 445, 100, 24), TdUiKind.Button, "Pular", TdUiAction.TutorialSkip, SArg: "tutskip"));
    }

    // ================================================================ Como jogar

    private TdPopoverView BuildHelp(TdMatchState m)
    {
        var s = new Stack(PopoverWidth);
        void Card(TdIcon icon, string text, TdTone tone = TdTone.Normal) => s.Add(TdUiKind.Label, text, 15f, sarg: "help-card", tone: tone, icon: icon);
        void Section(string title) => s.Add(TdUiKind.Label, title, 18f, sarg: "help-section");
        Section("OBJETIVO E ONDAS");
        Card(TdIcon.Medal, "Defenda a base: vazar tira XP e pode rebaixar.");
        Card(TdIcon.Medal, "Não há game over; continue defendendo.");
        Card(TdIcon.Build, "Coloque uma torre antes de iniciar a onda.");
        Card(TdIcon.Play, m.Options.ContinuousWaves ? "Inicie a 1ª onda; o resto flui." : "Inicie a onda quando estiver pronto.");
        Card(TdIcon.FastForward, "Chame a próxima onda para ganhar bônus.");
        Card(TdIcon.Alert, "Chefões especiais enfurecem com 50% de vida.");
        Card(TdIcon.Target, "Em Inimigos, veja traços e fraquezas.");
        Section("TORRES E ECONOMIA");
        Card(TdIcon.Build, "Construir mostra tipos, funções e fraquezas.");
        Card(TdIcon.Build, "Arraste ao vazio para mover sem custo.");
        Card(TdIcon.Star, "Torre sobre torre: solte e funde.");
        Card(TdIcon.Star, "Fusões ficam mais fortes e ganham nomes.");
        Card(TdIcon.Target, "Inspecione a torre para ajustar sua mira.");
        Card(TdIcon.Coin, $"Venda na aba Torres: devolve {Math.Round(m.Content.Stages.Rules.SellRefund * 100):0}%.");
        Card(TdIcon.Coin, "Créditos compram torres; Cofre gera renda.");
        Card(TdIcon.Coin, "Torres caras podem ser mais eficazes.");
        Card(TdIcon.Coin, "Cada torre igual custa +25%.");
        Section("PROGRESSO E RUNAS");
        Card(TdIcon.Medal, "Patentes, brasões e XP ficam em Jogo.");
        Card(TdIcon.Crown, "Vença sem vazamentos para ganhar coroa.");
        Card(TdIcon.Question, "Fragmentos despertam runas permanentes.");
        Card(TdIcon.Question, "Arraste e use zoom para explorar a teia.");
        Card(TdIcon.Alert, MutationHelpText);
        Card(TdIcon.Question, "Responda V ou F para ganhar bônus.");
        Section("FASE / SELO");
        Card(TdIcon.Crown, "O mapa mostra regiões, estrelas e coroas.");
        Card(TdIcon.Star, "Gaste selos na árvore, em ordem por ramo.");
        Card(TdIcon.Medal, "Cada região exige uma patente; sem ela, repita a região.");
        Card(TdIcon.Medal, "Nos ciclos, a trava por patente continua valendo.");
        Section("HUD E CONFIGURAÇÕES");
        Card(TdIcon.Expand, "Chip do HUD: toque para abrir/recolher.");
        Card(TdIcon.Expand, "Arraste o cabeçalho para mover o menu.");
        Card(TdIcon.Pause, "Em Jogo, ajuste avisos, tamanho e velocidade.");
        Card(TdIcon.Minus, $"Esconder tudo: {_host.BossHotkey}.", TdTone.Accent);
        Card(TdIcon.Alert, "Recomeçar do zero apaga o progresso.");
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
            TdDefeatReason.FlyersLeaked => (TdIcon.Alert, "Voadores passaram.", TdIcon.Target, useCounters),
            TdDefeatReason.FewTowersNearPortal => (TdIcon.Alert, "Poucas torres perto do portal.", TdIcon.Build, "Construa perto do portal."),
            _ => (TdIcon.Alert, $"{EnemyName(m, d.EnemyId)} passou.", TdIcon.Target, useCounters),
        };
    }
}
