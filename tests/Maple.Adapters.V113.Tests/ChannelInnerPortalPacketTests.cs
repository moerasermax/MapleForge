using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelInnerPortalPacketTests
{
    [Fact]
    public void IsFarFromPortal_WithinRange_ReturnsFalse()
    {
        // 對照 Java：distanceSq <= 22500（150 格）不算過遠。
        var portal = new MapPortal { X = 0, Y = 0 };
        var position = new Position(150, 0, 0, 0);

        Assert.False(V113ChannelConnectionHandler.IsFarFromPortal(portal, position));
    }

    [Fact]
    public void IsFarFromPortal_ExactlyAtThreshold_ReturnsFalse()
    {
        // distanceSq == 22500（剛好 150 格）對照 Java "> 22500" 不算過遠（邊界值不觸發）。
        var portal = new MapPortal { X = 150, Y = 0 };
        var position = new Position(0, 0, 0, 0);

        Assert.False(V113ChannelConnectionHandler.IsFarFromPortal(portal, position));
    }

    [Fact]
    public void IsFarFromPortal_BeyondRange_ReturnsTrue()
    {
        var portal = new MapPortal { X = 200, Y = 0 };
        var position = new Position(0, 0, 0, 0);

        Assert.True(V113ChannelConnectionHandler.IsFarFromPortal(portal, position));
    }

    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x5F, V113ChannelRecvOp.UseInnerPortal);
        Assert.Equal(unchecked((short)0xC8), V113ChannelSendOp.CurrentMapWarp);
    }

    [Fact]
    public void ParseUseInnerPortal_ReadsSkipNameAndTargetPosition()
    {
        var body = new PacketWriter()
            .WriteByte(0)
            .WriteMapleString("in00")
            .WriteShort(100)
            .WriteShort(200)
            .ToArray();

        var request = V113InnerPortalPackets.ParseUseInnerPortal(new PacketReader(body));

        Assert.Equal("in00", request.PortalName);
        Assert.Equal((short)100, request.X);
        Assert.Equal((short)200, request.Y);
    }

    [Fact]
    public void CurrentMapWarp_WritesJavaLayout()
    {
        byte[] expected =
        {
            0xC8, 0x00,
            0x00,
            0x06,
        };

        Assert.Equal(expected, V113InnerPortalPackets.CurrentMapWarp(6));
    }
}
