using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelEventMiniGamePacketTests
{
    [Fact]
    public void RpsMode_Result_WritesSelectionAndAnswer()
    {
        var packet = V113EventMiniGamePackets.RpsMode(11, selection: 2, answer: 1);

        Assert.Equal(new byte[] { 0x44, 0x01, 0x0B, 0x02, 0x01 }, packet);
    }

    [Fact]
    public void HitCoconut_WritesJavaLayout()
    {
        var packet = V113EventMiniGamePackets.HitCoconut(spawn: false, id: 166, type: 3);

        Assert.Equal(new byte[] { 0x1B, 0x01, 0xA6, 0x00, 0x00, 0x00, 0x03 }, packet);
    }

    [Fact]
    public void CoconutScore_WritesTwoShortScores()
    {
        var packet = V113EventMiniGamePackets.CoconutScore(2, 3);

        Assert.Equal(new byte[] { 0x1C, 0x01, 0x02, 0x00, 0x03, 0x00 }, packet);
    }

    [Fact]
    public void UpdateBeans_WritesCharacterIdBeansAndZeroTail()
    {
        var packet = V113EventMiniGamePackets.UpdateBeans(100, 7);
        var reader = new PacketReader(packet);

        Assert.Equal(0x6A, reader.ReadShort());
        Assert.Equal(100, reader.ReadInt());
        Assert.Equal(7, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
    }
}
