using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelItemEffectPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x2D, V113ChannelRecvOp.UseItemEffect);
        Assert.Equal(0x43, V113ChannelRecvOp.CancelItemEffect);
        Assert.Equal(unchecked((short)0xBA), V113ChannelSendOp.ShowItemEffect);
    }

    [Fact]
    public void ItemEffect_WritesJavaLayout()
    {
        var packet = V113MapPackets.ItemEffect(charId: 1234, itemId: 5010000);
        var reader = new PacketReader(packet);

        Assert.Equal(V113ChannelSendOp.ShowItemEffect, reader.ReadShort());
        Assert.Equal(1234, reader.ReadInt());
        Assert.Equal(5010000, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ItemEffect_ZeroCanClearEffect()
    {
        var packet = V113MapPackets.ItemEffect(charId: 1234, itemId: 0);

        Assert.Equal(new byte[]
        {
            0xBA, 0x00,
            0xD2, 0x04, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        }, packet);
    }
}
