namespace SideWar.Core.Td;

public sealed partial class TdMatchState
{
    /// <summary>Id de conteúdo do tipo de inimigo (somente leitura; usado pelo catálogo de alertas).</summary>
    public string EnemyDefId(int defIndex) => _enemyStats[defIndex].Id;

}
