using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Modo do popover/UI da partida TD (TD.8, SDD-28 RF-28.14/28.20). Estado de UI: nunca vai ao log nem ao hash.</summary>
public enum TdUiMode : byte { None, Build, Inspector, Preview, Menu, Map, End, Help, Towers, Runes }

/// <summary>Tipo de item de UI: rótulo, botão, cabeçalho de seção ou separador.</summary>
public enum TdUiKind : byte { Label, Button, Header, Separator, Badges, Bar }

/// <summary>Tom de cor semântico (o renderer mapeia para a paleta).</summary>
public enum TdTone : byte { Normal, Good, Warn, Bad, Accent, Dim }

/// <summary>Ação disparada por um botão do HUD/popover.</summary>
public enum TdUiAction : byte
{
    None, Close, BuildToggle, ChooseTower, StartWave, Ability, Menu, Targeting, Sell, Reposition,
    Preview, MapOpen, MapTab, SelectStage, BuyNode, Pause, Settings, Exit, Restart, NextStage, HudMinimize, HudRestore,
    /// <summary>TD.15: pular o tutorial, abrir "Como jogar" e refazer o tutorial.</summary>
    TutorialSkip, HowTo, RedoTutorial,
    /// <summary>TD.17: avançar passo informativo do tutorial (OK) e pergunta-bônus (V, F, ignorar).</summary>
    TutorialNext, QuizTrue, QuizFalse, QuizClose,
    /// <summary>TD.16b: coletar a caixa-bomba pelo HUD, abrir a lista de torres (vender/reposicionar) e abrir uma torre da lista.</summary>
    CollectCrate, TowersOpen, OpenTower, ModalTab, TowerPage, AutoStartToggle, GameOption, ResumeAbility, RestartPrompt, RestartCancel,
}

/// <summary>Ícone do HUD desenhado por código (TD.14); o renderer mapeia para caminhos vetoriais.</summary>
public enum TdIcon : byte
{
    None, Heart, Coin, Wave, Play, FastForward, Build, Menu, Minus, Restore, Lock, Scale, Firewall, Snapshot,
    /// <summary>TD.17: ícones de balão e cartões (alerta, pergunta, certo, errado, alcance, tempo, caixa, portal, coroa, medalha) e chip/recolher do HUD.</summary>
    Alert, Question, Check, Cross, Target, Clock, Box, Portal, Crown, Medal, Expand, Collapse,
}

/// <summary>
/// Item de layout (DIP, origem no canto superior esquerdo da janela). HUD e popover usam o mesmo tipo: o layout e o hit-test são puros
/// (Runtime) e o renderer só desenha. <c>Arg</c>/<c>SArg</c> carregam o alvo (id de torre, fase, id de torre/nó/habilidade).
/// </summary>
public readonly record struct TdUiItem(
    DipRect Rect, TdUiKind Kind, string Text, TdUiAction Action = TdUiAction.None, int Arg = 0, string? SArg = null,
    string? Right = null, string? Sub = null, bool Enabled = true, bool Selected = false, TdTone Tone = TdTone.Normal, float Fill = 0f,
    TdIcon Icon = TdIcon.None, string? Tip = null, bool RightCoin = false, bool Highlight = false, TdIcon RightIcon = TdIcon.None)
{
    public bool Clickable => Kind == TdUiKind.Button && Enabled && Action != TdUiAction.None;
}

/// <summary>Popover (acima da faixa): título, itens (o item 0, quando botão de fechar, fica no canto) e âncora em X (DIP da faixa).</summary>
public sealed record TdPopoverView(string Title, TdUiItem[] Items, float Width, float Height, float AnchorX, string? IconTower = null,
    float ArrowX = -1f, float TargetHudX = -1f)
{
    /// <summary>Altura (DIP) da seta do balão do tutorial (TD.15), somada ao fim da janela quando <see cref="ArrowX"/> &gt;= 0.</summary>
    public const float ArrowH = 7f;

    public bool HasArrow => ArrowX >= 0f;

    public int HitTest(float x, float y)
    {
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
        h.Add(Title); h.Add(IconTower); h.Add((int)(ArrowX * 10)); h.Add((int)(TargetHudX * 10));
        foreach (var it in Items)
        {
            h.Add(it.Text); h.Add(it.Right); h.Add(it.Sub); h.Add(it.Arg); h.Add((int)it.Icon); h.Add(it.Highlight); h.Add(it.Enabled); h.Add(it.Selected); h.Add((int)it.Tone); h.Add((int)(it.Fill * 100));
        }
        return h.ToHashCode();
    }
}

/// <summary>
/// HUD (RF-28.14, TD.14) na área à direita da faixa. Projetado para uma faixa de 64 DIP (<see cref="TdBand.HudBaseWidthDip"/>): a janela
/// escala por <see cref="TdBand.HudScale"/>. <see cref="Minimized"/> = chip pequeno (núcleo e onda) no canto.
/// </summary>
public sealed record TdHudView(TdUiItem[] Items, float Width, float Height, bool Minimized = false)
{
    public const float DefaultWidth = TdBand.HudBaseWidthDip, DefaultHeight = TdBand.HudBaseHeightDip;
    public const float ChipWidth = 190f, ChipHeight = 28f;

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

    public long Signature()
    {
        var h = new HashCode();
        foreach (var it in Items)
        {
            h.Add(it.Text); h.Add(it.Right); h.Add(it.Enabled); h.Add(it.Selected); h.Add(it.Highlight); h.Add((int)it.Tone); h.Add((int)(it.Fill * 100)); h.Add((int)it.Icon); h.Add((int)it.RightIcon);
        }
        h.Add(Minimized);
        return h.ToHashCode();
    }
}

/// <summary>Dica (tooltip) do item sob o mouse: texto completo e âncora (centro X e topo, em DIP do HUD) para a janela posicionar acima.</summary>
public sealed record TdTooltipView(string Text, float AnchorX, float AnchorTop);

/// <summary>Visão do perfil/meta para o Mapa e a árvore (implementada no App sobre o perfil TD; o Runtime não conhece a Persistência).</summary>
public sealed record TdGameOptions(int BandHeight = 64, bool AutoStart = false, bool Sound = false, int Volume = 60, bool Questions = true, bool Fade = true);

public interface ITdUiHost
{
    TdGameOptions GameOptions => new();
    void SetGameOptions(TdGameOptions options) { }

    bool Paused { get; }
    void TogglePause();
    void OpenSettings();
    void RequestExit();

    /// <summary>HUD recolhido (lembrado em settings, TD.14).</summary>
    bool HudMinimized { get; }
    void SetHudMinimized(bool minimized);

    /// <summary>Inicia (ou reinicia) a partida na fase; o App cria a nova partida e reenvia o layout.</summary>
    void StartStage(int stageId);

    int StageCount { get; }
    bool StageUnlocked(int stageId);
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
