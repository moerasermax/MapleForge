using Maple.Adapters.V113.Channel;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>P172：角色 → 連線換圖入口（WarpAll 依地圖名單逐一排入；舊 token 註銷不影響新連線）。</summary>
public sealed class SessionCommandRegistryTests
{
    [Fact]
    public void WarpAll_PostsToEveryoneOnMap_StaleTokenUnregisterIgnored()
    {
        var maps = new InMemoryMapSessionRegistry();
        var registry = new V113SessionCommandRegistry(maps);
        var warps = new List<(int Char, int Map, int Portal)>();
        var tokens = new Dictionary<int, object> { [1] = new(), [2] = new() };
        foreach (var id in new[] { 1, 2 })
        {
            var player = new Player(new Character { Id = id, Name = $"P{id}", MapId = 101000301 }, new Position(0, 0, 0, 0));
            maps.Register(101000301, id, player, (_, _) => Task.CompletedTask, new object());
            registry.Register(id, tokens[id], (map, portal) => { warps.Add((id, map, portal)); return true; });
        }

        Assert.Equal(2, registry.WarpAll(101000301, 200090010, 0));
        Assert.Equal([(1, 200090010, 0), (2, 200090010, 0)], warps.OrderBy(static w => w.Char));

        registry.Unregister(1, new object()); // 舊連線的 token
        Assert.True(registry.Warp(1, 100000000, 0));
        registry.Unregister(1, tokens[1]);
        Assert.False(registry.Warp(1, 100000000, 0));
    }
}
