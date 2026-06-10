using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelFaceExpressionPacketTests
{
    [Fact]
    public void Opcodes_MatchJavaProperties()
    {
        Assert.Equal(0x2C, V113ChannelRecvOp.FaceExpression);
        Assert.Equal(unchecked((short)0xB9), V113ChannelSendOp.FacialExpression);
    }

    [Fact]
    public void FacialExpression_WritesJavaLayout()
    {
        var packet = V113MapPackets.FacialExpression(charId: 1234, expression: 5);
        var reader = new PacketReader(packet);

        Assert.Equal(V113ChannelSendOp.FacialExpression, reader.ReadShort());
        Assert.Equal(1234, reader.ReadInt());
        Assert.Equal(5, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }
}
