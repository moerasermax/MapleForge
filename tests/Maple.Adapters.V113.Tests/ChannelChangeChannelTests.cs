using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelChangeChannelTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x1F, V113ChannelRecvOp.ChangeChannel);
        Assert.Equal(0x08, V113ChannelSendOp.ChangeChannel);
    }

    [Fact]
    public void ParseChangeChannel_ReadsTargetChannelByte()
    {
        var targetChannel = V113ChannelChangePackets.ParseChangeChannel(new PacketReader([0x02]));

        Assert.Equal((byte)2, targetChannel);
    }

    [Fact]
    public void ChangeChannel_WritesJavaLayout()
    {
        byte[] expected =
        [
            0x08, 0x00,
            0x01,
            127, 0, 0, 1,
            0x89, 0x21,
        ];

        Assert.Equal(expected, V113ChannelChangePackets.ChangeChannel([127, 0, 0, 1], 8585));
    }

    [Fact]
    public void ChangeChannel_RoundTrip_ReadsOpcodeFlagIpAndPort()
    {
        var packet = V113ChannelChangePackets.ChangeChannel([10, 20, 30, 40], 7575);
        var reader = new PacketReader(packet);

        Assert.Equal(V113ChannelSendOp.ChangeChannel, reader.ReadShort());
        Assert.Equal((byte)1, reader.ReadByte());
        Assert.Equal(new byte[] { 10, 20, 30, 40 }, reader.ReadBytes(4));
        Assert.Equal((short)7575, reader.ReadShort());
        Assert.Equal(0, reader.Remaining);
    }
}
