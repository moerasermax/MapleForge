using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelMonsterBookPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x32, V113ChannelRecvOp.MonsterBookCover);
        Assert.Equal(0x4E, V113ChannelSendOp.MonsterBookChangeCover);
    }

    [Fact]
    public void ParseChangeCover_ReadsCardId()
    {
        var body = new PacketWriter().WriteInt(2380001).ToArray();

        Assert.Equal(2380001, V113MonsterBookPackets.ParseChangeCover(new PacketReader(body)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2380001, true)]
    [InlineData(2379999, false)]
    [InlineData(-1, false)]
    public void IsMonsterCardOrClear_MatchesJavaMonsterCardRange(int itemId, bool expected)
    {
        Assert.Equal(expected, V113MonsterBookPackets.IsMonsterCardOrClear(itemId));
    }

    [Fact]
    public void ChangeCover_WritesJavaLayout()
    {
        var reader = new PacketReader(V113MonsterBookPackets.ChangeCover(2380001));

        Assert.Equal(V113ChannelSendOp.MonsterBookChangeCover, reader.ReadShort());
        Assert.Equal(2380001, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }
}
