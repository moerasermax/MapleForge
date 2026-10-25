using System.Text;
using Maple.Content.Drops;
using Maple.Core.Drops;

namespace Maple.Content.Tests.Drops;

/// <summary>P182：反應堆掉落表 JSON（tools/drop-data/convert_reactor_drops.py 格式）。</summary>
public sealed class JsonReactorDropCatalogTests
{
    [Fact]
    public void Parses_RowsInOrder_DefaultQuestId()
    {
        var catalog = new JsonReactorDropCatalog(new MemoryStream(Encoding.UTF8.GetBytes(
            """{"source":"reactordrops","drops":{"1022002":[[4001340,1,28167],[2000000,5]],"x":[[1,1,1]]}}""")));

        Assert.Equal(1, catalog.ReactorCount);
        Assert.Equal([new ReactorDropEntry(4001340, 1, 28167), new ReactorDropEntry(2000000, 5, -1)], catalog.GetDrops(1022002));
        Assert.Empty(catalog.GetDrops(9999999));
    }
}
