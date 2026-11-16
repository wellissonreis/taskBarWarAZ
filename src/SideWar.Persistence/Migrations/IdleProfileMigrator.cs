using SideWar.Persistence.Profile;

namespace SideWar.Persistence.Migrations;

/// <summary>
/// Migração do save idle (profile.json, schema <c>sidewar.profile</c>) para o perfil TD v2 (DP-28.4, RF-28.17, T-28.8).
/// Corta presença, offline, Reservas, Migrar, missões, ledger e contadores; nada disso é carregado. Só a onda máxima vira selos iniciais.
/// Nunca altera nem apaga o arquivo idle (o corte do store idle é a TD.12) e nunca lança (RNF-28.6).
/// </summary>
public static class IdleProfileMigrator
{
    /// <summary>
    /// Onda máxima observável no save idle: o maior número de onda de chefe vencido entre as fases (<c>ChefesVencidos</c>);
    /// 0 se não há nenhum. O perfil idle não grava "ondaMax" explícito.
    /// </summary>
    public static int IdleMaxWave(ProfileData idle) =>
        (idle.Stages ?? []).Where(s => s is not null).SelectMany(s => s.ChefesVencidos ?? []).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Converte o perfil idle em perfil TD. A fórmula onda máxima → selos NÃO está no SDD-28 e é decisão pendente: o chamador a
    /// fornece em <paramref name="sealsForMaxWave"/> (resultado negativo vira 0, e há teto de segurança).
    /// </summary>
    public static TdProfileData Migrate(ProfileData idle, Func<int, int> sealsForMaxWave)
    {
        ArgumentNullException.ThrowIfNull(idle);
        ArgumentNullException.ThrowIfNull(sealsForMaxWave);
        var seals = Math.Clamp(sealsForMaxWave(IdleMaxWave(idle)), 0, MaxInitialSeals);
        return TdProfileSerializer.Sanitize(TdProfileData.Default with { SealsEarned = seals, MigratedSeals = seals, IdleImportDone = true });
    }

    /// <summary>Teto de selos iniciais (sanidade contra fórmula/saves absurdos): o total da árvore é 30.</summary>
    public const int MaxInitialSeals = 30;

    /// <summary>
    /// Carga com importação única: se o perfil TD não existe (<see cref="TdProfileSource.Defaults"/> sem rejeição) e
    /// <paramref name="idleLoad"/> devolve um save idle, migra e grava. Se existe, devolve como está. Perfil TD corrompido
    /// recomeça do zero sem re-importar (o chamador avisa via <c>RejectReason</c>).
    /// </summary>
    public static TdProfileLoadResult LoadOrMigrate(TdProfileStore td, Func<ProfileLoadResult?> idleLoad, Func<int, int> sealsForMaxWave)
    {
        var r = td.Load();
        if (r.Source != TdProfileSource.Defaults || r.RejectReason is not null) return r;
        ProfileLoadResult? idle;
        try { idle = idleLoad(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { idle = null; }
        var fresh = idle is { Source: not ProfileSource.Defaults } i
            ? Migrate(i.Profile, sealsForMaxWave)
            : TdProfileData.Default with { IdleImportDone = true };
        td.Save(fresh);
        return new(fresh, TdProfileSource.Defaults, null);
    }
}
