using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelChalkboardPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x2B, V113ChannelRecvOp.CloseChalkboard);
        Assert.Equal(unchecked((short)0x9C), V113ChannelSendOp.Chalkboard);
    }

    [Fact]
    public void Chalkboard_Close_WritesNoMessageFlag()
    {
        var reader = new PacketReader(V113ChalkboardPackets.Chalkboard(1234, null));

        Assert.Equal(V113ChannelSendOp.Chalkboard, reader.ReadShort());
        Assert.Equal(1234, reader.ReadInt());
        Assert.Equal((byte)0, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void Chalkboard_Open_WritesMessage()
    {
        var reader = new PacketReader(V113ChalkboardPackets.Chalkboard(1234, "shop"));

        Assert.Equal(V113ChannelSendOp.Chalkboard, reader.ReadShort());
        Assert.Equal(1234, reader.ReadInt());
        Assert.Equal((byte)1, reader.ReadByte());
        Assert.Equal("shop", reader.ReadMapleString());
        Assert.Equal(0, reader.Remaining);
    }
}
