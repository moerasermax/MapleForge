using Maple.Adapters.V113.Channel;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelFollowPacketTests
{
    [Fact]
    public void Opcodes_DocumentJavaDisabledAndCommentedCandidates()
    {
        Assert.Equal(0x78, V113FollowPackets.FollowRequestRecvOpcodeCandidate);
        Assert.Equal(0x7A, V113FollowPackets.FollowReplyRecvOpcodeCandidate);
        Assert.Equal(-2, V113FollowPackets.FollowRequestSendOpcodeDisabledInJava);
        Assert.Equal(unchecked((short)0xB7), V113FollowPackets.FollowEffectSendOpcodeCandidate);
        Assert.Equal(unchecked((short)0xFD), V113FollowPackets.FollowMessageSendOpcodeCandidate);
        Assert.Equal(0x101, V113FollowPackets.FollowMoveSendOpcodeCandidate);
        Assert.Equal(0x102, V113FollowPackets.FollowMsgSendOpcodeCandidate);
    }

    [Fact]
    public void ParseFollowRequest_ReadsTargetMapChangeAndCancelFlags()
    {
        var body = new PacketWriter()
            .WriteInt(2002)
            .WriteByte(0)
            .WriteByte(1)
            .ToArray();

        var request = V113FollowPackets.ParseFollowRequest(new PacketReader(body));

        Assert.Equal(2002, request.TargetCharacterId);
        Assert.False(request.IsMapChangeResume);
        Assert.True(request.IsCancel);
    }

    [Fact]
    public void ParseFollowReply_ReadsRequesterAndAcceptedFlag()
    {
        var body = new PacketWriter()
            .WriteInt(1001)
            .WriteByte(1)
            .ToArray();

        var reply = V113FollowPackets.ParseFollowReply(new PacketReader(body));

        Assert.Equal(1001, reply.RequesterCharacterId);
        Assert.True(reply.Accepted);
    }

    [Fact]
    public void FollowEffect_WritesJavaLayoutForAcceptedRelation()
    {
        byte[] expected =
        {
            0xB7, 0x00,
            0xE9, 0x03, 0x00, 0x00,
            0xEA, 0x03, 0x00, 0x00,
        };

        Assert.Equal(expected, V113FollowPackets.FollowEffect(1001, 1002));
    }

    [Fact]
    public void FollowMsg_WritesJavaCanceledRequestLayout()
    {
        byte[] expected =
        {
            0x02, 0x01,
            0x05, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        };

        Assert.Equal(expected, V113FollowPackets.FollowMsg(5));
    }

    [Fact]
    public void FollowMessage_WritesJavaLayout()
    {
        byte[] expected =
        {
            0xFD, 0x00,
            0x0B, 0x00,
            0x02, 0x00,
            0x4F, 0x4B,
        };

        Assert.Equal(expected, V113FollowPackets.FollowMessage("OK"));
    }
}
