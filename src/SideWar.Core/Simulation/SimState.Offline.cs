namespace SideWar.Core.Simulation;

/// <summary>Aplicação do resultado offline ao estado (SDD-25 RF-25.10, D-11), sempre entre ticks.</summary>
public sealed partial class SimState
{
    /// <summary>
    /// Reinicia a simulação na onda <paramref name="wave"/> sem inimigos em campo (RF-25.10): cancela encontro de chefe e
    /// pico em fila, esvazia o campo, sobe <c>StageWaveMax</c> para pelo menos <paramref name="waveMax"/> e monta a onda
    /// nova (ou a espera do chefe). Não mexe em créditos, torres nem ledger. Lança durante um tick.
    /// </summary>
    public void RestartAtWave(SimContext ctx, int wave, int waveMax)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (InTick) throw new InvalidOperationException("RestartAtWave só entre ticks.");
        ArgumentOutOfRangeException.ThrowIfLessThan(wave, 1);
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
        Wave = wave;
        if (waveMax > StageWaveMax) StageWaveMax = waveMax;
        if (wave > StageWaveMax) StageWaveMax = wave;
        ClearWave();
        BeginWave(ctx);
    }
}
