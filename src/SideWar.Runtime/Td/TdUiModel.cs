using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Modo do popover/UI da partida TD (TD.8, SDD-28 RF-28.14/28.20). Estado de UI: nunca vai ao log nem ao hash.</summary>
public enum TdUiMode : byte { None, Build, Inspector, Preview, Menu, Map, End }

/// <summary>Tipo de item de UI: rótulo, botão, cabeçalho de seção ou separador.</summary>
public enum TdUiKind : byte { Label, Button, Header, Separator }

/// <summary>Tom de cor semântico (o renderer mapeia para a paleta).</summary>
public enum TdTone : byte { Normal, Good, Warn, Bad, Accent, Dim }

/// <summary>Ação disparada por um botão do HUD/popover.</summary>
public enum TdUiAction : byte
{
    None, Close, BuildToggle, ChooseTower, StartWave, Ability, Menu, Upgrade, Branch, Targeting, Sell, Reposition,
    Preview, MapOpen, MapTab, SelectStage, BuyNode, Pause, Settings, Exit, Restart, NextStage,
}

/// <summary>
/// Item de layout (DIP, origem no canto superior esquerdo da janela). HUD e popover usam o mesmo tipo: o layout e o hit-test são puros
/// (Runtime) e o renderer só desenha. <c>Arg</c>/<c>SArg</c> carregam o alvo (id de torre, fase, id de torre/nó/habilidade).
/// </summary>
public readonly record struct TdUiItem(
    DipRect Rect, TdUiKind Kind, string Text, TdUiAction Action = TdUiAction.None, int Arg = 0, string? SArg = null,
    string? Right = null, string? Sub = null, bool Enabled = true, bool Selected = false, TdTone Tone = TdTone.Normal, float Fill = 0f)
{
    public bool Clickable => Kind == TdUiKind.Button && Enabled && Action != TdUiAction.None;
}

/// <summary>Popover (acima da faixa): título, itens (o item 0, quando botão de fechar, fica no canto) e âncora em X (DIP da faixa).</summary>
public sealed record TdPopoverView(string Title, TdUiItem[] Items, float Width, float Height, float AnchorX)
{
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
        h.Add(Title);
        foreach (var it in Items)
        {
            h.Add(it.Text); h.Add(it.Right); h.Add(it.Sub); h.Add(it.Enabled); h.Add(it.Selected); h.Add((int)it.Tone); h.Add((int)(it.Fill * 100));
        }
        return h.ToHashCode();
    }
}

/// <summary>HUD compacto (RF-28.14) na área de 160 DIP à direita da faixa.</summary>
public sealed record TdHudView(TdUiItem[] Items, float Width, float Height)
{
    public const float DefaultWidth = TdBand.HudAreaDip, DefaultHeight = 62f;

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
            h.Add(it.Text); h.Add(it.Right); h.Add(it.Enabled); h.Add(it.Selected); h.Add((int)it.Tone); h.Add((int)(it.Fill * 100));
        }
        return h.ToHashCode();
    }
}

/// <summary>Visão do perfil/meta para o Mapa e a árvore (implementada no App sobre o perfil TD; o Runtime não conhece a Persistência).</summary>
public interface ITdUiHost
{
    bool Paused { get; }
    void TogglePause();
    void OpenSettings();
    void RequestExit();

    /// <summary>Inicia (ou reinicia) a partida na fase; o App cria a nova partida e reenvia o layout.</summary>
    void StartStage(int stageId);

    int StageCount { get; }
    bool StageUnlocked(int stageId);
    int StageStars(int stageId);
    int SealsAvailable { get; }
    bool NodePurchased(string nodeId);
    bool PurchaseNode(string nodeId);
}
