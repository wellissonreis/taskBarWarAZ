using SideWar.Runtime.Input;
using SideWar.Runtime.Td;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>TD.16b: a faixa só aceita clique no modo posicionar; os testes entram nele pelo botão Construir antes de clicar num ponto.</summary>
internal static class TdUiTestExtensions
{
    public static void ClickPoint(this TdUiController ui, float x, float y = 20)
    {
        if (!ui.BuildActive) ui.Activate(new TdUiItem(default, TdUiKind.Button, "Construir", TdUiAction.BuildToggle));
        ui.SceneLeftClick(x, y, HitTarget.None);
    }

    public static void OpenTowerFromList(this TdUiController ui, int towerId) =>
        ui.Activate(new TdUiItem(default, TdUiKind.Button, "Torre", TdUiAction.OpenTower, towerId));
}
