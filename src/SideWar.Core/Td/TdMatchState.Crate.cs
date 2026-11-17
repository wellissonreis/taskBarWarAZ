using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

// Caixa-bomba (SDD-28 RF-28.19): só em Onda, com usuário presente (sinal do log), 1 por vez, contagem em ticks de mundo.
// Os sorteios entram AO FIM da ordem de consumo do Rng em BeginWave (3 uint por onda elegível, sempre, para o consumo ser fixo).
public sealed partial class TdMatchState
{
    private bool _present;
    private int _cratePendingWave;      // onda com queda sorteada ainda não concretizada (0 = nenhuma)
    private long _crateDropTick;        // Tick absoluto da próxima tentativa de queda
    private uint _crateXRaw;            // sorteio do X (resolvido na queda, com o layout vigente)
    private int _nextCrateId = 1;

    /// <summary>Último sinal "presente agora" registrado no log (<see cref="SetPresence"/>).</summary>
    public bool Present => _present;

    /// <summary>Há uma caixa-bomba ativa.</summary>
    public bool CrateActive { get; private set; }
    public int CrateId { get; private set; }
    /// <summary>Posição lógica (RF-28.15) da caixa ativa.</summary>
    public int CrateX { get; private set; }
    /// <summary>Ticks de mundo restantes da contagem.</summary>
    public int CrateTicksLeft { get; private set; }
    public double CrateSecondsLeft => CrateActive ? CrateTicksLeft / (double)TdWorld.TicksPerSecond : 0.0;

    /// <summary>Id da torre posicionada mais próxima da caixa ativa (empate: menor id); 0 sem caixa ou sem torres. Exposto ao render.</summary>
    public int CrateTargetTowerId => CrateActive ? NearestPlacedTower(CrateX) : 0;

    private int NearestPlacedTower(int x)
    {
        int best = 0, bestDist = int.MaxValue;
        foreach (var t in _towers)
        {
            if (!t.Placed) continue;
            int d = Math.Abs(t.X - x);
            if (d < bestDist || (d == bestDist && t.Id < best)) { best = t.Id; bestDist = d; }
        }
        return best;
    }

    private bool CrateEligible(int wave)
    {
        var bc = _content.Stages.Rules.BombCrate;
        return _stageNumber != 1 || wave >= bc.FirstEligibleWaveStage1;
    }

    /// <summary>Chamado ao fim de <c>BeginWave</c>: sorteia (a) se há caixa, (b) o segundo de queda, (c) o X bruto. Sem caixa ativa/pendente.</summary>
    private void RollCrate(int wave, TdSpawn[] spawns)
    {
        if (!CrateEligible(wave)) return;
        var bc = _content.Stages.Rules.BombCrate;
        double r = _rng.NextUInt() / 4294967296.0;
        uint secRaw = _rng.NextUInt();
        uint xRaw = _rng.NextUInt();
        if (r >= bc.Chance || CrateActive || _cratePendingWave != 0) return;
        double spawnSeconds = spawns.Length == 0 ? 0.0 : spawns[^1].DueTick / (double)TdWorld.TicksPerSecond;
        int min = bc.MinDropSeconds;
        int max = Math.Max(min, (int)Math.Floor(spawnSeconds * bc.DropMaxSpawnFraction));
        int sec = min + (int)(secRaw % (uint)(max - min + 1));
        _cratePendingWave = wave;
        _crateDropTick = Tick + (long)sec * TdWorld.TicksPerSecond;
        _crateXRaw = xRaw;
    }

    /// <summary>X inteiro lógico dentro de [margem, 1200 − margem] ∩ zonas livres do layout; <c>false</c> se não há onde cair.</summary>
    private bool TryPickCrateX(uint raw, out int x)
    {
        x = 0;
        int m = _content.Stages.Rules.BombCrate.EdgeMargin;
        int lo = m, hi = (int)TdWorld.PathLength - m;
        if (hi < lo) { x = (int)TdWorld.PathLength / 2; return true; }
        if (_layout is null) { x = lo + (int)(raw % (uint)(hi - lo + 1)); return true; }
        long total = 0;
        for (int i = 0; i < _layout.FreeZoneCount; i++)
        {
            var z = _layout.ZoneAt(i);
            int a = Math.Max(lo, TdPlacement.ToLogical(z.X0, _layout.Width)), b = Math.Min(hi, TdPlacement.ToLogical(z.X1, _layout.Width));
            if (b >= a) total += b - a + 1;
        }
        if (total == 0) return false;
        long pick = raw % (uint)total;
        for (int i = 0; i < _layout.FreeZoneCount; i++)
        {
            var z = _layout.ZoneAt(i);
            int a = Math.Max(lo, TdPlacement.ToLogical(z.X0, _layout.Width)), b = Math.Min(hi, TdPlacement.ToLogical(z.X1, _layout.Width));
            if (b < a) continue;
            long len = b - a + 1;
            if (pick < len) { x = a + (int)pick; return true; }
            pick -= len;
        }
        return false;
    }

    private void TickCrate()
    {
        if (CrateActive)
        {
            if (--CrateTicksLeft <= 0) ExplodeCrate();
            return;
        }
        if (_cratePendingWave == 0) return;
        bool waveAlive = false;
        foreach (var w in _active) if (w.Number == _cratePendingWave) { waveAlive = true; break; }
        if (!waveAlive) { _cratePendingWave = 0; return; } // a onda acabou sem queda: sorteio perdido, sem punição
        if (Tick < _crateDropTick) return;
        if (!_present || !TryPickCrateX(_crateXRaw, out int x)) { _crateDropTick = Tick + TdWorld.TicksPerSecond; return; } // reavalia a cada segundo
        CrateActive = true;
        CrateId = _nextCrateId++;
        CrateX = x;
        CrateTicksLeft = _content.Stages.Rules.BombCrate.CountdownSeconds * TdWorld.TicksPerSecond;
        _cratePendingWave = 0;
        Emit(TdEventType.CrateSpawned, CrateId, x);
    }

    private void ExplodeCrate()
    {
        int target = NearestPlacedTower(CrateX);
        int i = target == 0 ? -1 : FindTower(target);
        if (i >= 0)
        {
            _nodes.RemoveAll(n => n.TowerId == target);
            _towers.RemoveAt(i); // sem reembolso
            _crateTowersLost++;
        }
        Emit(TdEventType.CrateExploded, CrateId, target);
        ClearCrate();
    }

    private void ClearCrate()
    {
        CrateActive = false;
        CrateId = 0;
        CrateX = 0;
        CrateTicksLeft = 0;
    }

    /// <summary>Sai de Onda (Preparo/Vitória/Derrota): a caixa some sem explodir e a queda pendente é perdida.</summary>
    private void CrateLeaveWave()
    {
        if (CrateActive) { Emit(TdEventType.CrateVanished, CrateId); ClearCrate(); }
        _cratePendingWave = 0;
    }

    private CommandResult DoSetPresence(SetPresence cmd)
    {
        if (_present == cmd.Present) return CommandResult.Ok;
        _present = cmd.Present;
        if (!_present && CrateActive) { Emit(TdEventType.CrateVanished, CrateId); ClearCrate(); } // ausência nunca é punida
        return CommandResult.Ok;
    }

    private CommandResult DoCollectCrate(CollectCrate cmd)
    {
        if (Phase != MatchPhase.Onda) return Reject(CommandReason.InvalidPhase);
        if (!CrateActive || cmd.CrateId != CrateId) return Reject(CommandReason.NotAvailable);
        long credits = _content.Runes.CreditSources.CrateBase + (long)_content.Runes.CreditSources.CratePerWave * WaveNumber;
        Credits = Sat.Add(Credits, credits);
        int best = -1;
        for (int i = 0; i < _abilityCd.Length; i++)
            if (_abilityCd[i] > 0 && (best < 0 || _abilityCd[i] > _abilityCd[best])) best = i; // maior recarga; empate: menor índice
        if (best >= 0)
            _abilityCd[best] = Math.Max(0, _abilityCd[best] - _content.Stages.Rules.BombCrate.CooldownReductionSeconds * TdWorld.TicksPerSecond);
        Emit(TdEventType.CrateCollected, CrateId, credits);
        ClearCrate();
        return CommandResult.Ok;
    }
}
