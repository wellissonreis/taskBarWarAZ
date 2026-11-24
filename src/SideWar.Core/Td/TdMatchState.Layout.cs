using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>Mapa mundo lógico ↔ DIP da barra (RF-28.15, DP-28.2): o mundo é fixo em <see cref="TdWorld.PathLength"/> unidades.</summary>
public static class TdPlacement
{
    /// <summary>Unidades lógicas → DIP inteiro (arredonda para o mais próximo, aritmética inteira).</summary>
    public static int ToDip(int logicalX, int widthDip) => (int)(((long)logicalX * widthDip + 600) / 1200);

    /// <summary>DIP → unidades lógicas, limitado a [0, 1200].</summary>
    public static int ToLogical(int dip, int widthDip)
    {
        long v = ((long)dip * 1200 + widthDip / 2) / widthDip;
        return (int)Math.Clamp(v, 0, 1200);
    }
}

public sealed partial class TdMatchState
{
    /// <summary>
    /// Layout vigente da barra (zonas livres em DIP); <c>null</c> até o primeiro <c>LayoutChanged</c>. Sem layout o mundo é aberto
    /// (X lógico livre em [0, 1200], sem snap nem sobreposição): o hospedeiro sempre envia o layout antes de jogar.
    /// </summary>
    public StripLayout? Layout => _layout;

    /// <summary>Posição física (DIP) do centro de uma torre no layout vigente.</summary>
    /// <summary>X lógico em que o inimigo toca o núcleo desenhado e vaza: <see cref="TdWorld.CoreEdgeDip"/> convertido pela largura do layout (0 sem layout).</summary>
    public double LeakX => _layout is null ? 0.0 : TdWorld.CoreEdgeDip * TdWorld.PathLength / _layout.Width;

    public int TowerDip(in TdTower t) => TdPlacement.ToDip(t.X, _layout?.Width ?? (int)TdWorld.PathLength);

    /// <summary>Quantas torres estão SEM POSIÇÃO.</summary>
    public int UnplacedCount
    {
        get
        {
            int n = 0;
            foreach (var t in _towers) if (!t.Placed) n++;
            return n;
        }
    }

    /// <summary>
    /// Valida e aplica o snap (RF-28.20, DP-28.3) para o X lógico pedido, sem alterar o estado (serve ao fantasma da UI).
    /// <c>ignoreTowerId</c> (0 = nenhum) exclui a própria torre da checagem de sobreposição.
    /// </summary>
    public CommandReason ValidatePlacement(int x, int ignoreTowerId, out int snappedX) => ResolvePlacement(x, ignoreTowerId, out snappedX);

    private CommandReason ResolvePlacement(int x, int ignoreTowerId, out int snappedX)
    {
        snappedX = 0;
        if (x < 0 || x > (int)TdWorld.PathLength) return CommandReason.OutOfZone;
        if (_layout is null) { snappedX = x; return CommandReason.None; }
        int w = _layout.Width;
        int d = TdPlacement.ToDip(x, w);
        int zi = -1;
        for (int i = 0; i < _layout.FreeZoneCount; i++)
        {
            var z = _layout.ZoneAt(i);
            if (z.X0 <= d && d <= z.X1) { zi = i; break; }
        }
        if (zi < 0) return CommandReason.OutOfZone;
        var zone = _layout.ZoneAt(zi);
        if (zone.Width < PlacementValidator.BoxWidth) return CommandReason.OutOfZone;
        // Snap: centros em X0 + 22 + 44k (mesma grade do idle, DP-33.7), k limitado ao que cabe na zona.
        int k = Math.Min((d - zone.X0) / PlacementValidator.BoxWidth, (zone.Width - PlacementValidator.BoxWidth) / PlacementValidator.BoxWidth);
        int snapDip = zone.X0 + PlacementValidator.HalfBox + PlacementValidator.BoxWidth * k;
        int lx = TdPlacement.ToLogical(snapDip, w);
        int chosen = -1;
        foreach (int c in (ReadOnlySpan<int>)[lx, lx + 1, lx - 1])
        {
            if (c < 0 || c > (int)TdWorld.PathLength) continue;
            if (PlacementValidator.FitsInZone(TdPlacement.ToDip(c, w), _layout)) { chosen = c; break; }
        }
        if (chosen < 0) return CommandReason.OutOfZone;
        int cd = TdPlacement.ToDip(chosen, w);
        foreach (var o in _towers)
        {
            if (!o.Placed || o.Id == ignoreTowerId) continue;
            if (Math.Abs((long)cd - TdPlacement.ToDip(o.X, w)) < PlacementValidator.BoxWidth) return CommandReason.Overlap;
        }
        snappedX = chosen;
        return CommandReason.None;
    }

    private CommandResult DoLayoutChanged(LayoutChanged cmd)
    {
        if (cmd.Layout is null) return Reject(CommandReason.InvalidLayout);
        _layout = cmd.Layout;
        // Reavaliação por Id crescente (RF-11.3): só as de Id menor já reavaliadas contam na sobreposição.
        var before = new bool[_towers.Count];
        for (int i = 0; i < _towers.Count; i++)
        {
            before[i] = _towers[i].Placed;
            var t = _towers[i];
            t.Placed = false;
            _towers[i] = t;
        }
        int w = _layout.Width;
        for (int i = 0; i < _towers.Count; i++)
        {
            var t = _towers[i];
            int d = TdPlacement.ToDip(t.X, w);
            bool ok = PlacementValidator.FitsInZone(d, _layout);
            for (int j = 0; ok && j < i; j++)
                if (_towers[j].Placed && Math.Abs((long)d - TdPlacement.ToDip(_towers[j].X, w)) < PlacementValidator.BoxWidth) ok = false;
            t.Placed = ok;
            _towers[i] = t;
            if (ok != before[i])
            {
                Emit(ok ? TdEventType.TowerPlaced : TdEventType.TowerUnplaced, t.Id, t.X);
                if (!ok) _nodes.RemoveAll(n => n.TowerId == t.Id);
            }
        }
        Emit(TdEventType.LayoutApplied, w, UnplacedCount);
        return CommandResult.Ok;
    }

    private CommandResult DoPlace(PlaceTower cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = FindTower(cmd.TowerId);
        if (i < 0) return Reject(CommandReason.TowerNotFound);
        if (_towers[i].Placed) return Reject(CommandReason.NotUnpositioned);
        return Reposition(i, cmd.X);
    }

    private CommandResult DoMove(MoveTower cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = FindTower(cmd.TowerId);
        if (i < 0) return Reject(CommandReason.TowerNotFound);
        if (_towers[i].Placed) return Reject(CommandReason.NotUnpositioned); // só torre SEM POSIÇÃO muda de lugar
        return Reposition(i, cmd.X);
    }

    /// <summary>Recarga (s) em que a torre movida não atira, para não abusar do deslocamento no meio da onda.</summary>
    public const double RelocateCooldownSeconds = 1.0;

    private CommandResult DoRelocate(RelocateTower cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = FindTower(cmd.TowerId);
        if (i < 0) return Reject(CommandReason.TowerNotFound);
        if (!_towers[i].Placed) return Reject(CommandReason.NotPlaced);
        var reason = ResolvePlacement(cmd.X, cmd.TowerId, out int px);
        if (reason != CommandReason.None) return Reject(reason);
        var t = _towers[i];
        if (px == t.X) return CommandResult.Ok; // mesmo ponto: nada muda, sem recarga
        t.X = px;
        t.CooldownTicks = Math.Max(t.CooldownTicks, Ticks(RelocateCooldownSeconds) + 1);
        t.BurstLeft = 0;
        t.BurstGapTicks = 0;
        t.FocusTargetId = 0;
        t.FocusHits = 0;
        _towers[i] = t;
        _nodes.RemoveAll(n => n.TowerId == t.Id); // nós/pods antigos ficariam no ponto velho
        Emit(TdEventType.TowerPlaced, t.Id, px);
        return CommandResult.Ok;
    }

    private CommandResult Reposition(int i, int x)
    {
        var reason = ResolvePlacement(x, _towers[i].Id, out int px);
        if (reason != CommandReason.None) return Reject(reason);
        var t = _towers[i];
        t.X = px;
        t.Placed = true;
        _towers[i] = t;
        Emit(TdEventType.TowerPlaced, t.Id, px);
        return CommandResult.Ok;
    }
}
