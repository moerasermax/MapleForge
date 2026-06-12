using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class ReactorTests
{
    [Fact]
    public void Reactor_IsFieldObject_WithDedicatedTypeValue()
    {
        var reactor = CreateReactor();

        Assert.Equal(200001, reactor.ObjectId);
        Assert.Equal(ReactorFieldObjectTypes.Reactor, reactor.Type);
        Assert.Equal((short)100, reactor.Position.X);
        Assert.Equal((short)200, reactor.Position.Y);
    }

    [Fact]
    public void Hit_ReactorAdvancesStateAndRequestsScriptOnFinalState()
    {
        var reactor = CreateReactor(delayMs: 0);

        var hit = reactor.Hit(charPosition: 1, stance: 7);

        Assert.True(hit.Applied);
        Assert.Equal((byte)0, hit.OldState);
        Assert.Equal((byte)1, hit.NewState);
        Assert.Equal(ReactorPacketAction.Hit, hit.PacketAction);
        Assert.True(hit.ShouldInvokeScript);
        Assert.True(reactor.IsAlive);
    }

    [Fact]
    public void Hit_WithDelayDestroysFinalReactor()
    {
        var reactor = CreateReactor(delayMs: 5000);

        var hit = reactor.Hit(charPosition: 1, stance: 0);

        Assert.True(hit.Applied);
        Assert.Equal(ReactorPacketAction.Destroy, hit.PacketAction);
        Assert.False(reactor.IsAlive);
        Assert.True(hit.ShouldInvokeScript);
    }

    [Fact]
    public void Hit_TypeTwoFromBlockedSideDoesNotAdvance()
    {
        var reactor = CreateReactor(type: 2);

        var hit = reactor.Hit(charPosition: 0, stance: 0);

        Assert.False(hit.Applied);
        Assert.Equal((byte)0, reactor.State);
    }

    private static Reactor CreateReactor(int delayMs = 0, int type = 0) => new(
        new MapReactor
        {
            ReactorId = 1000,
            X = 100,
            Y = 200,
            F = 1,
            ReactorTimeMs = delayMs,
            Name = "box",
        },
        new ReactorStats(new[]
        {
            new ReactorStateData(0, type, null, 0, 1, -1),
            new ReactorStateData(1, 999, null, 0, -1, 0),
        }),
        objectId: 200001);
}
