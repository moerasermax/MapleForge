using Maple.Core.Maps;

namespace Maple.Core.Tests.Maps;

/// <summary>
/// P049：<see cref="MapData.GetClosestSpawnPoint"/> 對照 Java
/// <c>MapleMap.findClosestSpawnpoint(Point)</c>。
/// </summary>
public sealed class MapDataSpawnPointTests
{
    [Fact]
    public void GetClosestSpawnPoint_ReturnsNearestSpawnPortalByDistance()
    {
        var map = new MapData
        {
            MapId = 100000000,
            Portals =
            [
                new MapPortal { Id = 0, Type = 0, Name = "sp", X = 0, Y = 0 },
                new MapPortal { Id = 1, Type = 0, Name = "sp", X = 1000, Y = 0 },
                new MapPortal { Id = 2, Type = 0, Name = "sp", X = 2000, Y = 0 },
            ],
        };

        var closest = map.GetClosestSpawnPoint(x: 1100, y: 0);

        Assert.NotNull(closest);
        Assert.Equal(1, closest!.Id);
    }

    [Fact]
    public void GetClosestSpawnPoint_IgnoresNonSpawnPortals()
    {
        var map = new MapData
        {
            MapId = 100000000,
            Portals =
            [
                new MapPortal { Id = 0, Type = 2, Name = "out00", X = 0, Y = 0 }, // 可見入口，非出生點
                new MapPortal { Id = 1, Type = 0, Name = "sp", X = 5000, Y = 0 },
            ],
        };

        var closest = map.GetClosestSpawnPoint(x: 0, y: 0);

        Assert.NotNull(closest);
        Assert.Equal(1, closest!.Id);
    }

    [Fact]
    public void GetClosestSpawnPoint_NoSpawnPortals_ReturnsNull()
    {
        var map = new MapData { MapId = 100000000 };

        Assert.Null(map.GetClosestSpawnPoint(x: 0, y: 0));
    }
}
