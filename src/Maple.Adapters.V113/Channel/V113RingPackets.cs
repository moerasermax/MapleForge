using Maple.Application.Social;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113RingRequest(byte Mode, string TargetName, int ItemId, bool Accepted, int CharacterId)
{
    public bool IsProposal => Mode == 0;

    public bool IsCancel => Mode == 1;

    public bool IsReply => Mode == 2;

    public bool IsEtcDrop => Mode == 3;
}

internal static class V113RingPackets
{
    public const short RingActionRecvOpcode = 0x81;
    public const short MarriageRequestSendOpcode = 0x41;
    public const short MarriageResultSendOpcode = 0x42;
    public const short MarriageUpdateSendOpcode = 0x62;
    public const short ShowForeignEffectSendOpcode = unchecked((short)0xBF);

    public const byte MarriageRequestModeEngage = 0;
    public const byte EngagementSuccess = 0x10;
    public const byte EngagementDeclined = 0x1E;
    public const byte UnverifiedRingEffect = 0x0F;

    public static V113RingRequest ParseRingAction(PacketReader reader)
    {
        var mode = reader.ReadByte();
        return mode switch
        {
            0 => new V113RingRequest(
                mode,
                reader.ReadMapleString(),
                reader.ReadInt(),
                Accepted: false,
                CharacterId: 0),
            1 => new V113RingRequest(mode, string.Empty, 0, Accepted: false, CharacterId: 0),
            2 => ParseReply(mode, reader),
            3 => new V113RingRequest(mode, string.Empty, reader.ReadInt(), Accepted: false, CharacterId: 0),
            _ => new V113RingRequest(mode, string.Empty, 0, Accepted: false, CharacterId: 0),
        };
    }

    private static V113RingRequest ParseReply(byte mode, PacketReader reader)
    {
        var accepted = reader.ReadByte() > 0;
        var name = reader.ReadMapleString();
        var characterId = reader.ReadInt();
        return new V113RingRequest(mode, name, ItemId: 0, accepted, characterId);
    }

    public static byte[] MarriageRequest(string proposerName, int proposerCharacterId)
    {
        var w = new PacketWriter(2 + 1 + 2 + proposerName.Length + 4);
        w.WriteShort(MarriageRequestSendOpcode);
        w.WriteByte(MarriageRequestModeEngage);
        w.WriteMapleString(proposerName);
        w.WriteInt(proposerCharacterId);
        return w.ToArray();
    }

    public static byte[] MarriageResult(byte message, int itemId = 0, string maleName = "", string femaleName = "", int maleId = 0, int femaleId = 0)
    {
        var w = new PacketWriter(64);
        w.WriteShort(MarriageResultSendOpcode);
        w.WriteByte(message);
        if (message == 11)
        {
            w.WriteInt(0);
            w.WriteInt(maleId);
            w.WriteInt(femaleId);
            w.WriteShort(1);
            w.WriteInt(itemId);
            w.WriteInt(itemId);
            w.WriteFixedAsciiString(maleName, 15);
            w.WriteFixedAsciiString(femaleName, 15);
        }

        return w.ToArray();
    }

    public static byte[] MarriageResult(RingActionStatus status)
    {
        var code = RingService.JavaEngagementError(status);
        return code == 0 ? Array.Empty<byte>() : MarriageResult(code);
    }

    public static byte[] MarriageUpdate(string characterName, bool family = false)
    {
        var w = new PacketWriter(2 + 1 + 2 + characterName.Length);
        w.WriteShort(MarriageUpdateSendOpcode);
        w.WriteByte(family ? 1 : 0);
        w.WriteMapleString(characterName);
        return w.ToArray();
    }

    public static byte[] MarriageRingLook(Player player)
    {
        var w = new PacketWriter(16);
        w.WriteByte(player.HasVisibleMarriageRing ? 1 : 0);
        if (player.HasVisibleMarriageRing)
        {
            w.WriteInt(player.Character.Id);
            w.WriteInt(player.MarriagePartnerCharacterId);
            w.WriteInt((int)player.MarriageRingId);
        }

        return w.ToArray();
    }

    public static byte[] ShowRingEffectCandidate(int characterId)
    {
        var w = new PacketWriter(7);
        w.WriteShort(ShowForeignEffectSendOpcode);
        w.WriteInt(characterId);
        w.WriteByte(UnverifiedRingEffect);
        return w.ToArray();
    }
}
