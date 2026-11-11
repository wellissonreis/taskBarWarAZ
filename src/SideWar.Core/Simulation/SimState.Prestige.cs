using SideWar.Core.Content;

namespace SideWar.Core.Simulation;

/// <summary>
/// Região atual e aplicação do "Migrar" (SDD-27 RF-27.10), mais o pagamento de recompensas de baú/missão (RF-27.13).
/// A regra (disponibilidade, Reservas, desbloqueio) é de <see cref="Prestige.PrestigeService"/>; aqui só a mutação do estado.
/// </summary>
public sealed partial class SimState
{
    /// <summary>Índice da região vigente em <c>regions.json</c> (0 = Brazil South). No hash.</summary>
    public int RegionIndex { get; private set; }

    /// <summary>Migrações já feitas (desbloqueia East US). No hash; persistido com o perfil.</summary>
    public int Migrations { get; private set; }

    /// <summary>Restore (SDD-14): região vigente e contador de migrações; o HP regional vale a partir da próxima onda.</summary>
    public void RestoreRegion(SimContext ctx, int regionIndex, int migrations)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var regions = ctx.Content.Regions;
        RegionIndex = Math.Clamp(regionIndex, 0, regions.Length - 1);
        Migrations = Math.Max(0, migrations);
        _regionHp = regions[RegionIndex].HpMultiplier;
    }

    /// <summary>
    /// Migrar (RF-27.10), só entre ticks, validado por <c>PrestigeService</c>: soma as Reservas, reinicia onda 1, torres e
    /// créditos iniciais da fase atual e troca a região. Mantém nível, XP, estrelas, fases, Reservas, perguntas
    /// respondidas (o ledger passa a <c>StageAttempt + 1</c>) e missões. Cancela encontro de chefe e pico em fila.
    /// </summary>
    internal void ApplyMigration(SimContext ctx, int regionIndex, long reservesGain)
    {
        var eco = ctx.Content.Economy;
        if (_encounter is { IsActive: true } enc)
        {
            enc.Cancel();
            _sched?.BossResolved(false);
            if (_sched is not null) DrainScheduler();
        }
        _encounter = null;
        _bossHold = false;
        _peakQueue = 0;
        Enemies.Clear();
        Towers.Clear();
        Reservas = Sat.Add(Reservas, reservesGain);
        Credits = eco.StartingCredits;
        Carry = 0;
        Migrations++;
        RegionIndex = regionIndex;
        _regionHp = ctx.Content.Regions[regionIndex].HpMultiplier;
        Wave = 1;
        StageWaveMax = 1;
        StageFalls = 0;
        BossQuestionsCorrect = 0;
        StageAttempt++;
        if (Stage <= MaxStages) _lastBossWave[Stage] = 0;   // nova passagem: sem chefe vencido para o recuo
        ClearWave();
        BeginWave(ctx);
        Events.Add(new SimEvent(SimEventType.Migrated, 0f, 0f, reservesGain, regionIndex));
    }

    /// <summary>
    /// Entrega uma recompensa de baú ou missão (RF-27.13), sem multiplicador de presença: créditos pelo carry, XP pelo carry
    /// (o nível sobe no próximo tick) e Reservas na produção na hora. Só entre ticks.
    /// </summary>
    public void GrantReward(double credits, long xp, long reserves)
    {
        if (InTick) throw new InvalidOperationException("GrantReward só entre ticks.");
        if (credits > 0) PayCredits(credits);
        if (xp > 0) AddXp(xp);
        if (reserves > 0) Reservas = Sat.Add(Reservas, reserves);
    }
}
