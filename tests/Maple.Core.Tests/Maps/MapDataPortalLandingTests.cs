using Maple.Core.Maps;

namespace Maple.Core.Tests.Maps;

/// <summary>P084：<see cref="MapData.GetPortalOrFirst"/> 對照 Java <c>getPortal(id)</c> 找不到時 <c>getPortal(0)</c>。</summary>
public sealed class MapDataPortalLandingTests
{
    private static readonly MapData Map = new()
    {
        MapId = 100000000,
        Portals =
        [
            new MapPortal { Id = 0, Type = 0, Name = "sp", X = 10, Y = 20 },
            new MapPortal { Id = 3, Type = 2, Name = "west00", X = -500, Y = 150 },
        ],
    };

    [Fact]
    public void GetPortalOrFirst_KnownId_ReturnsThatPortal()
    {
        Assert.Equal(3, Map.GetPortalOrFirst(3)!.Id);
    }

    [Fact]
    public void GetPortalOrFirst_UnknownId_FallsBackToPortalZero()
    {
        Assert.Equal(0, Map.GetPortalOrFirst(99)!.Id);
    }

    [Fact]
    public void GetPortalOrFirst_NoPortals_ReturnsNull()
    {
        Assert.Null(new MapData { MapId = 1 }.GetPortalOrFirst(0));
    }
}
