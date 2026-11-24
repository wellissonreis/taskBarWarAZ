using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>
/// Torre "de viagem": o que precisa sobreviver a troca de fase, "Reiniciar corrida" e reabertura do app (nível/ramo não existem; ficam tipo, posição,
/// preço pago, mira e módulo de fusão). Estado transitório de combate (recargas, rajadas, foco) recomeça zerado.
/// </summary>
public readonly record struct TdTowerCarry(string DefId, int X, bool Placed, long PurchasePrice, byte Targeting, string? ModuleDefId, long ModulePrice);

public sealed partial class TdMatchState
{
    /// <summary>Torres atuais (inclusive as SEM POSIÇÃO) prontas para carregar a outra partida ou para o perfil.</summary>
    public TdTowerCarry[] ExportTowers()
    {
        var list = new TdTowerCarry[_towers.Count];
        for (int i = 0; i < list.Length; i++)
        {
            var t = _towers[i];
            list[i] = new(_content.Towers[t.DefIndex].Id, t.X, t.Placed, t.PurchasePrice, t.Targeting, t.HasModule ? _content.Towers[t.ModuleDefIndex].Id : null, t.ModulePrice);
        }
        return list;
    }

    /// <summary>
    /// Entrega as torres a uma partida recém-criada (sem torres). Devolve quantas entraram; as que o <c>LayoutChanged</c> seguinte não comportar
    /// ficam SEM POSIÇÃO (reserva: o jogador recoloca de graça). Nunca reembolsa nem descarta por falta de posição.
    /// </summary>
    public int ImportTowers(IEnumerable<TdTowerCarry> towers)
    {
        ArgumentNullException.ThrowIfNull(towers);
        if (_towers.Count > 0) return 0;
        foreach (var c in towers)
        {
            if (_towers.Count >= TechnicalTowerLimit) break;
            int di = TowerDefIndex(c.DefId);
            if (di < 0) continue;
            int mi = c.ModuleDefId is null ? -1 : TowerDefIndex(c.ModuleDefId);
            _towers.Add(new TdTower
            {
                Id = _nextTowerId++, DefIndex = di, PurchasePrice = Math.Max(0, c.PurchasePrice), X = Math.Clamp(c.X, 0, (int)TdWorld.PathLength), Placed = c.Placed,
                Targeting = c.Targeting, ModuleSlot = mi + 1, ModulePrice = mi >= 0 ? Math.Max(0, c.ModulePrice) : 0,
            });
        }
        if (_towers.Count > _peakTowers) _peakTowers = _towers.Count;
        return _towers.Count;
    }

    /// <summary>
    /// Depois de um <c>LayoutChanged</c>: recoloca cada torre SEM POSIÇÃO no ponto válido mais próximo do lugar original (grátis). A que não achar ponto
    /// continua na reserva (SEM POSIÇÃO) e o jogador a recoloca depois, também de graça. Devolve quantas foram recolocadas.
    /// </summary>
    public int AutoPlaceReserve()
    {
        if (_layout is null) return 0;
        int placed = 0, max = (int)TdWorld.PathLength;
        for (int i = 0; i < _towers.Count; i++)
        {
            if (_towers[i].Placed) continue;
            int origin = _towers[i].X;
            for (int d = 0; d <= max; d += 4)
            {
                bool done = false;
                foreach (int x in d == 0 ? (ReadOnlySpan<int>)[origin] : [origin - d, origin + d])
                {
                    if (x < 0 || x > max || ResolvePlacement(x, _towers[i].Id, out int px) != CommandReason.None) continue;
                    var t = _towers[i];
                    t.X = px; t.Placed = true;
                    _towers[i] = t;
                    Emit(TdEventType.TowerPlaced, t.Id, px);
                    placed++; done = true;
                    break;
                }
                if (done) break;
            }
        }
        return placed;
    }
}
