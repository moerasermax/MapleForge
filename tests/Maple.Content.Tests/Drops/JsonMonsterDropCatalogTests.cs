using System.Text;
using Maple.Content.Drops;

namespace Maple.Content.Tests.Drops;

/// <summary>P119：怪物掉落 JSON（由 tools/drop-data/convert_drop_data.py 從 Java drop_data 轉出）。</summary>
public sealed class JsonMonsterDropCatalogTests
{
    [Fact]
    public void RetrieveDrop_ParsesRowsPerDropper()
    {
        var catalog = Load("""{"drops":{"100100":[[4000019,600000,1,1,0],[2000000,40000,1,3,1001]],"100101":[[0,400000,10,20,0]]}}""");

        var drops = catalog.RetrieveDrop(100100);

        Assert.Equal(2, drops.Count);
        Assert.Equal(4000019, drops[0].ItemId);
        Assert.Equal(600000, drops[0].Chance);
        Assert.Equal(3, drops[1].MaximumQuantity);
        Assert.Equal((short)1001, drops[1].QuestId);
        Assert.Equal(2, catalog.MonsterCount);
    }

    [Fact]
    public void RetrieveDrop_UnknownMonster_Empty()
    {
        Assert.Empty(Load("""{"drops":{}}""").RetrieveDrop(999));
    }

    [Fact]
    public void Constructor_SkipsInvalidKeysAndShortRows()
    {
        var catalog = Load("""{"drops":{"abc":[[1,2,3,4,5]],"5":[[1,2],[4000000,10,1,1]]}}""");

        var entry = Assert.Single(catalog.RetrieveDrop(5));
        Assert.Equal((short)0, entry.QuestId);
    }

    [Fact]
    public void RealConvertedFile_WhenProvided_LoadsSnails()
    {
        var path = Environment.GetEnvironmentVariable("MAPLEFORGE_DROP_CATALOG");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return; // 資料檔不進版控；本機設定 MAPLEFORGE_DROP_CATALOG 才驗證
        }

        var catalog = new JsonMonsterDropCatalog(path);

        Assert.True(catalog.MonsterCount > 1000);
        Assert.NotEmpty(catalog.RetrieveDrop(100100)); // 嫩寶
    }

    private static JsonMonsterDropCatalog Load(string json)
        => new(new MemoryStream(Encoding.UTF8.GetBytes(json)));
}
