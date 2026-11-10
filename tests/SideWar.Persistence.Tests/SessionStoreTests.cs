using Xunit;
using System.Text;
using System.Text.Json;
using SideWar.Persistence.Migrations;
using SideWar.Persistence.Profile;
using SideWar.Persistence.Session;
using static SideWar.Persistence.Tests.StoreTestSupport;

namespace SideWar.Persistence.Tests;

public class SessionStoreTests
{
    private const string D = "/data";
    private const string F = "/data/session.json";

    private static (FakeFileSystem fs, SessionStore st) Make(MigrationRegistry? m = null, FakeFileSystem? fs = null)
    {
        fs ??= new FakeFileSystem();
        return (fs, new SessionStore(fs, D, utcNow: () => Now, migrations: m));
    }

    private static string[] Corrupt(FakeFileSystem fs) => fs.Files.Keys.Where(k => k.StartsWith("/data/corrupt/", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void Load_NoFile_IsNone()
    {
        var (_, st) = Make();
        var r = st.Load("content-A");
        Assert.Equal(SessionLoadStatus.None, r.Status);
        Assert.Null(r.Snapshot);
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesEveryIdleField_CA14_6()
    {
        var (fs, st) = Make();
        var s = FullSession();
        Assert.True(st.Save(s));
        var r = st.Load("content-A");
        Assert.Equal(SessionLoadStatus.Restored, r.Status);
        Assert.Equal(DocumentSource.File, r.Source);
        var o = r.Snapshot!;
        Assert.Equal(Json(s), Json(o));
        // savedUtc, presença, pendências + cardState, layout, slotIndex (inclusive SEM SLOT), Rng, carry, Reservas, região
        Assert.Equal(s.SavedUtc, o.SavedUtc);
        Assert.Equal(DateTimeKind.Utc, o.SavedUtc.Kind);
        Assert.Equal(PresenceStateData.Ausente, o.Presence.State);
        Assert.Equal(611.5, o.Presence.AutoScaleAccumSeconds);
        Assert.False(o.AutoScaleOn);
        Assert.Equal(186_000_000_000_000, o.Pending.Credits);
        Assert.Equal(2, o.Pending.Melhorias.Count);
        Assert.Equal(CardStateData.Recolhido, o.CardState);
        Assert.Equal([150.25f, 300f, 450.125f, 600f], o.Layout.SlotX);
        Assert.Equal(9, o.Sim.Towers[1].SlotIndex);
        Assert.True(o.Sim.Towers[1].SlotIndex >= o.Layout.SlotX.Count);
        Assert.Equal(ulong.MaxValue - 3, o.Sim.Rng.State);
        Assert.Equal(0.123456789012345, o.Sim.Carry);
        Assert.Equal(8, o.Sim.Reservas);
        Assert.Equal("east-us", o.Sim.Region);
        Assert.Equal("0123456789abcdef", o.StateHash);
        Assert.Equal(812.5f, o.Sim.Enemies[0].X);
        Assert.Equal(30.1f, o.Sim.Enemies[0].Speed);
        Assert.Equal(1, SchemaVersionOnDisk(fs.Files[F]));
    }

    [Fact]
    public void SaveLoadSave_IsByteStable()
    {
        var (fs, st) = Make();
        st.Save(FullSession());
        var first = fs.Files[F];
        st.Save(st.Load("content-A").Snapshot!);
        Assert.Equal(first, fs.Files[F]);
    }

    [Fact]
    public void Load_ContentHashDiffers_DiscardsSessionAndKeepsProfile_CA14_4()
    {
        var fs = new FakeFileSystem();
        var profiles = new ProfileStore(fs, D, utcNow: () => Now);
        var (_, st) = Make(fs: fs);
        profiles.Save(FullProfile());
        st.Save(FullSession("content-OLD"));
        st.Save(FullSession("content-OLD")); // cria .bak
        var r = st.Load("content-NEW");
        Assert.Equal(SessionLoadStatus.DiscardedContentMismatch, r.Status);
        Assert.Null(r.Snapshot);
        Assert.False(fs.Files.ContainsKey(F));
        Assert.False(fs.Files.ContainsKey(F + ".bak"));
        Assert.Equal(2, Corrupt(fs).Length);
        Assert.Equal(SessionLoadStatus.None, st.Load("content-NEW").Status);
        var p = profiles.Load();
        Assert.Equal(ProfileSource.File, p.Source);
        Assert.Equal(14, p.Profile.Level);
    }

    [Fact]
    public void Load_ContentMigrationRegisteredForPair_MigratesAndWritesBack()
    {
        var (fs, st) = Make();
        st.Save(FullSession("content-OLD"));
        st.RegisterContentMigration("content-OLD", "content-NEW", node =>
        {
            node["sim"]!["credits"] = 999;
            return node;
        });
        var r = st.Load("content-NEW");
        Assert.Equal(SessionLoadStatus.Restored, r.Status);
        Assert.Equal(999, r.Snapshot!.Sim.Credits);
        Assert.Equal("content-NEW", r.Snapshot.GameContentHash);
        Assert.Equal("content-NEW", Make(fs: fs).st.Load("content-NEW").Snapshot!.GameContentHash);
        Assert.Empty(Corrupt(fs));
    }

    [Fact]
    public void Load_CorruptMain_QuarantinesAndFallsBackToBackup()
    {
        var (fs, st) = Make();
        st.Save(FullSession() with { StateHash = "aaaa" });
        st.Save(FullSession() with { StateHash = "bbbb" });
        fs.Files[F] = fs.Files[F][..100];
        var r = st.Load("content-A");
        Assert.Equal(DocumentSource.Backup, r.Source);
        Assert.Equal("aaaa", r.Snapshot!.StateHash);
        Assert.Single(Corrupt(fs));
    }

    [Theory]
    [InlineData("""{"gameContentHash":"content-A","stateHash":"x","presence":{},"layout":{},"sim":{}}""")] // sem savedUtc
    [InlineData("""{"savedUtc":"2026-11-10T12:00:00Z","stateHash":"x","presence":{},"layout":{},"sim":{}}""")] // sem gameContentHash
    [InlineData("""{"savedUtc":"2026-11-10T12:00:00Z","gameContentHash":"content-A","stateHash":"x","presence":{"state":"Dormindo"},"layout":{},"sim":{}}""")] // enum inválido
    [InlineData("""{"savedUtc":"ontem","gameContentHash":"content-A","stateHash":"x","presence":{},"layout":{},"sim":{}}""")] // data inválida
    [InlineData("""{"savedUtc":"2026-11-10T12:00:00Z","gameContentHash":"content-A","stateHash":"x","presence":{},"layout":{"slotX":[1,2,3,4,5,6,7,8,9,10,11,12,13]},"sim":{}}""")] // slots demais
    public void Load_InvalidPayload_IsQuarantined(string payload)
    {
        var (fs, st) = Make();
        fs.Files[F] = Envelope(payload, "sidewar.session", 1);
        var r = st.Load("content-A");
        Assert.Equal(SessionLoadStatus.None, r.Status);
        Assert.Single(Corrupt(fs));
    }

    [Theory]
    [InlineData("versao-futura")]
    [InlineData("hash-divergente")]
    [InlineData("acima-de-1mb")]
    [InlineData("json-invalido")]
    public void Load_CorruptEnvelope_IsQuarantined_AppStartsNormally(string kind)
    {
        var (fs, st) = Make();
        fs.Files[F] = kind switch
        {
            "versao-futura" => Envelope("{}", "sidewar.session", 2),
            "hash-divergente" => Envelope("{}", "sidewar.session", 1, "00"),
            "json-invalido" => Encoding.UTF8.GetBytes("[["),
            _ => new byte[JsonEnvelope.MaxReadBytes + 1],
        };
        Assert.Equal(SessionLoadStatus.None, st.Load("content-A").Status);
        Assert.Single(Corrupt(fs));
        Assert.False(fs.Files.ContainsKey(F));
    }

    [Fact]
    public void Migration_FictitiousV0ToV1_AppliesAndWritesBack()
    {
        var reg = new MigrationRegistry().Register("sidewar.session", 0, node =>
        {
            var o = node.AsObject();
            o["savedUtc"] = o["salvoEm"]!.DeepClone();
            o.Remove("salvoEm");
            return o;
        });
        var (fs, st) = Make(reg);
        var good = FullSession();
        // v0: o mesmo payload com o campo savedUtc com outro nome
        var json = Json(good).Replace("\"savedUtc\"", "\"salvoEm\"");
        fs.Files[F] = Envelope(json, "sidewar.session", 0);
        var r = st.Load("content-A");
        Assert.Equal(SessionLoadStatus.Restored, r.Status);
        Assert.Equal(good.SavedUtc, r.Snapshot!.SavedUtc);
        Assert.Equal(1, SchemaVersionOnDisk(fs.Files[F]));
        Assert.Equal(Json(good), Json(st.Load("content-A").Snapshot!));
    }

    [Fact]
    public void RejectRestored_MovesMainAndBackupToCorrupt()
    {
        var (fs, st) = Make();
        st.Save(FullSession());
        st.Save(FullSession());
        st.RejectRestored("stateHash divergente");
        Assert.False(fs.Files.ContainsKey(F));
        Assert.False(fs.Files.ContainsKey(F + ".bak"));
        Assert.Equal(2, Corrupt(fs).Length);
        Assert.Equal(SessionLoadStatus.None, st.Load("content-A").Status);
    }

    [Fact]
    public void Delete_RemovesMainAndBackup_WithoutQuarantine()
    {
        var (fs, st) = Make();
        st.Save(FullSession());
        st.Save(FullSession());
        st.Delete();
        Assert.False(fs.Files.ContainsKey(F));
        Assert.False(fs.Files.ContainsKey(F + ".bak"));
        Assert.Empty(Corrupt(fs));
    }

    [Fact]
    public void Save_IoFailure_ReturnsFalse_AndKeepsPreviousFile()
    {
        var (fs, st) = Make();
        st.Save(FullSession() with { StateHash = "old" });
        fs.Fault = (op, _) => { if (op == "replace" || op == "copy") throw new IOException("av"); };
        fs.Fault += (op, _) => { if (op == "write") throw new IOException("sharing"); };
        Assert.False(st.Save(FullSession() with { StateHash = "new" }));
        fs.Fault = null;
        Assert.Equal("old", st.Load("content-A").Snapshot!.StateHash);
    }

    [Fact]
    public void Encode_WriteEncoded_EquivalentToSave()
    {
        var (fs, st) = Make();
        var bytes = st.Encode(FullSession());
        Assert.True(st.WriteEncoded(bytes));
        Assert.Equal(bytes, fs.Files[F]);
        Assert.Equal(Json(FullSession()), Json(st.Load("content-A").Snapshot!));
    }

    [Fact]
    public void SerializedSession_StaysSmall()
    {
        var (_, st) = Make();
        Assert.True(st.Encode(FullSession()).Length < 64 * 1024);
    }
}
