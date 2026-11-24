namespace SideWar.Rendering.Scene;

/// <summary>
/// DESIGN-ANIM: tabela das animações do design "Torres — animações de ataque" / "Monstros — sprites animados"
/// (assets/source/anim, MANIFEST.json). Só dados: tempos copiados da duração CSS <c>steps(N)</c> do design, mapa design → id de conteúdo
/// (td-towers/td-enemies). Torres/inimigos fora das tabelas mantêm a arte vetorial/atlas de <see cref="TdArtMap"/>.
/// </summary>
public static class TdAnimArt
{
    public const int IdleFrames = 4, AttackFrames = 6, FxFrames = 4, WalkFrames = 4;

    /// <summary>Parada 4 quadros a 6 fps (0,667 s).</summary>
    public const double IdleFps = 6;

    /// <summary>Ataque 6 quadros a 12 fps (0,5 s): carrega (0-1), dispara no quadro <see cref="FireFrame"/> (0-based) e recua (3-5).</summary>
    public const double AttackFps = 12;

    /// <summary>Projétil/efeito 4 quadros a 10 fps (0,4 s).</summary>
    public const double FxFps = 10;

    /// <summary>Quadro (0-based) do disparo na folha de ataque; coincide com o tick em que a recarga reinicia.</summary>
    public const int FireFrame = 2;

    /// <summary>Quanto do desenho do design o projétil leva até o alvo (só visual; o Core não expõe o projétil).</summary>
    public const int ProjectileTicks = 6;

    /// <summary>Escala do efeito de área do CDN em relação à torre (design: <c>sf-cdn</c> com <c>scale(2.2)</c>).</summary>
    public const float CdnAreaScale = 2.2f;

    /// <summary>Ids de conteúdo (td-towers) com arte animada: o nome do design entre parênteses no REPORT / MANIFEST.json.</summary>
    public static readonly string[] Towers =
        ["bolt", "cannon", "frost", "balancer", "scaleset", "functions", "cdn", "cosmos", "policy", "frontdoor", "waf", "shield", "sentinel", "pipeline", "eventgrid", "bastion", "backup", "vault", "firewall", "queue", "aks"];

    /// <summary>Inimigos (td-enemies) com ciclo de caminhada de 4 quadros e o fps do design (frames / duração CSS).</summary>
    public static readonly (string Id, double Fps)[] Enemies =
    [
        ("swarm", 8), ("runner", 12), ("armored", 6), ("flyer", 10), ("leech", 6), ("tank", 6), ("ransomware", 6), ("botnet", 5),
    ];

    /// <summary>Nome lógico do recurso embutido de um arquivo de assets/source/anim.</summary>
    public static string Resource(string file) => "SideWar.Rendering.Assets.Anim." + file;

    public static bool HasTower(string id) => id == "dns" || Array.IndexOf(Towers, id) >= 0;

    public static string TowerSheetId(string id) => id == "dns" ? "bolt" : id;

    public static bool TryEnemyFps(string id, out double fps)
    {
        foreach (var e in Enemies)
        {
            if (e.Id == id) { fps = e.Fps; return true; }
        }
        fps = 0;
        return false;
    }

    /// <summary>Quadro de um ciclo em loop no instante <paramref name="seconds"/> (com defasagem de quadros por instância).</summary>
    public static int LoopFrame(double seconds, double fps, int frames, int phase = 0)
    {
        long f = (long)Math.Floor(seconds * fps) + phase;
        return (int)(((f % frames) + frames) % frames);
    }
}
