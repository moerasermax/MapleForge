using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Application.Tests.Maps;

/// <summary>P063（M4-2 世界 tick）：<see cref="InMemoryMapSessionRegistry.GetAll"/>——背景排程器沒有
/// 「自己」這個概念，需要地圖上「所有人」而非既有 <see cref="IMapSessionRegistry.GetOthers"/>
/// 的排除語意。</summary>
public sealed class InMemoryMapSessionRegistryTests
{
    [Fact]
    public void GetAll_ReturnsEveryRegisteredPlayerIncludingNoExclusion()
    {
        var registry = new InMemoryMapSessionRegistry();
        var alice = NewPlayer(1, "Alice");
        var bob = NewPlayer(2, "Bob");
        registry.Register(100000000, alice.Character.Id, alice, (_, _) => Task.CompletedTask, new object());
        registry.Register(100000000, bob.Character.Id, bob, (_, _) => Task.CompletedTask, new object());

        var all = registry.GetAll(100000000);

        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.CharId == alice.Character.Id);
        Assert.Contains(all, e => e.CharId == bob.Character.Id);
    }

    [Fact]
    public void GetAll_UnknownMap_ReturnsEmpty()
    {
        var registry = new InMemoryMapSessionRegistry();

        Assert.Empty(registry.GetAll(999999999));
    }

    [Fact]
    public void GetAll_AfterDeregister_ExcludesRemovedPlayer()
    {
        var registry = new InMemoryMapSessionRegistry();
        var alice = NewPlayer(1, "Alice");
        var token = new object();
        registry.Register(100000000, alice.Character.Id, alice, (_, _) => Task.CompletedTask, token);

        registry.Deregister(100000000, alice.Character.Id, token);

        Assert.Empty(registry.GetAll(100000000));
    }

    private static Player NewPlayer(int id, string name) =>
        new(new Character { Id = id, Name = name }, new Position(0, 0, 0, 0));
}
