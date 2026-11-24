using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Modo do popover/UI da partida TD (TD.8, SDD-28 RF-28.14/28.20). Estado de UI: nunca vai ao log nem ao hash.</summary>
public enum TdUiMode : byte { None, Build, Inspector, Preview, Menu, Map, End, Help, Towers, Runes, Bestiary }

/// <summary>Tipo de item de UI: rótulo, botão, cabeçalho de seção ou separador.</summary>
/// <remarks><c>Money</c> (TD.33d): contador de créditos do HUD, não clicável.</remarks>
public enum TdUiKind : byte { Label, Button, Header, Separator, Badges, Bar, Money }

/// <summary>Tom de cor semântico (o renderer mapeia para a paleta).</summary>
public enum TdTone : byte { Normal, Good, Warn, Bad, Accent, Dim }

/// <summary>Ação disparada por um botão do HUD/popover.</summary>
public enum TdUiAction : byte
{
    None, Close, BuildToggle, ChooseTower, StartWave, Ability, Menu, Targeting, Sell, Reposition,
    Preview, MapOpen, MapTab, SelectStage, BuyNode, Pause, Settings, Exit, Restart, NextStage, HudMinimize, HudRestore, HudExpand,
    /// <summary>TD.15: pular o tutorial, abrir "Como jogar" e refazer o tutorial.</summary>
    TutorialSkip, HowTo, RedoTutorial,
    /// <summary>TD.17: avançar passo informativo do tutorial (OK) e pergunta-bônus (V, F, ignorar).</summary>
    TutorialNext, QuizTrue, QuizFalse, QuizClose,
    /// <summary>TD.16b: abrir a lista de torres (vender/reposicionar) e abrir uma torre da lista.</summary>
    TowersOpen, OpenTower, ModalTab, TowerPage, AutoStartToggle, GameOption, ResumeAbility, RestartPrompt, RestartCancel, WipePrompt, WipeConfirm, WipeCancel, QuickTowers,
    /// <summary>TD.29b: pausa real (Pause/Resume do Core), velocidade 1x/2x/3x (Arg), seleção de um nó da teia (SArg = id) e despertar runa.</summary>
    PauseToggle, SetSpeed, SelectRune, AwakenRune,
    /// <summary>TD.30c: botão Runas do HUD (abre/fecha a janela de runas ancorada no chip, sem pausar e sem tomar foco).</summary>
    RuneWindow,
    /// <summary>TD.30e: chip da legenda de ramos (passar o mouse destaca os nós do ramo; SArg = id do tipo) e os botões do cartão "runas no primeiro dano".</summary>
    RuneBranch, RuneTutorialOpen, RuneTutorialDismiss, ViewRunes, TowerCategory,
    /// <summary>TD.40c: cartão de alerta: Salvar / Deixar cair (pausa crítica da proteção), Entendi e "não pausar por este tipo".</summary>
    AlertSave, AlertFall, AlertDismiss, AlertMute, DemotionContinue,
    /// <summary>TD.37b: fusão de torres: abrir o cartão (Arg = id da base), escolher o módulo (SArg = tipo), confirmar, voltar, fechar e dispensar a dica de primeira vez.</summary>
    FuseOpen, FusePick, FuseConfirm, FuseBack, FuseClose, FusionTipDismiss, FusionTry, FusionModule, FusionSelectModule,
    /// <summary>TD.37d: dispensar o aviso de mutantes da próxima onda.</summary>
    MutationWarnDismiss,
    /// <summary>Paginação do catálogo rápido (painel do cantinho).</summary>
    BuildCatalogPage,
    /// <summary>Mapa de fases: jogar a fase selecionada pede confirmação (MapPlay) dentro da própria aba; confirmar inicia a fase, cancelar volta.</summary>
    MapPlay, MapPlayConfirm, MapPlayCancel,
    BestiarySelect,
}

/// <summary>Ícone do HUD desenhado por código (TD.14); o renderer mapeia para caminhos vetoriais.</summary>
public enum TdIcon : byte
{
    None, Heart, Coin, Wave, Play, FastForward, Build, Menu, Minus, Restore, Lock, Scale, Firewall, Snapshot,
    /// <summary>TD.17: ícones de balão e cartões (alerta, pergunta, certo, errado, alcance, tempo, portal, coroa, medalha) e chip/recolher do HUD.</summary>
    Alert, Question, Check, Cross, Target, Clock, Portal, Crown, Medal, Expand, Collapse, Bolt, Frost,
    /// <summary>TD.29b: castelo (botão Torres do HUD) e pausa.</summary>
    Castle, Pause,
    /// <summary>TD.30c: estrela/runa (botão Runas do HUD).</summary>
    Star,
    Functions, Cosmos, Sentinel, Vault, Backup, Cdn, Queue, Policy, Aks, Pipeline,
}

/// <summary>
/// Item de layout (DIP, origem no canto superior esquerdo da janela). HUD e popover usam o mesmo tipo: o layout e o hit-test são puros
/// (Runtime) e o renderer só desenha. <c>Arg</c>/<c>SArg</c> carregam o alvo (id de torre, fase, id de torre/nó/habilidade).
/// </summary>
public readonly record struct TdUiItem(
    DipRect Rect, TdUiKind Kind, string Text, TdUiAction Action = TdUiAction.None, int Arg = 0, string? SArg = null,
    string? Right = null, string? Sub = null, bool Enabled = true, bool Selected = false, TdTone Tone = TdTone.Normal, float Fill = 0f,
    TdIcon Icon = TdIcon.None, string? Tip = null, bool RightCoin = false, bool Highlight = false, TdIcon RightIcon = TdIcon.None, float FontSize = 0f)
{
    public bool Clickable => Kind == TdUiKind.Button && Enabled && Action != TdUiAction.None;
}

/// <summary>Rolagem de uma lista da modal (em linhas): total, visíveis e primeira linha visível. Nulo = lista cabe inteira (sem barra).</summary>
public readonly record struct TdScrollInfo(int Total, int Visible, int Offset, float Top, float Height)
{
    public const float TrackX = 709f, TrackW = 4f;
    /// <summary>Polegar (topo e altura em DIP) proporcional à janela visível.</summary>
    public (float Y, float H) Thumb()
    {
        float h = Math.Max(16f, Height * Visible / Math.Max(1, Total));
        int max = Math.Max(1, Total - Visible);
        return (Top + (Height - h) * Offset / max, h);
    }
}

/// <summary>Popover (acima da faixa): título, itens (o item 0, quando botão de fechar, fica no canto) e âncora em X (DIP da faixa).</summary>
public sealed record TdPopoverView(string Title, TdUiItem[] Items, float Width, float Height, float AnchorX, string? IconTower = null,
    float ArrowX = -1f, float TargetHudX = -1f, bool Quick = false, TdRuneTip? Tip = null, bool RuneWindow = false,
    TdTone AlertTone = TdTone.Normal, float Pulse = 0f, string? IconModule = null, bool ArrowTop = false, TdScrollInfo? Scroll = null)
{
    /// <summary>Altura (DIP) da seta do balão do tutorial (TD.15), somada ao fim da janela quando <see cref="ArrowX"/> &gt;= 0.</summary>
    public const float ArrowH = 7f;

    public bool HasArrow => ArrowX >= 0f;

    public int HitTest(float x, float y)
    {
        if (RuneWindow && ArrowTop) y -= ArrowH;
        for (int i = Items.Length - 1; i >= 0; i--)
        {
            var r = Items[i].Rect;
            if (Items[i].Clickable && x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) return i;
        }
        return -1;
    }

    /// <summary>Resumo barato do conteúdo (texto, estado e preenchimento): o driver só redesenha quando muda.</summary>
    public long Signature()
    {
        var h = new HashCode();
        h.Add(ArrowTop); h.Add(Title); h.Add(IconTower); h.Add(IconModule); h.Add((int)AlertTone); h.Add((int)(Pulse * 8)); h.Add(Quick); h.Add(RuneWindow); h.Add(Tip); h.Add(Scroll); h.Add((int)(ArrowX * 10)); h.Add((int)(TargetHudX * 10));
        foreach (var it in Items)
        {
            h.Add(it.Text); h.Add(it.Right); h.Add(it.Sub); h.Add(it.Arg); h.Add(it.FontSize); h.Add((int)it.Icon); h.Add(it.Highlight); h.Add(it.Enabled); h.Add(it.Selected); h.Add((int)it.Tone); h.Add((int)(it.Fill * 100));
        }
        return h.ToHashCode();
    }
}

/// <summary>
/// HUD (RF-28.14, TD.14) na área à direita da faixa. Projetado para uma faixa de 64 DIP (<see cref="TdBand.HudBaseWidthDip"/>): a janela
/// usa a escala configurada para o canto. <see cref="Minimized"/> = chip pequeno (núcleo e onda) no canto.
/// </summary>
public sealed record TdHudView(TdUiItem[] Items, float Width, float Height, bool Minimized = false, int Wave = 0, int TotalWaves = 0,
    IReadOnlyList<TdMoneyFloat>? Floats = null, float TopPad = 0f, float LayoutScale = 1f)
{
    /// <summary>Faixa transparente (DIP) acima do chip enquanto há "+N" visíveis (TD.33d); o fundo do HUD começa em <c>TopPad</c>.</summary>
    public const float FloatPad = 16f;

    public const float DefaultWidth = TdBand.HudBaseWidthDip, DefaultHeight = TdBand.HudBaseHeightDip;

    /// <summary>TD.28b: largura do HUD com o botão Iniciar onda (chip + painel + iniciar); a janela ancora a borda esquerda nesta largura.</summary>
    /// <summary>TD.29b: chip compacto (alvo ≤ ~220 DIP em repouso); Iniciar cresce para a esquerda do grupo fixo.</summary>
    /// <summary>TD.30c: com o botão Runas o grupo fixo continua em 220 DIP (chip 108, quatro botões de 24 e quatro vãos de 4).</summary>
    /// <summary>
    /// TD.33d: o contador de créditos (fonte <see cref="MoneyFontDip"/>, âmbar, números tabulares) sai do chip e ganha item próprio; o chip passa a mostrar só
    /// integridade e onda. O grupo continua em 220 DIP até 4 dígitos de créditos (<see cref="MoneyMinWidth"/>); cada dígito a mais cresce para a ESQUERDA
    /// (como o Iniciar), então o grupo fixo à direita nunca se move.
    /// </summary>
    public const float ChipWidth = 58f, ChipHeight = 28f, Gap = 4f, ButtonWidth = 24f, SpeedWidth = 24f, StartWidth = 66f;
    public const float MoneyFontDip = 16f, MoneyMinWidth = 46f, MoneyDigitWidth = 9.6f, MoneyMinDigits = 4;
    public const float RestWidth = MoneyMinWidth + Gap + ChipWidth + Gap + ButtonWidth + Gap + ButtonWidth + Gap + ButtonWidth + Gap + SpeedWidth;

    /// <summary>Largura (DIP) do contador de créditos para um valor com esse número de dígitos.</summary>
    public static float MoneyWidth(int digits) => MoneyMinWidth + Math.Max(0, digits - (int)MoneyMinDigits) * MoneyDigitWidth;
    public const float FullWidth = RestWidth + Gap + StartWidth;

    /// <summary>Índice do item sob o ponto (qualquer item com texto/dica, inclusive não clicável) ou -1: base do tooltip.</summary>
    public int TipAt(float x, float y)
    {
        for (int i = Items.Length - 1; i >= 0; i--)
        {
            var r = Items[i].Rect;
            if (Items[i].Tip is not null && x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) return i;
        }
        return -1;
    }

    public int HitTest(float x, float y)
    {
        for (int i = Items.Length - 1; i >= 0; i--)
        {
            var r = Items[i].Rect;
            if (Items[i].Clickable && x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) return i;
        }
        return -1;
    }

    /// <summary>
    /// TD.29b: o ponto está sobre um BOTÃO (habilitado ou não)? Só botões recebem clique; fundo, texto e vãos do HUD são transparentes ao
    /// mouse (o clique passa para o app de baixo, como na faixa).
    /// </summary>
    public bool OverButton(float x, float y)
    {
        foreach (var it in Items)
        {
            var r = it.Rect;
            if (it.Kind == TdUiKind.Button && x >= r.X && x < r.X + r.W && y >= r.Y && y < r.Y + r.H) return true;
        }
        return false;
    }

    public long Signature()
    {
        var h = new HashCode();
        foreach (var it in Items)
        {
            h.Add(it.Text); h.Add(it.Right); h.Add(it.Sub); h.Add(it.Arg); h.Add(it.Enabled); h.Add(it.Selected); h.Add(it.Highlight); h.Add((int)it.Tone); h.Add((int)(it.Fill * 100)); h.Add((int)it.Icon); h.Add((int)it.RightIcon);
        }
        h.Add(Minimized); h.Add(Wave); h.Add(TotalWaves); h.Add(TopPad);
        if (Floats is { Count: > 0 })
            foreach (var f in Floats) { h.Add(f.Text); h.Add((int)(f.Rise * 2f)); h.Add(f.Alpha / 16); }
        return h.ToHashCode();
    }
}

/// <summary>Dica (tooltip) do item sob o mouse: texto completo e âncora (centro X e topo, em DIP do HUD) para a janela posicionar acima.</summary>
public sealed record TdTooltipView(string Text, float AnchorX, float AnchorTop)
{
    // Dicas de hover não podem interceptar o cursor que as mantém visíveis.
    public static bool AcceptsPointer(bool firstTime) => firstTime;
}

/// <summary>Visão do perfil/meta para o Mapa e a árvore (implementada no App sobre o perfil TD; o Runtime não conhece a Persistência).</summary>
/// <param name="Continuous">TD.28b: ondas contínuas (corrida infinita); desligado = modo clássico. Vale na próxima corrida.</param>
/// <param name="GameSpeed">TD.28c: velocidade do jogo 1, 2 ou 3 (Steps por tick no host).</param>
/// <param name="PanelPinned">TD.28b: painel rápido de torres sempre aberto junto ao chip.</param>
public sealed record TdGameOptions(int BandHeight = 64, bool AutoStart = false, bool Sound = false, int Volume = 60, bool Questions = true, bool Fade = true,
    bool Continuous = true, bool PanelPinned = false, int GameSpeed = 1, bool ShowIncome = false, bool Discreet = false, bool AutoRestart = false, int NoticeSize = 1);

public interface ITdUiHost
{
    TdGameOptions GameOptions => new();
    int LegacyTotal => 0;
    int PreviousLegacyTotal => 0;
    int PreviousBestRunWaves => 0;
    bool SimulationRunning => !Paused;
    float EndCardAvailableHeight => float.PositiveInfinity;
    void SetGameOptions(TdGameOptions options) { }

    /// <summary>TD.40c: proteção/alertas do host (null = sem alertas). Salvar, Deixar cair e persistência da máscara de silenciados.</summary>
    TdAlertGuard? Alerts => null;
    void SaveTower() { }
    void LetTowerFall() { }
    void AlertsMutedChanged(int mutedMask) { }

    /// <summary>TD.28b: texto do atalho global de minimizar tudo (boss key), mostrado na dica do chip e em Como jogar.</summary>
    string BossHotkey => "Ctrl+Alt+H";

    /// <summary>TD.28b: fase em que a corrida atual começou (Reiniciar começa uma corrida nova daqui; o Mapa muda).</summary>
    int RunStartStage => 1;

    /// <summary>Corrida infinita: recorde do perfil. Fase absoluta mais alta (ciclo = (n-1)/fases + 1; 0 = nunca jogou) e a onda mais alta nela.</summary>
    int BestStageNumber => 0;
    int BestStageWave => 0;

    bool Paused { get; }
    void TogglePause();
    void OpenSettings();
    void RequestExit();

    /// <summary>"Recomeçar do zero" (confirmado no menu): apaga o perfil TD, reinicia o onboarding e começa uma partida nova na fase 1. Configurações ficam.</summary>
    void ResetProfile() { }

    /// <summary>HUD recolhido (lembrado em settings, TD.14).</summary>
    bool HudMinimized { get; }
    void SetHudMinimized(bool minimized);

    /// <summary>Inicia (ou reinicia) a partida na fase; o App cria a nova partida e reenvia o layout.</summary>
    void StartStage(int stageId);

    int StageCount { get; }
    bool StageUnlocked(int stageId);
    int EncounterCount(string id) => 0;
    int StageStars(int stageId);
    /// <summary>Coroa da fase (vencida sem perder integridade, RF-28.21).</summary>
    bool StageCrown(int stageId);
    /// <summary>Medalhas ganhas na fase (bits de <see cref="TdMedals"/>).</summary>
    int StageMedals(int stageId);
    /// <summary>Recorde pessoal de pontuação da fase (0 = nunca vencida).</summary>
    int StageBestScore(int stageId);
    /// <summary>Patente local e progresso até a próxima.</summary>
    TdRankProgress Rank { get; }
    /// <summary>O que a última partida encerrada trouxe de novo ao perfil (para o card de Vitória).</summary>
    TdRecordOutcome LastOutcome { get; }
    int SealsAvailable { get; }
    bool NodePurchased(string nodeId);
    bool PurchaseNode(string nodeId);
}
