using Maple.Application.OnlinePlayers;
using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Application.Tests.OnlinePlayers;

public sealed class InMemoryOnlinePlayerRegistryTests
{
    [Fact]
    public void GetAll_ReturnsEveryRegisteredPlayer()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        registry.Register(Player(1, "Alice"), channel: 1, SendNoop, new object());
        registry.Register(Player(2, "Bob"), channel: 1, SendNoop, new object());

        var all = registry.GetAll();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.CharacterId == 1);
        Assert.Contains(all, p => p.CharacterId == 2);
    }

    [Fact]
    public void GetAll_ExcludesDeregisteredPlayer()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        var token = new object();
        registry.Register(Player(1, "Alice"), channel: 1, SendNoop, token);
        registry.Register(Player(2, "Bob"), channel: 1, SendNoop, new object());

        registry.Deregister(1, token);

        var all = registry.GetAll();
        Assert.Single(all);
        Assert.Equal(2, all[0].CharacterId);
    }

    [Fact]
    public void GetAll_ReturnsSnapshot_UnaffectedByLaterRegistrations()
    {
        var registry = new InMemoryOnlinePlayerRegistry();
        registry.Register(Player(1, "Alice"), channel: 1, SendNoop, new object());

        var snapshot = registry.GetAll();
        registry.Register(Player(2, "Bob"), channel: 1, SendNoop, new object());

        Assert.Single(snapshot);
    }

    private static Player Player(int id, string name) =>
        new(new Character { Id = id, Name = name, MapId = 100000000 }, new Position(0, 0, 0, 0));

    private static Task SendNoop(byte[] packet, CancellationToken ct) => Task.CompletedTask;
}
