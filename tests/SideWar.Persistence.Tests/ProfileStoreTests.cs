using Xunit;
using System.Text;
using SideWar.Persistence.Migrations;
using SideWar.Persistence.Profile;
using static SideWar.Persistence.Tests.StoreTestSupport;

namespace SideWar.Persistence.Tests;

public class ProfileStoreTests
{
    private const string D = "/data";
    private const string F = "/data/profile.json";

    private static (FakeFileSystem fs, ProfileStore st) Make(MigrationRegistry? m = null)
    {
        var fs = new FakeFileSystem();
        return (fs, new ProfileStore(fs, D, utcNow: () => Now, migrations: m));
    }

    private static string[] Corrupt(FakeFileSystem fs) => fs.Files.Keys.Where(k => k.StartsWith("/data/corrupt/", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void Load_NoFiles_ReturnsNewProfile()
    {
        var (_, st) = Make();
        var r = st.Load();
        Assert.Equal(ProfileSource.Defaults, r.Source);
        Assert.Equal(1, r.Profile.Level);
        Assert.Null(r.Profile.Seed);
        Assert.Equal("brazil-south", r.Profile.Region);
        Assert.Equal(["brazil-south"], r.Profile.UnlockedRegions);
    }

    [Fact]
    public void SaveLoad_RoundTrip_AllFields()
    {
        var (fs, st) = Make();
        var p = FullProfile();
        Assert.True(st.Save(p));
        Assert.Equal(1, SchemaVersionOnDisk(fs.Files[F]));
        var r = st.Load();
        Assert.Equal(ProfileSource.File, r.Source);
        Assert.Equal(Json(p), Json(r.Profile));
        Assert.Equal(ulong.MaxValue, r.Profile.Seed);
        Assert.Null(r.Profile.Ledger[1].OptionIndex);
        Assert.Equal(MissionStateData.Completed, r.Profile.Missions.Items[0].State);
        Assert.Equal(0x1234567890ABCDEFUL, r.Profile.Rng!.Inc);
    }

    [Fact]
    public void Save_SecondTime_KeepsBakAndLoadsLatest()
    {
        var (fs, st) = Make();
        st.Save(FullProfile());
        st.Save(FullProfile() with { Level = 15 });
        Assert.True(fs.Files.ContainsKey(F + ".bak"));
        Assert.Equal(15, st.Load().Profile.Level);
    }

    [Theory]
    [InlineData("truncado")]
    [InlineData("json-invalido")]
    [InlineData("versao-futura")]
    [InlineData("hash-divergente")]
    [InlineData("schema-errado")]
    [InlineData("tipo-errado")]
    [InlineData("acima-de-1mb")]
    public void Load_CorruptMain_QuarantinesAndUsesBackup(string kind)
    {
        var (fs, st) = Make();
        st.Save(FullProfile() with { Level = 3 });
        st.Save(FullProfile() with { Level = 4 }); // .bak = nível 3
        var good = fs.Files[F];
        fs.Files[F] = kind switch
        {
            "truncado" => good[..(good.Length / 2)],
            "json-invalido" => Encoding.UTF8.GetBytes("{ nope"),
            "versao-futura" => Envelope("{}", "sidewar.profile", 99),
            "hash-divergente" => Envelope("{}", "sidewar.profile", 1, "00"),
            "schema-errado" => Envelope("{}", "sidewar.settings", 1),
            "tipo-errado" => Envelope("""{"level":"muitos"}""", "sidewar.profile", 1),
            _ => new byte[JsonEnvelope.MaxReadBytes + 1],
        };
        var r = st.Load();
        Assert.Equal(ProfileSource.Backup, r.Source);
        Assert.Equal(3, r.Profile.Level);
        Assert.Single(Corrupt(fs));
    }

    [Fact]
    public void Load_BothCorrupt_ReturnsNewProfile()
    {
        var (fs, st) = Make();
        fs.Files[F] = Encoding.UTF8.GetBytes("lixo");
        fs.Files[F + ".bak"] = Encoding.UTF8.GetBytes("lixo");
        var r = st.Load();
        Assert.Equal(ProfileSource.Defaults, r.Source);
        Assert.Equal(2, Corrupt(fs).Length);
    }

    [Fact]
    public void Load_OutOfRangeFields_AreSanitizedPerField()
    {
        var (fs, st) = Make();
        fs.Files[F] = Envelope("""{"level":-5,"xp":-1,"reservas":10,"stages":[{"stageId":1,"unlocked":true,"stars":9,"attempt":0},{"stageId":0,"stars":1}],"region":"","counters":{"kills":-3}}""", "sidewar.profile", 1);
        var p = st.Load().Profile;
        Assert.Equal(1, p.Level);
        Assert.Equal(0, p.Xp);
        Assert.Equal(10, p.Reservas);
        Assert.Single(p.Stages);
        Assert.Equal(3, p.Stages[0].Stars);
        Assert.Equal(1, p.Stages[0].Attempt);
        Assert.Equal("brazil-south", p.Region);
        Assert.Equal(0, p.Counters.Kills);
    }

    [Fact]
    public void Load_MissingFields_UseDefaults()
    {
        var (fs, st) = Make();
        fs.Files[F] = Envelope("""{"level":7}""", "sidewar.profile", 1);
        var r = st.Load();
        Assert.Equal(ProfileSource.File, r.Source);
        Assert.Equal(7, r.Profile.Level);
        Assert.Empty(r.Profile.Ledger);
    }

    [Fact]
    public void Migration_FictitiousV0ToV1_AppliesAndWritesBackNewVersion()
    {
        var reg = new MigrationRegistry().Register("sidewar.profile", 0, node =>
        {
            var o = node.AsObject();
            o["level"] = o["nivel"]!.DeepClone();
            o.Remove("nivel");
            return o;
        });
        var (fs, st) = Make(reg);
        fs.Files[F] = Envelope("""{"nivel":9,"reservas":2}""", "sidewar.profile", 0);
        var r = st.Load();
        Assert.Equal(9, r.Profile.Level);
        Assert.Equal(2, r.Profile.Reservas);
        Assert.Equal(1, SchemaVersionOnDisk(fs.Files[F]));
        Assert.Equal(9, st.Load().Profile.Level);
        Assert.Equal(ProfileSource.File, st.Load().Source);
    }

    [Fact]
    public void Migration_NotRegistered_QuarantinesV0()
    {
        var (fs, st) = Make();
        fs.Files[F] = Envelope("""{"nivel":9}""", "sidewar.profile", 0);
        Assert.Equal(ProfileSource.Defaults, st.Load().Source);
        Assert.Single(Corrupt(fs));
    }

    [Fact]
    public void Migration_StepThrows_QuarantinesInsteadOfCrashing()
    {
        var reg = new MigrationRegistry().Register("sidewar.profile", 0, _ => throw new InvalidOperationException("boom"));
        var (fs, st) = Make(reg);
        fs.Files[F] = Envelope("""{"nivel":9}""", "sidewar.profile", 0);
        Assert.Equal(ProfileSource.Defaults, st.Load().Source);
        Assert.Single(Corrupt(fs));
    }

    [Fact]
    public void Registry_DuplicateStep_Throws_AndMinVersionFollowsChain()
    {
        var reg = new MigrationRegistry().Register("s", 0, n => n);
        Assert.Throws<InvalidOperationException>(() => reg.Register("s", 0, n => n));
        Assert.Equal(0, reg.MinVersion("s", 1));
        Assert.Equal(1, reg.MinVersion("outro", 1));
        Assert.False(reg.TryMigrate("s", 0, 2, System.Text.Json.Nodes.JsonNode.Parse("{}")!, out _, out var why));
        Assert.Contains("v1", why);
    }

    [Fact]
    public void Save_IoFailure_ReturnsFalse_AndDoesNotThrow()
    {
        var (fs, st) = Make();
        fs.Fault = (op, _) => { if (op == "write") throw new UnauthorizedAccessException("negado"); };
        Assert.False(st.Save(FullProfile()));
    }

    [Fact]
    public void Encode_WriteEncoded_EquivalentToSave()
    {
        var (fs, st) = Make();
        var bytes = st.Encode(FullProfile());
        Assert.True(st.WriteEncoded(bytes));
        Assert.Equal(bytes, fs.Files[F]);
        Assert.Equal(Json(FullProfile()), Json(st.Load().Profile));
    }
}
