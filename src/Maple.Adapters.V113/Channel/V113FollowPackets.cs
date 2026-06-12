using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113FollowRequest(int TargetCharacterId, byte MapChange, byte Cancel)
{
    public bool IsMapChangeResume => MapChange > 0;

    public bool IsCancel => Cancel > 0;
}

internal readonly record struct V113FollowReply(int RequesterCharacterId, bool Accepted);

internal static class V113FollowPackets
{
    public const short FollowRequestRecvOpcodeCandidate = 0x78;
    public const short FollowReplyRecvOpcodeCandidate = 0x7A;

    public const short FollowRequestSendOpcodeDisabledInJava = -2;
    public const short FollowEffectSendOpcodeCandidate = unchecked((short)0xB7);
    public const short FollowMessageSendOpcodeCandidate = unchecked((short)0xFD);
    public const short FollowMoveSendOpcodeCandidate = 0x101;
    public const short FollowMsgSendOpcodeCandidate = 0x102;

    public static V113FollowRequest ParseFollowRequest(PacketReader reader)
    {
        var targetId = reader.ReadInt();
        var mapChange = reader.ReadByte();
        var cancel = reader.ReadByte();
        return new V113FollowRequest(targetId, mapChange, cancel);
    }

    public static V113FollowReply ParseFollowReply(PacketReader reader)
    {
        var requesterId = reader.ReadInt();
        var accepted = reader.ReadByte() > 0;
        return new V113FollowReply(requesterId, accepted);
    }

    public static byte[] FollowRequest(int requesterCharacterId)
    {
        var w = new PacketWriter(6);
        w.WriteShort(FollowRequestSendOpcodeDisabledInJava);
        w.WriteInt(requesterCharacterId);
        return w.ToArray();
    }

    public static byte[] FollowEffect(int initiatorCharacterId, int replierCharacterId, int? toMapX = null, int? toMapY = null)
    {
        var w = new PacketWriter(16);
        w.WriteShort(FollowEffectSendOpcodeCandidate);
        w.WriteInt(initiatorCharacterId);
        w.WriteInt(replierCharacterId);
        if (replierCharacterId == 0)
        {
            var hasMapPoint = toMapX.HasValue && toMapY.HasValue;
            w.WriteByte(hasMapPoint ? 1 : 0);
            if (hasMapPoint)
            {
                w.WriteInt(toMapX!.Value);
                w.WriteInt(toMapY!.Value);
            }
        }

        return w.ToArray();
    }

    public static byte[] FollowMsg(long opcode)
    {
        var w = new PacketWriter(10);
        w.WriteShort(FollowMsgSendOpcodeCandidate);
        w.WriteLong(opcode);
        return w.ToArray();
    }

    public static byte[] FollowMessage(string message)
    {
        var w = new PacketWriter(2 + 2 + 2 + message.Length);
        w.WriteShort(FollowMessageSendOpcodeCandidate);
        w.WriteShort(0x0B);
        w.WriteMapleString(message);
        return w.ToArray();
    }
}
