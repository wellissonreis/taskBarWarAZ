namespace SideWar.Runtime.Panel;

/// <summary>Resultado de um clique no botão MIGRAR.</summary>
public enum MigrateClick { Ignored, Armed, Confirmed }

/// <summary>
/// Confirmação do MIGRAR por segundo clique em 5 s (SDD-24 RF-24.8 itens 1–4; CA-24.8). Puro: usa <see cref="IClock"/>.
/// O primeiro clique arma e <b>não</b> migra; o segundo em ≤ 5 s confirma (o host emite <c>Migrate(regionId)</c>).
/// Cada transição (armar, expirar, cancelar, confirmar) sobe <see cref="Version"/> uma vez = um evento de render.
/// Sem timer próprio: o host agenda 1 acordar em <see cref="DeadlineTicks"/> e chama <see cref="Update"/>.
/// </summary>
public sealed class MigrateArming(IClock clock, double windowSeconds = 5.0)
{
    public bool Armed { get; private set; }
    public long Version { get; private set; }

    /// <summary>Instante (ticks do <see cref="IClock"/>) em que o estado armado expira; 0 se não armado.</summary>
    public long DeadlineTicks { get; private set; }

    private long WindowTicks => (long)(windowSeconds * clock.TicksPerSecond);

    /// <summary>Desarma se o prazo passou. Retorna true se houve transição (um render).</summary>
    public bool Update()
    {
        if (!Armed || clock.NowTicks < DeadlineTicks) return false;
        return Disarm();
    }

    /// <summary>Mouse up no botão. <paramref name="available"/> = false (onda máx. &lt; 15): ignorado.</summary>
    public MigrateClick Click(bool available)
    {
        Update();
        if (!available) return MigrateClick.Ignored;
        if (!Armed)
        {
            Armed = true;
            DeadlineTicks = clock.NowTicks + WindowTicks;
            Version++;
            return MigrateClick.Armed;
        }
        Disarm();
        return MigrateClick.Confirmed;
    }

    /// <summary>Troca de aba, fechar o painel ou clique em qualquer outro ponto (rolar não cancela). True se estava armado.</summary>
    public bool Cancel() => Disarm();

    private bool Disarm()
    {
        if (!Armed) return false;
        Armed = false;
        DeadlineTicks = 0;
        Version++;
        return true;
    }
}
