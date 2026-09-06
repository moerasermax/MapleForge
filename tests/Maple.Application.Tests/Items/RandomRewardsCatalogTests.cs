using Maple.Application.Items;

namespace Maple.Application.Tests.Items;

public sealed class RandomRewardsCatalogTests
{
    private static readonly HashSet<int> GoldTableItemIds =
    [
        1302059, 1402037, 1092049, 1102041, 1432018, 1022047, 1432011, 1442020, 1382035, 1372010,
        1332027, 1302056, 1402005, 1472053, 1462018, 1452017, 1422013, 1322029, 1412010, 1472051,
        1482013, 1492013, 1382050, 1382045, 1382047, 1382048, 1382046, 1442018, 1332032, 1482025,
        2290096, 2290049, 2290041, 2290047, 2290095, 2290017, 2290075, 2290085, 2290116,
        2049100, 2040914, 2040900, 2030008, 2000005, 2000004,
        3010051, 3010020,
        4001011, 4001010, 4001009, 4280000,
    ];

    private static readonly HashSet<int> SilverTableItemIds =
    [
        1002452, 1002455, 1102082, 1302049, 1102041, 1452019, 1022060, 1432011, 1442020, 1382035,
        1372010, 1332027, 1302056, 1402005, 1472053, 1462018, 1452017, 1422013, 1322029, 1412010,
        1002587, 1402044, 1442046, 1422031, 1332054, 1012056, 1022047, 1442012, 1442018, 1432010,
        2290084, 2290048, 2290040, 2290046, 2290074, 2290064, 2290094, 2290022, 2290056, 2290066, 2290020,
        2000005, 2000004,
        3010041, 3012002,
        4001116, 4001012, 4280001,
    ];

    [Fact]
    public void GoldBoxReward_FirstIndex_MatchesJavaTableFirstEntry()
    {
        var catalog = new RandomRewardsCatalog(new FixedIndexRandom(0));

        Assert.Equal(1302059, catalog.GetGoldBoxReward()); // Java GameConstants.goldrewards[0] = 龍泉劍
    }

    [Fact]
    public void SilverBoxReward_FirstIndex_MatchesJavaTableFirstEntry()
    {
        var catalog = new RandomRewardsCatalog(new FixedIndexRandom(0));

        Assert.Equal(1002452, catalog.GetSilverBoxReward()); // Java GameConstants.silverrewards[0] = 黑星白頭巾
    }

    [Fact]
    public void GoldTable_IndexEightyNine_IsSuperPotion()
    {
        // Cumulative weight before the 2000005 (超級藥水, weight 10) block in Java goldrewards
        // is 69 (equip) + 9 (skill books) + 11 (2049100/2040914/2040900/2030008) = 89.
        var catalog = new RandomRewardsCatalog(new FixedIndexRandom(89));

        Assert.Equal(2000005, catalog.GetGoldBoxReward());
    }

    [Fact]
    public void GoldTable_IndexNinetyNine_IsSpecialPotion()
    {
        // Immediately after the 10-wide 2000005 block (index 89..98) comes 2000004 (特殊藥水, weight 10) at index 99.
        var catalog = new RandomRewardsCatalog(new FixedIndexRandom(99));

        Assert.Equal(2000004, catalog.GetGoldBoxReward());
    }

    // Java GameConstants.goldrewards/silverrewards 各條目 weight 總和（攤平表長度）。
    private const int GoldTableLength = 127;
    private const int SilverTableLength = 119;

    [Fact]
    public void GoldBoxReward_EveryPossibleIndex_ReturnsOnlyDeclaredTableItems()
    {
        for (var i = 0; i < GoldTableLength; i++)
        {
            var catalog = new RandomRewardsCatalog(new FixedIndexRandom(i));
            Assert.Contains(catalog.GetGoldBoxReward(), GoldTableItemIds);
        }
    }

    [Fact]
    public void SilverBoxReward_EveryPossibleIndex_ReturnsOnlyDeclaredTableItems()
    {
        for (var i = 0; i < SilverTableLength; i++)
        {
            var catalog = new RandomRewardsCatalog(new FixedIndexRandom(i));
            Assert.Contains(catalog.GetSilverBoxReward(), SilverTableItemIds);
        }
    }

    [Fact]
    public void GoldBoxReward_IndexAtTableLength_ThrowsOutOfRange()
    {
        var catalog = new RandomRewardsCatalog(new FixedIndexRandom(GoldTableLength));

        Assert.Throws<IndexOutOfRangeException>(() => catalog.GetGoldBoxReward());
    }

    [Fact]
    public void SilverBoxReward_IndexAtTableLength_ThrowsOutOfRange()
    {
        var catalog = new RandomRewardsCatalog(new FixedIndexRandom(SilverTableLength));

        Assert.Throws<IndexOutOfRangeException>(() => catalog.GetSilverBoxReward());
    }

    private sealed class FixedIndexRandom(int index) : Random
    {
        public override int Next(int maxValue) => index;
    }
}
