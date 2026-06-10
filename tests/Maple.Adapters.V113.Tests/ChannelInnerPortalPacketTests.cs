using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelInnerPortalPacketTests
{
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
