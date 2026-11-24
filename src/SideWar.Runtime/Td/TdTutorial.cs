namespace SideWar.Runtime.Td;

/// <summary>Uma explicação curta e o elemento real ao qual ela se refere.</summary>
public sealed record TdTutorialPage(TdUiMode Tab, string Text, string Detail, TdIcon Icon,
    string? Target = null, TdUiAction TargetAction = TdUiAction.None, int TargetArg = -1, bool Seals = false,
    TdUiAction HudTarget = TdUiAction.None);

/// <summary>Sequência independente do estado da partida; apenas a conclusão é persistida.</summary>
public sealed class TdTutorial(ITdOnboardingStore store)
{
    public static IReadOnlyList<TdTutorialPage> Pages { get; } = Array.AsReadOnly<TdTutorialPage>(
    [
        new(TdUiMode.None, "Coloque sua primeira torre.", "Expanda o HUD. Em Torres, escolha e clique na faixa livre.", TdIcon.Build, HudTarget: TdUiAction.QuickTowers),
        new(TdUiMode.None, "Inicie a onda no HUD.", "Auto-início: Imediato por padrão; ajuste em Configurações.", TdIcon.Play, HudTarget: TdUiAction.StartWave),
        new(TdUiMode.None, "Mova uma torre arrastando.", "Solte em um ponto livre; não custa créditos.", TdIcon.Build),
        new(TdUiMode.Bestiary, "Cada inimigo tem pontos fortes e fracos.", "Veja os tipos em Inimigos e escolha a torre certa.", TdIcon.Target),
        new(TdUiMode.Menu, "Patentes liberam torres e fusão.", "Fusão a partir de Júnior; dano no núcleo tira XP.", TdIcon.Medal),
        new(TdUiMode.None, "Velocidade no HUD acelera a partida.", "", TdIcon.FastForward),
        new(TdUiMode.None, "Modo sombra farma sem arriscar XP.", "Liga e desliga em Configurações; o atalho também vale.", TdIcon.Moon),
        new(TdUiMode.Menu, "Configurações ficam na engrenagem.", "Abra Como jogar para refazer o tutorial.", TdIcon.Question, TargetAction: TdUiAction.SettingsOpen),
    ]);

    public int Index { get; private set; }
    public bool Active => !store.TutorialDone;
    public TdTutorialPage Current => Pages[Index];
    public void Next()
    {
        if (!Active) return;
        if (Index == Pages.Count - 1) store.SetTutorialDone(true);
        else Index++;
    }
    public void Previous() { if (Active && Index > 0) Index--; }
    public void Skip() { if (Active) store.SetTutorialDone(true); }
    public void Restart() { Index = 0; store.SetTutorialDone(false); }
}
