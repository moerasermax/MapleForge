using Maple.Application.Maps;
using Maple.Content.Wz;

namespace Maple.Application.Tests.Maps;

public sealed class MapServiceTests : IDisposable
{
    private const string WzDir = @"D:\WorkSpace\AI_Lab\研究中\MapleStory\V113\v113_Client";
    private readonly WzDataProvider _provider = new(WzDir);
    private readonly MapService _svc;

    public MapServiceTests() => _svc = new MapService(_provider);
    public void Dispose() => _provider.Dispose();

    [Fact]
    public void Henesys_Loads_With_Portals()
    {
        var map = _svc.LoadMap(100000000);
        Assert.Equal(100000000, map.MapId);
        Assert.NotEmpty(map.Portals);
    }

    [Fact]
    public void Henesys_Has_Spawn_Points()
    {
        var map = _svc.LoadMap(100000000);
        var spawns = map.Portals.Where(p => p.IsSpawnPoint).ToList();
        Assert.NotEmpty(spawns);
    }

    [Fact]
    public void Henesys_GetSpawnPoint_Returns_Valid_Position()
    {
        var map = _svc.LoadMap(100000000);
        var spawn = map.GetSpawnPoint(0);
        Assert.NotNull(spawn);
        Assert.True(spawn.X != 0 || spawn.Y != 0, "spawn position should not be origin (0,0)");
    }

    [Fact]
    public void Henesys_Has_Footholds()
    {
        var map = _svc.LoadMap(100000000);
        Assert.NotEmpty(map.Footholds);
    }

    [Fact]
    public void Henesys_Is_Town()
    {
        var map = _svc.LoadMap(100000000);
        Assert.True(map.Town);
    }
}
