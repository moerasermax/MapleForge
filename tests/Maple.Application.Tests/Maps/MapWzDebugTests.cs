using Maple.Content.Wz;

namespace Maple.Application.Tests.Maps;

/// <summary>暫時 debug 測試 - 確認 Map WZ 路徑與屬性解析。</summary>
public sealed class MapWzDebugTests : IDisposable
{
    private const string WzDir = @"D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client";
    private readonly WzDataProvider _provider = new(WzDir);

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void MapWz_Root_Has_Map_Directory()
    {
        var root = _provider.GetRoot("Map");
        Assert.Contains("Map", root.Children.Keys);
    }

    [Fact]
    public void MapWz_Map1_Directory_Exists()
    {
        var map1 = _provider.GetAt("Map", "Map/Map1");
        Assert.NotNull(map1);
        Assert.NotEmpty(map1!.Children);
    }

    [Fact]
    public void MapWz_100000000_Image_Exists()
    {
        var img = _provider.GetAt("Map", "Map/Map1/100000000.img");
        Assert.NotNull(img);
    }

    [Fact]
    public void MapWz_100000000_Has_Info()
    {
        var info = _provider.GetAt("Map", "Map/Map1/100000000.img/info");
        Assert.NotNull(info);
    }

    [Fact]
    public void MapWz_100000000_Info_Town_Is_1()
    {
        var town = _provider.GetAt("Map", "Map/Map1/100000000.img/info/town");
        Assert.NotNull(town);
        Assert.NotNull(town!.Value);
    }

    [Fact]
    public void MapWz_100000000_Has_Portal_Node()
    {
        var portal = _provider.GetAt("Map", "Map/Map1/100000000.img/portal");
        Assert.NotNull(portal);
        Assert.NotEmpty(portal!.Children);
    }
}
