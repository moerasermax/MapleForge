using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelReactorPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(unchecked((short)0xC9), V113ReactorPackets.DamageReactorRecvOp);
        Assert.Equal(unchecked((short)0xCA), V113ReactorPackets.TouchReactorRecvOp);
        Assert.Equal(0x113, V113ReactorPackets.ReactorHitSendOp);
        Assert.Equal(0x115, V113ReactorPackets.ReactorSpawnSendOp);
        Assert.Equal(0x116, V113ReactorPackets.ReactorDestroySendOp);
    }

    [Fact]
    public void ParseDamageReactor_ReadsJavaLayout()
    {
        var body = new PacketWriter()
            .WriteInt(200000)
            .WriteInt(1)
            .WriteShort(7)
            .ToArray();

        var req = V113ReactorPackets.ParseDamageReactor(new PacketReader(body));

        Assert.Equal(200000, req.ObjectId);
        Assert.Equal(1, req.CharacterPosition);
        Assert.Equal((short)7, req.Stance);
    }

    [Fact]
    public void ParseTouchReactor_ReadsJavaLayout()
    {
        var body = new PacketWriter()
            .WriteInt(200000)
            .WriteByte(1)
            .ToArray();

        var req = V113ReactorPackets.ParseTouchReactor(new PacketReader(body));

        Assert.Equal(200000, req.ObjectId);
        Assert.True(req.Touched);
    }

    [Fact]
    public void SpawnReactor_MatchesJavaLayout()
    {
        var reactor = SampleReactor();

        byte[] golden =
        {
            0x15, 0x01,
            0x40, 0x0D, 0x03, 0x00,
            0xE8, 0x03, 0x00, 0x00,
            0x02,
            0x64, 0x00,
            0xC8, 0x00,
            0x01,
            0x03, 0x00,
            0x62, 0x6F, 0x78,
        };

        Assert.Equal(golden, V113ReactorPackets.SpawnReactor(reactor));
    }

    [Fact]
    public void TriggerReactor_MatchesJavaLayout()
    {
        var reactor = SampleReactor();

        byte[] golden =
        {
            0x13, 0x01,
            0x40, 0x0D, 0x03, 0x00,
            0x02,
            0x64, 0x00,
            0xC8, 0x00,
            0x07, 0x00,
            0x00,
            0x04,
        };

        Assert.Equal(golden, V113ReactorPackets.TriggerReactor(reactor, stance: 7));
    }

    [Fact]
    public void DestroyReactor_MatchesJavaLayout()
    {
        var reactor = SampleReactor();

        byte[] golden =
        {
            0x16, 0x01,
            0x40, 0x0D, 0x03, 0x00,
            0x02,
            0x64, 0x00,
            0xC8, 0x00,
        };

        Assert.Equal(golden, V113ReactorPackets.DestroyReactor(reactor));
    }

    private static Reactor SampleReactor()
    {
        var reactor = new Reactor(
            new MapReactor
            {
                ReactorId = 1000,
                X = 100,
                Y = 200,
                F = 1,
                Name = "box",
            },
            new ReactorStats(Array.Empty<ReactorStateData>()),
            objectId: 200000);
        reactor.ForceState(2);
        return reactor;
    }
}
