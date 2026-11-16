using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

// Habilidades ativas (SDD-28 RF-28.9): recarga e efeito contados em ticks de mundo (só andam em Onda).
public sealed partial class TdMatchState
{
    public IReadOnlyList<TdAbilityStatus> AbilityStatuses
    {
        get
        {
            var list = new List<TdAbilityStatus>(_abilityCd.Length);
            for (int i = 0; i < _abilityCd.Length; i++)
            {
                var d = _content.Abilities[i];
                list.Add(new TdAbilityStatus(d.Id, IsAbilityUnlocked(d), _abilityCd[i], _abilityActive[i],
                    d.UsesPerStage is int u ? Math.Max(0, u - _abilityUses[i]) : -1));
            }
            return list;
        }
    }

    private int AbilityIndex(string id)
    {
        for (int i = 0; i < _content.Abilities.Length; i++)
            if (_content.Abilities[i].Id == id) return i;
        return -1;
    }

    private bool IsAbilityActive(string id)
    {
        int i = AbilityIndex(id);
        return i >= 0 && _abilityActive[i] > 0;
    }

    private double AbilityStat(string id, string key)
    {
        int i = AbilityIndex(id);
        return i >= 0 && _content.Abilities[i].Stats.TryGetValue(key, out var v) ? v : 0.0;
    }

    private double FirewallSlow() => AbilityStat("firewall", "slow");
    private double FirewallBossSlow() => AbilityStat("firewall", "bossSlow");

    private bool IsAbilityUnlocked(TdAbilityDef d) =>
        (d.UnlockStage is int s && _stage.Id >= s) || (d.UnlockMetaNode is string n && _metaNodes.Contains(n));

    private void TickAbilities()
    {
        for (int i = 0; i < _abilityCd.Length; i++)
        {
            if (_abilityCd[i] > 0) _abilityCd[i]--;
            if (_abilityActive[i] > 0) _abilityActive[i]--;
        }
    }

    private CommandResult DoUseAbility(UseAbility cmd)
    {
        if (Phase != MatchPhase.Onda) return Reject(CommandReason.InvalidPhase);
        int i = cmd.AbilityId is null ? -1 : AbilityIndex(cmd.AbilityId);
        if (i < 0) return Reject(CommandReason.NotAvailable);
        var d = _content.Abilities[i];
        if (!IsAbilityUnlocked(d)) return Reject(CommandReason.NotAvailable);
        if (d.UsesPerStage is int u && _abilityUses[i] >= u) return Reject(CommandReason.AlreadyUsed);
        if (_abilityCd[i] > 0) return Reject(CommandReason.NotAvailable);
        if (d.Id == "snapshot")
        {
            if (Integrity >= IntegrityMax) return Reject(CommandReason.NotAvailable); // nada a restaurar
            Integrity = Math.Min(IntegrityMax, Integrity + (int)AbilityStat("snapshot", "integrity"));
        }
        _abilityActive[i] = Ticks(d.DurationSeconds);
        _abilityCd[i] = Ticks(d.CooldownSeconds);
        _abilityUses[i]++;
        Emit(TdEventType.AbilityUsed, i);
        return CommandResult.Ok;
    }
}
