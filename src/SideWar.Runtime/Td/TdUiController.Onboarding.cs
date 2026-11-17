using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Passo do tutorial da 1ª partida (TD.15). Calculado a partir do estado real (partida e UI), nunca por tempo.</summary>
public enum TdTutorialStep : byte
{
    None,
    /// <summary>1/7: "Clique em Construir".</summary>
    Build,
    /// <summary>2/7: "Clique num ponto da faixa para posicionar".</summary>
    Point,
    /// <summary>3/7: "Escolha uma torre".</summary>
    Tower,
    /// <summary>4/7: "Clique em Iniciar onda".</summary>
    Wave,
    /// <summary>5/7: "Clique numa torre para melhorar" (quando há créditos).</summary>
    Upgrade,
    /// <summary>6/7: "Use uma habilidade" (quando liberada e pronta).</summary>
    Ability,
    /// <summary>7/7: "Clique na caixa antes que exploda" (na 1ª caixa).</summary>
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
}

/// <summary>
/// Onboarding do <see cref="TdUiController"/> (TD.15, SDD-28 RF-28.22): tutorial guiado da 1ª partida (fase 1), cartão "Como jogar"
/// e dicas de 1ª vez. Tudo é UI: lê o estado da partida e envia só os comandos que o próprio jogador dispara (nenhum comando novo no log,
/// nenhuma pausa: Preparo já é 0 fps e sem pressa; a caixa-bomba só cai na fase 1 a partir da onda 3).
/// </summary>
public sealed partial class TdUiController
{
    public const int TutorialSteps = 7;
    public const double TipSeconds = 6.0;
    public const float CalloutWidth = 250f;

    private bool _upgradeDone, _abilityDone, _crateDone, _crateSeen, _hudForced;
    private readonly Queue<string> _tipQueue = [];
    private readonly HashSet<string> _tipKnown = [];
    private string? _tipText;
    private double _tipUntil;

    /// <summary>Há onboarding ligado (armazenamento fornecido pelo App).</summary>
    public bool OnboardingEnabled => _onb is not null;

    /// <summary>Texto da dica de 1ª vez em exibição (null = nenhuma). Some sozinha em <see cref="TipSeconds"/> ou ao clicar.</summary>
    public string? CurrentTip => _tipText;

    /// <summary>Passo atual do tutorial (None = tutorial concluído, pulado ou fora da fase 1).</summary>
    public TdTutorialStep Step => ComputeStep(_match());

    private bool TutorialOn(TdMatchState m) =>
        _onb is { TutorialDone: false } && m.StageId == 1 && m.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    private TdTutorialStep ComputeStep(TdMatchState m)
    {
        if (!TutorialOn(m)) return TdTutorialStep.None;
        bool started = m.WaveNumber > 0 || m.Phase == MatchPhase.Onda;
        if (!started)
        {
            if (m.Towers.Count == 0)
                return Mode == TdUiMode.Build ? (_chosenX >= 0 && _moveTowerId == 0 ? TdTutorialStep.Tower : TdTutorialStep.Point) : TdTutorialStep.Build;
            return TdTutorialStep.Wave;
        }
        if (m.CrateActive && !_crateDone) return TdTutorialStep.Crate;
        if (!_upgradeDone && UpgradeTarget(m) >= 0) return TdTutorialStep.Upgrade;
        if (!_abilityDone && ReadyAbility(m) >= 0) return TdTutorialStep.Ability;
        return TdTutorialStep.None;
    }

    private static int UpgradeTarget(TdMatchState m)
    {
        for (int i = 0; i < m.Towers.Count; i++)
        {
            var t = m.Towers[i];
            if (!t.Placed) continue;
            var d = m.Content.Towers[t.DefIndex];
            if (t.Level < d.Levels.Length && m.Credits >= Ceil(d.Cost * d.Levels[t.Level].CostFactor)) return i;
        }
        return -1;
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
            case TdTutorialStep.Upgrade:
                int ti = UpgradeTarget(m);
                return ti >= 0 ? TdBand.TowerCenterDip(m, m.Towers[ti], w) : -1f;
            case TdTutorialStep.Crate:
                return m.CrateActive ? TdBand.ToDip(m.CrateX, w) : -1f;
            default: return -1f;
        }
    }

    // ================================================================ eventos

    private void NoteAction(TdUiAction action)
    {
        if (_onb is null) return;
        if (action == TdUiAction.Upgrade) _upgradeDone = true;
        else if (action == TdUiAction.Ability) _abilityDone = true;
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
        _upgradeDone = _abilityDone = _crateDone = _crateSeen = _hudForced = false;
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
        if (_onb.TutorialDone || m.StageId != 1) return;
        if (!_hudForced && _host.HudMinimized)
        {
            _hudForced = true; // o tutorial aponta para botões do HUD: expande uma vez
            _host.SetHudMinimized(false);
        }
        if (m.CrateActive) _crateSeen = true;
        else if (_crateSeen) _crateDone = true;
        bool started = m.WaveNumber > 0 || m.Phase == MatchPhase.Onda;
        bool ended = m.Phase is MatchPhase.Vitoria or MatchPhase.Derrota;
        if (started && (ended || (_upgradeDone && _abilityDone && _crateDone))) FinishTutorial();
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
                    if (d.Id == id) return $"Dica: {d.Name}: {d.Role}.";
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
        return $"Dica: {name}: {(t.Length > 0 ? t : "novo inimigo")}." + (c.Length > 0 ? $" Use {c}." : "");
    }

    // ================================================================ HUD e popover

    private TdUiItem[] HighlightHud(List<TdUiItem> items)
    {
        var step = _onb is null || _host.HudMinimized ? TdTutorialStep.None : ComputeStep(_match());
        if (step is TdTutorialStep.Build or TdTutorialStep.Wave or TdTutorialStep.Ability && Mode == TdUiMode.None)
        {
            int target = HudTargetIndex(items, step);
            if (target >= 0) items[target] = items[target] with { Highlight = true };
        }
        return [.. items];
    }

    private static int HudTargetIndex(IReadOnlyList<TdUiItem> items, TdTutorialStep step)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (step == TdTutorialStep.Build && it.Action == TdUiAction.BuildToggle) return i;
            if (step == TdTutorialStep.Wave && it.Action == TdUiAction.StartWave) return i;
            if (step == TdTutorialStep.Ability && it.Action == TdUiAction.Ability && it.Enabled) return i;
        }
        return -1;
    }

    private static string StepText(TdTutorialStep step, TdUiMode mode) => step switch
    {
        TdTutorialStep.Build => "Clique em Construir e escolha uma torre",
        TdTutorialStep.Point => "Clique num ponto da faixa para posicionar",
        TdTutorialStep.Tower => "Agora escolha uma torre da lista",
        TdTutorialStep.Wave => "Clique em Iniciar onda",
        TdTutorialStep.Upgrade => mode == TdUiMode.Inspector ? "Clique em Melhorar" : "Clique numa torre para melhorar",
        TdTutorialStep.Ability => "Use uma habilidade",
        TdTutorialStep.Crate => "Clique na caixa antes que exploda",
        _ => "",
    };

    private TdPopoverView? DecoratePopover(TdMatchState m, TdPopoverView? view)
    {
        if (_onb is null) return view;
        var step = ComputeStep(m);
        if (step == TdTutorialStep.None) return view;
        if (view is null) return Mode == TdUiMode.None ? BuildCallout(m, step) : null;
        bool decorate = (Mode == TdUiMode.Build && step is TdTutorialStep.Point or TdTutorialStep.Tower)
            || (Mode == TdUiMode.Inspector && step == TdTutorialStep.Upgrade);
        return decorate ? AppendTutorial(view, step) : view;
    }

    private TdPopoverView BuildCallout(TdMatchState m, TdTutorialStep step)
    {
        var s = new Stack(CalloutWidth);
        s.Add(TdUiKind.Label, StepText(step, Mode), 26f, tone: TdTone.Accent);
        s.Items.Add(new TdUiItem(new DipRect(s.Width - Pad - 96f, s.Y, 96f, 16f), TdUiKind.Button, "Pular tutorial", TdUiAction.TutorialSkip, Tone: TdTone.Dim));
        s.Y += 18f;
        string title = $"Tutorial {(int)step}/{TutorialSteps}";
        float anchor = 0f, target = -1f;
        var items = new List<TdUiItem>(s.Items);
        switch (step)
        {
            case TdTutorialStep.Build:
            case TdTutorialStep.Wave:
            case TdTutorialStep.Ability:
                var hud = BuildHud();
                int ix = hud.Minimized ? -1 : HudTargetIndex(hud.Items, step);
                if (ix >= 0) target = hud.Items[ix].Rect.X + hud.Items[ix].Rect.W / 2f;
                break;
            case TdTutorialStep.Upgrade:
                int ti = UpgradeTarget(m);
                if (ti >= 0) anchor = MathF.Max(1f, m.TowerDip(m.Towers[ti]));
                break;
            case TdTutorialStep.Crate:
                anchor = MathF.Max(1f, TdBand.ToDip(m.CrateX, TdBand.WidthDip(m)));
                break;
        }
        bool arrow = target >= 0f || anchor > 0f;
        return new TdPopoverView(title, [.. items], s.Width, s.Y + Pad + (arrow ? TdPopoverView.ArrowH : 0f), anchor, null,
            arrow ? s.Width / 2f : -1f, target);
    }

    private TdPopoverView AppendTutorial(TdPopoverView view, TdTutorialStep step)
    {
        var items = new List<TdUiItem>(view.Items.Length + 2);
        bool marked = false;
        foreach (var it in view.Items)
        {
            bool target = !marked && ((step == TdTutorialStep.Tower && it.Action == TdUiAction.ChooseTower && it.Enabled)
                || (step == TdTutorialStep.Upgrade && it.Action == TdUiAction.Upgrade && it.Enabled));
            if (target) marked = true;
            items.Add(target ? it with { Highlight = true } : it);
        }
        float y = view.Height - Pad + 2f;
        items.Add(new TdUiItem(new DipRect(Pad, y, view.Width - 2 * Pad, 14f), TdUiKind.Label, StepText(step, Mode), Tone: TdTone.Accent));
        y += 16f;
        items.Add(new TdUiItem(new DipRect(view.Width - Pad - 96f, y, 96f, 16f), TdUiKind.Button, "Pular tutorial", TdUiAction.TutorialSkip, Tone: TdTone.Dim));
        y += 18f;
        return view with { Items = [.. items], Height = y + Pad };
    }

    // ================================================================ Como jogar

    private TdPopoverView BuildHelp()
    {
        var s = new Stack(PopoverWidth);
        void Section(string header, string text)
        {
            s.Add(TdUiKind.Header, header, 12f);
            s.Add(TdUiKind.Label, text, 24f);
        }
        Section("Objetivo", "Impeça os inimigos de chegar ao núcleo, à esquerda. Sobreviva a todas as ondas da fase.");
        Section("Torres", "Clique em Construir (ou num ponto da faixa) e escolha a torre. Clique numa torre para melhorar, mirar ou vender.");
        Section("Ondas", "No preparo nada se move. Iniciar onda começa a próxima; chamar antes do tempo rende créditos.");
        Section("Habilidades", "Escalar, Firewall e Snapshot ficam no HUD, com recarga. Algumas liberam por fase ou na árvore de selos.");
        Section("Caixa-bomba", "Às vezes cai uma caixa na faixa: clique nela antes que exploda. Dá créditos e acelera as habilidades.");
        Section("Coroa, medalhas e patente", "Coroa: vencer sem perder integridade. Medalhas: desafios extras. A patente soma seus recordes.");
        if (_onb is not null) s.Add(TdUiKind.Button, "Refazer tutorial", RowH + 2, TdUiAction.RedoTutorial, tone: TdTone.Good);
        return Finish("Como jogar", s, 0);
    }
}
