using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelBroadcastPacketTests
{
    [Fact]
    public void OpcodeConstants_MatchJavaSendPacketOpcode()
    {
        Assert.Equal(0x3D, V113ChannelSendOp.ServerMessage);
        Assert.Equal(0x6D, V113ChannelSendOp.AvatarMega);
        Assert.Equal(V113ChannelSendOp.ServerMessage, V113BroadcastPackets.SendServerMessage);
        Assert.Equal(V113ChannelSendOp.AvatarMega, V113BroadcastPackets.SendAvatarMega);
    }

    [Fact]
    public void Megaphone_WritesType2AndMessageOnly()
    {
        var r = ReadServerMessage(V113BroadcastPackets.Megaphone("map hello"), expectedType: 2);

        Assert.Equal("map hello", r.ReadMapleString());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void SuperMegaphone_WritesChannelMinusOneAndEarFlag()
    {
        var r = ReadServerMessage(V113BroadcastPackets.SuperMegaphone("channel hello", channel: 5, ear: true), expectedType: 3);

        Assert.Equal("channel hello", r.ReadMapleString());
        Assert.Equal((byte)4, r.ReadByte());
        Assert.Equal((byte)1, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ItemMegaphone_NullItem_WritesFalseItemFlag()
    {
        var r = ReadServerMessage(V113BroadcastPackets.ItemMegaphone("item hello", channel: 3, ear: false, item: null), expectedType: 8);

        Assert.Equal("item hello", r.ReadMapleString());
        Assert.Equal((byte)2, r.ReadByte());
        Assert.Equal((byte)0, r.ReadByte());
        Assert.Equal((byte)0, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void ItemMegaphone_WithItem_WritesZeroPositionItemInfo()
    {
        var item = new Item
        {
            ItemId = 2000000,
            Quantity = 3,
            Owner = "Alice",
            Flag = 4,
            Expiration = -1,
        };

        var r = ReadServerMessage(V113BroadcastPackets.ItemMegaphone("selling", channel: 1, ear: true, item), expectedType: 8);

        Assert.Equal("selling", r.ReadMapleString());
        Assert.Equal((byte)0, r.ReadByte());
        Assert.Equal((byte)1, r.ReadByte());
        Assert.Equal((byte)1, r.ReadByte());
        Assert.Equal((byte)2, r.ReadByte());
        Assert.Equal(2000000, r.ReadInt());
        Assert.Equal((byte)0, r.ReadByte());
        r.Skip(8);
        Assert.Equal(3, r.ReadShort());
        Assert.Equal("Alice", r.ReadMapleString());
        Assert.Equal(4, r.ReadShort());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void TripleMegaphone_WritesThreeLinesThenChannelAndEar()
    {
        var r = ReadServerMessage(
            V113BroadcastPackets.TripleMegaphone(["one", "two", "three"], channel: 9, ear: false),
            expectedType: 10);

        Assert.Equal("one", r.ReadMapleString());
        Assert.Equal((byte)3, r.ReadByte());
        Assert.Equal("two", r.ReadMapleString());
        Assert.Equal("three", r.ReadMapleString());
        Assert.Equal((byte)8, r.ReadByte());
        Assert.Equal((byte)0, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void TripleMegaphone_OneLine_WritesLineCountWithoutExtraStrings()
    {
        var r = ReadServerMessage(V113BroadcastPackets.TripleMegaphone(["solo"], channel: 2, ear: true), expectedType: 10);

        Assert.Equal("solo", r.ReadMapleString());
        Assert.Equal((byte)1, r.ReadByte());
        Assert.Equal((byte)1, r.ReadByte());
        Assert.Equal((byte)1, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Theory]
    [InlineData(11, 7, true, 6, 1)]
    [InlineData(12, 4, false, 3, 0)]
    public void HeartAndSkullMegaphone_WriteSharedChannelAndEarLayout(
        byte expectedType,
        int channel,
        bool ear,
        byte expectedChannel,
        byte expectedEar)
    {
        var packet = expectedType == 11
            ? V113BroadcastPackets.HeartMegaphone("styled", channel, ear)
            : V113BroadcastPackets.SkullMegaphone("styled", channel, ear);
        var r = ReadServerMessage(packet, expectedType);

        Assert.Equal("styled", r.ReadMapleString());
        Assert.Equal(expectedChannel, r.ReadByte());
        Assert.Equal(expectedEar, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    private static PacketReader ReadServerMessage(byte[] packet, byte expectedType)
    {
        var r = new PacketReader(packet);
        Assert.Equal(V113ChannelSendOp.ServerMessage, r.ReadShort());
        Assert.Equal(expectedType, r.ReadByte());
        return r;
    }
}
