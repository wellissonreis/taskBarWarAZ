using SideWar.Core.Economy;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class CreditFormatterTests
{
    [Theory(DisplayName = "T-27.1 Table of RF-27.2")]
    [InlineData(0L, "0")]
    [InlineData(999L, "999")]
    [InlineData(1000L, "1K")]
    [InlineData(1200L, "1,2K")]
    [InlineData(12345L, "12,3K")]
    [InlineData(48200L, "48,2K")]
    [InlineData(186000L, "186K")]
    [InlineData(999950L, "1M")]
    [InlineData(999499L, "999K")]
    [InlineData(999949L, "1M")]
    [InlineData(99950L, "100K")]
    [InlineData(9995L, "10K")]
    [InlineData(1005L, "1,01K")]
    [InlineData(1500000L, "1,5M")]
    [InlineData(2000000000L, "2B")]
    [InlineData(3000000000000L, "3T")]
    [InlineData(1000000000000000L, "1aa")]
    [InlineData(long.MaxValue, "9,22ab")]
    public void Format_Long_MatchesTable(long value, string expected) =>
        Assert.Equal(expected, CreditFormatter.Format(value));

    [Theory(DisplayName = "T-27.1 Double input")]
    [InlineData(999.4, "999")]
    [InlineData(999.5, "1K")]
    [InlineData(48200.0, "48,2K")]
    [InlineData(999950.0, "1M")]
    public void Format_Double_MatchesTable(double value, string expected) =>
        Assert.Equal(expected, CreditFormatter.Format(value));

    [Fact(DisplayName = "T-27.1 All tiers up to zz")]
    public void Suffix_AllTiers_AreUniqueAndOrdered()
    {
        Assert.Equal(["K", "M", "B", "T", "aa", "ab"], Enumerable.Range(1, 6).Select(CreditFormatter.Suffix));
        Assert.Equal("az", CreditFormatter.Suffix(30));
        Assert.Equal("ba", CreditFormatter.Suffix(31));
        Assert.Equal("zz", CreditFormatter.Suffix(CreditFormatter.MaxTier));
        var all = Enumerable.Range(1, CreditFormatter.MaxTier).Select(CreditFormatter.Suffix).ToArray();
        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => CreditFormatter.Suffix(681));
    }

    [Theory(DisplayName = "T-27.1 Large doubles per tier")]
    [InlineData(1e18, "1ab")]
    [InlineData(1e30, "1af")]
    [InlineData(1e300, "1dr")]
    public void Format_LargeDoubles_UseTierSuffix(double value, string expected) =>
        Assert.Equal(expected, CreditFormatter.Format(value));

    [Fact(DisplayName = "T-27.1 Unreachable and negative values")]
    public void Format_SpecialValues()
    {
        Assert.Equal("—", CreditFormatter.Format(double.NaN));
        Assert.Equal("—", CreditFormatter.Format(double.PositiveInfinity));
        Assert.Equal("-1,2K", CreditFormatter.Format(-1200L));
    }

    [Fact(DisplayName = "T-27.1 Culture is fixed")]
    public void Format_IgnoresCurrentCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal("48,2K", CreditFormatter.Format(48200L));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
