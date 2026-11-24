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
        new(TdUiMode.Menu, "Defenda a base dos inimigos.", "Vazamentos tiram XP; não há game over.", TdIcon.Medal, "rank-card"),
        new(TdUiMode.Build, "Escolha uma torre e posicione-a na faixa.", "É preciso ter uma torre antes de iniciar.", TdIcon.Build, TargetAction: TdUiAction.ChooseTower, HudTarget: TdUiAction.QuickTowers),
        new(TdUiMode.Build, "Use Iniciar onda no HUD.", "Expanda o HUD se o botão estiver recolhido.", TdIcon.Play, TargetAction: TdUiAction.ModalTab, TargetArg: (int)TdUiMode.Build, HudTarget: TdUiAction.StartWave),
        new(TdUiMode.Build, "Cada torre tem uma função e fraquezas.", "Confira os detalhes no catálogo Construir.", TdIcon.Target, TargetAction: TdUiAction.ChooseTower),
        new(TdUiMode.Towers, "Arraste uma torre para um espaço vazio.", "Movê-la não custa créditos.", TdIcon.Build, TargetAction: TdUiAction.OpenTower),
        new(TdUiMode.Towers, "Solte uma torre sobre outra para fundir.", "A fusão fica mais forte e ganha nome.", TdIcon.Star, TargetAction: TdUiAction.OpenTower),
        new(TdUiMode.Towers, "Em Torres, inspecione ou venda.", "No inspetor, escolha a mira da torre.", TdIcon.Target, TargetAction: TdUiAction.OpenTower),
        new(TdUiMode.Build, "Créditos compram torres; o Cofre gera renda.", "Torres caras podem ser mais eficazes.", TdIcon.Coin, TargetAction: TdUiAction.ChooseTower),
        new(TdUiMode.Menu, "Em Jogo, veja patente, brasão e XP.", "Vazamentos podem rebaixar sua patente.", TdIcon.Medal, "rank-card"),
        new(TdUiMode.Menu, "Defenda sem vazamentos para formar sequência.", "O desempenho também rende coroas e estrelas.", TdIcon.Crown, "rank-card"),
        new(TdUiMode.Runes, "Fragmentos despertam runas permanentes.", "Na teia, arraste para mover e use a roda para zoom.", TdIcon.Star, "rfrag"),
        new(TdUiMode.Map, "Fase / Selo mostra o mapa de regiões.", "Cada fase registra estrelas e coroas.", TdIcon.Crown, "mapbg"),
        new(TdUiMode.Map, "Use selos na árvore de melhorias.", "Compre os nós de cada ramo em ordem.", TdIcon.Star, TargetAction: TdUiAction.MapTab, TargetArg: 1, Seals: true),
        new(TdUiMode.Map, "Cada região exige uma patente.", "Sem patente, repita a região; a regra vale nos ciclos.", TdIcon.Medal, "mapbg"),
        new(TdUiMode.Map, "Fases especiais trazem chefões.", "Eles enfurecem ao chegar a 50% de vida.", TdIcon.Alert, "mapbg"),
        new(TdUiMode.Menu, "O chip abre e recolhe o HUD.", "Arraste o cabeçalho para mover este menu.", TdIcon.Expand, "rank-chip", HudTarget: TdUiAction.HudRestore),
        new(TdUiMode.Menu, "Em Jogo, ajuste tamanho e avisos.", "Você também pode pausar e mudar a velocidade.", TdIcon.Pause, TargetAction: TdUiAction.GameOption, TargetArg: 0),
        new(TdUiMode.Menu, "Use a boss key para esconder tudo.", "O atalho aparece em Como jogar.", TdIcon.Minus, "rank-chip"),
        new(TdUiMode.Menu, "Use habilidades quando estiverem prontas.", "Elas ajudam a defender durante as ondas.", TdIcon.Scale, TargetAction: TdUiAction.ResumeAbility),
        new(TdUiMode.Menu, "Perguntas bônus podem render recompensas.", "Ative ou desative as perguntas em Jogo.", TdIcon.Question, TargetAction: TdUiAction.GameOption, TargetArg: 4),
        new(TdUiMode.Menu, "Recomeçar do zero apaga o progresso.", "A opção fica em Jogo e pede confirmação.", TdIcon.Alert, TargetAction: TdUiAction.WipePrompt),
        new(TdUiMode.Bestiary, "Conheça os inimigos no bestiário.", "Confira características e fraquezas.", TdIcon.Target, TargetAction: TdUiAction.ModalTab, TargetArg: (int)TdUiMode.Bestiary),
        new(TdUiMode.Help, "Consulte Como jogar quando quiser.", "Use Refazer tutorial para rever estes passos.", TdIcon.Question, TargetAction: TdUiAction.ModalTab, TargetArg: (int)TdUiMode.Help),
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
