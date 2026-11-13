using SideWar.Core.Simulation;
using SideWar.Persistence.IO;
using SideWar.Persistence.Mapping;
using SideWar.Persistence.Profile;
using SideWar.Persistence.Session;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Presence;

namespace SideWar.App.Composition;

/// <summary>
/// Persistência da raiz de composição (SDD-14): restaura a partida no início (RF-14.8) e serializa sessão + perfil
/// na T-Game (SDD-01 RF-01.4 regra 5); a escrita acontece na T-IO (ThreadPool) ou síncrona quando o gatilho exige
/// (suspensão, encerramento). Guarda os últimos bytes da sessão para o save de emergência do crash (RF-07.12).
/// </summary>
internal sealed class SessionPersistence
{
    private readonly SessionStore sessions;
    private readonly ProfileStore profiles;
    private readonly Action<string> log;
    private readonly object writeGate = new();
    private ProfileData baseProfile;
    private byte[]? lastSession;

    public SessionPersistence(string dataDirectory, string appVersion, Action<string> log, IFileSystem? fileSystem = null)
    {
        var fs = fileSystem ?? RealFileSystem.Instance;
        sessions = new SessionStore(fs, dataDirectory, appVersion);
        profiles = new ProfileStore(fs, dataDirectory, appVersion);
        this.log = log;
        baseProfile = profiles.Load().Profile;
    }

    /// <summary>Últimos bytes de sessão gravados ou codificados (save de emergência; qualquer thread).</summary>
    public byte[]? LastSessionBytes => Volatile.Read(ref lastSession);

    public long Saves { get; private set; }

    /// <summary>Snapshot lido do <c>session.json</c> e restaurado com sucesso (instante, auto-escala, pendência do card); null = partida nova.</summary>
    public SideWar.Persistence.Session.OfflineSnapshot? RestoredSession { get; private set; }

    /// <summary>Restaura com o contexto do runtime; sessão inválida/de outro conteúdo = partida nova (o perfil fica).</summary>
    public SimState? Restore(SimContext ctx)
    {
        var loaded = sessions.Load(ctx.Content.ContentHash);
        if (loaded.Status == SessionLoadStatus.DiscardedContentMismatch) log("session: discarded (content hash mismatch); new game");
        if (loaded.Snapshot is not { } snapshot) return null;
        var restored = SimSnapshotMapper.Restore(ctx, snapshot);
        if (restored.IsRestored)
        {
            RestoredSession = snapshot;
            return restored.State;
        }
        log($"session: restore failed ({restored.Status}: {restored.Detail}); new game");
        sessions.RejectRestored(restored.Detail ?? restored.Status.ToString());
        return null;
    }

    /// <summary>Restaura <c>pending</c>/<c>cardState</c> no controlador do card (depois do runtime; semeia os ids do ledger).</summary>
    public void RestorePending(OfflineCardController card, SimState state)
    {
        if (RestoredSession is { } snapshot) PendingMapper.Restore(card, snapshot, state);
    }

    /// <summary>Serializa na T-Game (estado vivo). Devolve os bytes de sessão e perfil prontos para escrever.</summary>
    public (byte[] Session, byte[] Profile) Encode(GameRuntime runtime, OfflineCardController? card = null)
    {
        var presence = new PresenceData
        {
            State = runtime.Presence.State switch
            {
                PresenceState.Presente => PresenceStateData.Presente,
                PresenceState.Ausente => PresenceStateData.Ausente,
                _ => PresenceStateData.Offline,
            },
            LastInteractionUtc = runtime.Presence.LastInteractionUtc,
            PresentSeconds = runtime.Presence.PresentSeconds,
            AutoScaleAccumSeconds = runtime.Host.AutoScaleAccumSeconds,
        };
        var profile = SimSnapshotMapper.ToProfile(runtime.State, runtime.Context, baseProfile);
        // SDD-14 RF-14.5 / SDD-25 RF-25.11: pending + cardState vão junto da sessão (nunca se perdem ao fechar o app).
        var (pending, cardState) = card is null ? (new PendingData(), CardStateData.Aberto) : PendingMapper.ToSession(card);
        var snapshot = SimSnapshotMapper.ToSession(runtime.State, runtime.Context, DateTime.UtcNow, presence, profile,
            runtime.Host.AutoScaleEnabled, pending, cardState);
        baseProfile = profile;
        var session = sessions.Encode(snapshot);
        Volatile.Write(ref lastSession, session);
        return (session, profiles.Encode(profile));
    }

    /// <summary>Escreve (atômico) sessão e perfil; qualquer thread, serializado entre si.</summary>
    public bool Write(byte[] session, byte[] profile)
    {
        lock (writeGate)
        {
            bool ok = sessions.WriteEncoded(session) & profiles.WriteEncoded(profile);
            Saves++;
            if (!ok) log("save: write failed");
            return ok;
        }
    }
}
