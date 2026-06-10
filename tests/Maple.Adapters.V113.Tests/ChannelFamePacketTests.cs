using Maple.Adapters.V113.Channel;
using Maple.Application.Fame;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelFamePacketTests
{
    [Fact]
    public void ParseGiveFame_ReadsTargetCharacterIdAndMode()
    {
        var w = new PacketWriter();
        w.WriteInt(1234);
        w.WriteByte(1);

        var request = V113FamePackets.ParseGiveFame(new PacketReader(w.ToArray()));

        Assert.Equal(1234, request.TargetCharacterId);
        Assert.Equal(1, request.Mode);
    }

    [Fact]
    public void GiveFameResponse_WritesJavaLayout()
    {
        var packet = V113FamePackets.GiveFameResponse(mode: 1, targetName: "Bob", newFame: 7);

        byte[] expected =
        {
            0x24, 0x00,
            0x00,
            0x03, 0x00,
            0x42, 0x6F, 0x62,
            0x01,
            0x07, 0x00,
            0x00, 0x00,
        };
        Assert.Equal(expected, packet);
    }

    [Fact]
    public void ReceiveFame_WritesJavaLayout()
    {
        var packet = V113FamePackets.ReceiveFame(mode: 0, giverName: "Alice");

        byte[] expected =
        {
            0x24, 0x00,
            0x05,
            0x05, 0x00,
            0x41, 0x6C, 0x69, 0x63, 0x65,
            0x00,
        };
        Assert.Equal(expected, packet);
    }

    [Fact]
    public void GiveFameError_MapsThrottleErrors()
    {
        Assert.Equal(new byte[] { 0x24, 0x00, 0x03 }, V113FamePackets.GiveFameError(FameResultStatus.AlreadyToday));
        Assert.Equal(new byte[] { 0x24, 0x00, 0x04 }, V113FamePackets.GiveFameError(FameResultStatus.AlreadyThisMonth));
    }
}
