namespace SideWar.Core.Td;

public sealed partial class TdMatchState
{
    /// <summary>
    /// Cópia independente do estado para simular passos sem tocar na partida real (proteção contra derrota, SDD-38 §3).
    /// Conteúdo e definições imutáveis são compartilhados; tudo que muda a cada passo é copiado. Não consome RNG da original.
    /// </summary>
    public TdMatchState CloneForSimulation()
    {
        var c = (TdMatchState)MemberwiseClone();
        c._stageResults = [.. _stageResults];
        c._abilityCd = (int[])_abilityCd.Clone();
        c._abilityActive = (int[])_abilityActive.Clone();
        c._abilityUses = (int[])_abilityUses.Clone();
        c._mutDebut = (int[])_mutDebut.Clone();
        c._towers = [.. _towers];
        c._enemies = [.. _enemies];
        c._active = [.. _active.Select(w => w.Copy())];
        c._nodes = [.. _nodes];
        c._zones = [.. _zones];
        c._held = [.. _held];
        c._events = [.. _events];
        c._killsByTower = new Dictionary<int, int>(_killsByTower);
        c._runeEffects = new Dictionary<string, int>(_runeEffects);
        c._auraCadence = (double[])_auraCadence.Clone();
        c._auraDamage = (double[])_auraDamage.Clone();
        c._auraRange = (double[])_auraRange.Clone();
        c._leakCount = (int[]?)_leakCount?.Clone();
        c._leakDamage = (int[]?)_leakDamage?.Clone();
        return c;
    }
}
