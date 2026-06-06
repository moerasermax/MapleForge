using Maple.Application.Parties;
using Maple.Core.IO;
using Maple.Core.Parties;

namespace Maple.Adapters.V113.Channel;

internal enum V113PartyClientOperation : byte
{
    Create = 1,
    Leave = 2,
    Join = 3,
    Invite = 4,
    Expel = 5,
    ChangeLeader = 6,
}

internal static class V113PartyPackets
{
    public const short RecvPartyOperationOpcode = 0x74;
    public const short SendPartyOperationOpcode = 0x37;
    public const short SendUpdatePartyMemberHpOpcode = unchecked((short)0xC2);

    public const byte StatusBeginnerCannotCreate = 10;
    public const byte StatusUnknownError = 11;
    public const byte StatusNotInParty = 13;
    public const byte StatusAlreadyInParty = 16;
    public const byte StatusPartyFull = 17;
    public const byte StatusCannotFindCharacter = 19;

    public static V113PartyClientOperation ReadOperation(PacketReader reader) =>
        (V113PartyClientOperation)reader.ReadByte();

    public static byte[] PartyCreated(int partyId)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPartyOperationOpcode);
        w.WriteByte(8);
        w.WriteInt(partyId);
        w.WriteInt(PartyMember.NoDoorMapId);
        w.WriteInt(PartyMember.NoDoorMapId);
        w.WriteInt(0);
        return w.ToArray();
    }

    public static byte[] PartyInvite(int partyId, string inviterName, bool auto = false)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPartyOperationOpcode);
        w.WriteByte(4);
        w.WriteInt(partyId);
        w.WriteMapleString(inviterName);
        w.WriteByte(auto ? 1 : 0);
        return w.ToArray();
    }

    public static byte[] PartyStatusMessage(byte message)
    {
        var w = new PacketWriter(3);
        w.WriteShort(SendPartyOperationOpcode);
        w.WriteByte(message);
        return w.ToArray();
    }

    public static byte[] PartyStatusMessage(byte message, string characterName)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPartyOperationOpcode);
        w.WriteByte(message);
        w.WriteMapleString(characterName);
        return w.ToArray();
    }

    public static byte[] UpdateParty(int recipientChannelIndex, PartyState party, PartyUpdateKind update, PartyMember target)
    {
        var w = new PacketWriter();
        w.WriteShort(SendPartyOperationOpcode);

        switch (update)
        {
            case PartyUpdateKind.Disband:
            case PartyUpdateKind.Expel:
            case PartyUpdateKind.Leave:
                w.WriteByte(0x0C);
                w.WriteInt(party.Id);
                w.WriteInt(target.CharacterId);
                w.WriteByte(update == PartyUpdateKind.Disband ? 0 : 1);
                if (update == PartyUpdateKind.Disband)
                {
                    w.WriteInt(target.CharacterId);
                }
                else
                {
                    w.WriteByte(update == PartyUpdateKind.Expel ? 1 : 0);
                    w.WriteMapleString(target.Name);
                    AddPartyStatus(w, recipientChannelIndex, party, leaving: update == PartyUpdateKind.Leave);
                }
                break;

            case PartyUpdateKind.Join:
                w.WriteByte(0x0F);
                w.WriteInt(party.Id);
                w.WriteMapleString(target.Name);
                AddPartyStatus(w, recipientChannelIndex, party, leaving: false);
                break;

            case PartyUpdateKind.SilentUpdate:
            case PartyUpdateKind.LogOnOff:
                w.WriteByte(0x07);
                w.WriteInt(party.Id);
                AddPartyStatus(w, recipientChannelIndex, party, leaving: update == PartyUpdateKind.LogOnOff);
                break;

            case PartyUpdateKind.ChangeLeader:
            case PartyUpdateKind.ChangeLeaderDisconnect:
                w.WriteByte(0x1B);
                w.WriteInt(target.CharacterId);
                w.WriteByte(update == PartyUpdateKind.ChangeLeaderDisconnect ? 1 : 0);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(update), update, null);
        }

        return w.ToArray();
    }

    public static byte[] UpdatePartyMemberHp(int characterId, int currentHp, int maxHp)
    {
        var w = new PacketWriter(14);
        w.WriteShort(SendUpdatePartyMemberHpOpcode);
        w.WriteInt(characterId);
        w.WriteInt(currentHp);
        w.WriteInt(maxHp);
        return w.ToArray();
    }

    private static void AddPartyStatus(PacketWriter w, int recipientChannelIndex, PartyState party, bool leaving)
    {
        var members = PartySlots(party);

        foreach (var member in members)
        {
            w.WriteInt(member.CharacterId);
        }

        foreach (var member in members)
        {
            w.WriteFixedAsciiString(member.Name, 15);
        }

        foreach (var member in members)
        {
            w.WriteInt(member.JobId);
        }

        foreach (var member in members)
        {
            w.WriteInt(member.Level);
        }

        foreach (var member in members)
        {
            w.WriteInt(member.IsOnline ? member.ChannelIndex : -2);
        }

        w.WriteInt(party.LeaderId);

        foreach (var member in members)
        {
            w.WriteInt(member.ChannelIndex == recipientChannelIndex ? member.MapId : 0);
        }

        foreach (var member in members)
        {
            if (member.ChannelIndex == recipientChannelIndex && !leaving)
            {
                w.WriteInt(member.DoorTownId);
                w.WriteInt(member.DoorTargetMapId);
                w.WriteInt(member.DoorX);
                w.WriteInt(member.DoorY);
            }
            else
            {
                w.WriteInt(leaving ? PartyMember.NoDoorMapId : 0);
                w.WriteInt(leaving ? PartyMember.NoDoorMapId : 0);
                w.WriteInt(leaving ? -1 : 0);
                w.WriteInt(leaving ? -1 : 0);
            }
        }
    }

    private static IReadOnlyList<PartyMember> PartySlots(PartyState party)
    {
        var slots = new List<PartyMember>(Party.MaxMembers);
        slots.AddRange(party.Members.Take(Party.MaxMembers));

        while (slots.Count < Party.MaxMembers)
        {
            slots.Add(EmptyMember());
        }

        return slots;
    }

    private static PartyMember EmptyMember() =>
        new(0, string.Empty, 0, 0, 0, -2, IsOnline: false, DoorTownId: 0, DoorTargetMapId: 0);
}
