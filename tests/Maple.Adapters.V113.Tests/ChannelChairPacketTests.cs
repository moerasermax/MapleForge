using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelChairPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x23, V113ChannelRecvOp.UseChair);
        Assert.Equal(0x22, V113ChannelRecvOp.CancelChair);
        Assert.Equal(unchecked((short)0xBD), V113ChannelSendOp.ShowChair);
        Assert.Equal(unchecked((short)0xC6), V113ChannelSendOp.CancelChair);
    }

    [Fact]
    public void ShowChair_WritesJavaLayout()
    {
        var packet = V113MapPackets.ShowChair(charId: 1234, itemId: 3010001);
        var reader = new PacketReader(packet);

        Assert.Equal(V113ChannelSendOp.ShowChair, reader.ReadShort());
        Assert.Equal(1234, reader.ReadInt());
        Assert.Equal(3010001, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void CancelChair_ForItemChair_WritesJavaLayout()
    {
        var packet = V113MapPackets.CancelChair(-1);

        Assert.Equal(new byte[]
        {
            0xC6, 0x00,
            0x00,
        }, packet);
    }

    [Fact]
    public void CancelChair_ForMapChair_WritesJavaLayout()
    {
        var packet = V113MapPackets.CancelChair(7);

        Assert.Equal(new byte[]
        {
            0xC6, 0x00,
            0x01,
            0x07, 0x00,
        }, packet);
    }
}
