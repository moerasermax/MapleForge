using Maple.Application.Buddies;
using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal static class V113BuddyPackets
{
    public const short RecvBuddyListModify = 0x7A;
    public const short SendBuddyList = 0x38;

    public static BuddyModifyRequest ParseModify(PacketReader reader)
    {
        var mode = reader.ReadByte();
        return mode switch
        {
            0 => ParseRefresh(reader, skipBytes: 8),
            1 => new BuddyModifyRequest(
                BuddyModifyKind.Add,
                BuddyName: reader.ReadMapleString(),
                Group: reader.ReadMapleString()),
            2 => new BuddyModifyRequest(BuddyModifyKind.Accept, BuddyCharacterId: reader.ReadInt()),
            3 => new BuddyModifyRequest(BuddyModifyKind.Delete, BuddyCharacterId: reader.ReadInt()),
            82 => ParseRefresh(reader, skipBytes: 3),
            _ => new BuddyModifyRequest(BuddyModifyKind.Unknown),
        };
    }

    public static byte[] Message(byte message)
        => new PacketWriter(3)
            .WriteShort(SendBuddyList)
            .WriteByte(message)
            .ToArray();

    public static byte[] UpdateBuddyList(IReadOnlyCollection<BuddyEntry> buddies)
    {
        var w = new PacketWriter(8 + (buddies.Count * 45));
        w.WriteShort(SendBuddyList);
        w.WriteByte(7);
        w.WriteByte(buddies.Count);

        foreach (var buddy in buddies)
        {
            w.WriteInt(buddy.CharacterId);
            w.WriteFixedAsciiString(buddy.Name, 15);
            w.WriteByte(0);
            w.WriteInt(buddy.Channel == -1 || !buddy.Visible ? -1 : buddy.Channel - 1);
            w.WriteFixedAsciiString(buddy.Group.Length > 17 ? "" : buddy.Group, 17);
        }

        for (var i = 0; i < buddies.Count; i++)
        {
            w.WriteInt(0);
        }

        return w.ToArray();
    }

    public static byte[] RequestBuddyListAdd(int characterIdFrom, string nameFrom)
        => new PacketWriter(48)
            .WriteShort(SendBuddyList)
            .WriteByte(9)
            .WriteInt(characterIdFrom)
            .WriteMapleString(nameFrom)
            .WriteInt(characterIdFrom)
            .WriteFixedAsciiString(nameFrom, 15)
            .WriteByte(1)
            .WriteInt(0)
            .WriteFixedAsciiString(BuddyList.DefaultGroup, 17)
            .WriteShort(0)
            .ToArray();

    public static byte[] UpdateBuddyChannel(int characterId, int channelForClient)
        => new PacketWriter(12)
            .WriteShort(SendBuddyList)
            .WriteByte(0x14)
            .WriteInt(characterId)
            .WriteByte(0)
            .WriteInt(channelForClient)
            .ToArray();

    public static byte[] UpdateBuddyCapacity(int capacity)
        => new PacketWriter(4)
            .WriteShort(SendBuddyList)
            .WriteByte(0x15)
            .WriteByte(capacity)
            .ToArray();

    private static BuddyModifyRequest ParseRefresh(PacketReader reader, int skipBytes)
    {
        if (reader.Remaining >= skipBytes)
        {
            reader.Skip(skipBytes);
        }

        return new BuddyModifyRequest(BuddyModifyKind.Refresh);
    }
}
