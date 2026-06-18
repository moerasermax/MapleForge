using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal enum V113MessengerClientMode : byte
{
    Open = 0x00,
    Exit = 0x02,
    Invite = 0x03,
    Decline = 0x05,
    Message = 0x06,
}

internal static class V113MessengerPackets
{
    public const short SendMessengerOpcode = V113ChannelSendOp.SendMessenger;

    public static V113MessengerClientMode ReadMode(PacketReader reader) =>
        (V113MessengerClientMode)reader.ReadByte();

    public static byte[] MessengerInvite(string from, int messengerId)
    {
        var w = new PacketWriter();
        w.WriteShort(SendMessengerOpcode);
        w.WriteByte(0x03);
        w.WriteMapleString(from);
        w.WriteByte(0);
        w.WriteInt(messengerId);
        w.WriteByte(0);
        return w.ToArray();
    }

    public static byte[] AddMessengerPlayer(string from, int position, int channel)
    {
        var w = new PacketWriter();
        w.WriteShort(SendMessengerOpcode);
        w.WriteByte(0x00);
        w.WriteByte(position);
        WriteEmptyCharLook(w);
        w.WriteMapleString(from);
        w.WriteShort(channel);
        return w.ToArray();
    }

    public static byte[] AddMessengerPlayer(string from, Character character, int position, int channel)
    {
        var w = new PacketWriter();
        w.WriteShort(SendMessengerOpcode);
        w.WriteByte(0x00);
        w.WriteByte(position);
        WriteCharLook(w, character);
        w.WriteMapleString(from);
        w.WriteShort(channel);
        return w.ToArray();
    }

    public static byte[] JoinMessenger(int position)
    {
        var w = new PacketWriter();
        w.WriteShort(SendMessengerOpcode);
        w.WriteByte(0x01);
        w.WriteByte(position);
        return w.ToArray();
    }

    public static byte[] RemoveMessengerPlayer(int position)
    {
        var w = new PacketWriter();
        w.WriteShort(SendMessengerOpcode);
        w.WriteByte(0x02);
        w.WriteByte(position);
        return w.ToArray();
    }

    public static byte[] MessengerChat(string text)
    {
        var w = new PacketWriter();
        w.WriteShort(SendMessengerOpcode);
        w.WriteByte(0x06);
        w.WriteMapleString(text);
        return w.ToArray();
    }

    public static byte[] MessengerNote(string text, int mode, int mode2)
    {
        var w = new PacketWriter();
        w.WriteShort(SendMessengerOpcode);
        w.WriteByte(mode);
        w.WriteMapleString(text);
        w.WriteByte(mode2);
        return w.ToArray();
    }

    private static void WriteCharLook(PacketWriter w, Character chr)
    {
        w.WriteByte(chr.Gender);
        w.WriteByte(chr.SkinColor);
        w.WriteInt(chr.Face);
        w.WriteByte(0);
        w.WriteInt(chr.Hair);

        foreach (var equip in chr.Equips.Where(static e => e.Position < 0 && e.Position > -100))
        {
            w.WriteByte((byte)(-equip.Position));
            w.WriteInt(equip.ItemId);
        }

        w.WriteByte(0xFF);
        w.WriteByte(0xFF);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteLong(0);
    }

    private static void WriteEmptyCharLook(PacketWriter w)
    {
        w.WriteByte(0);
        w.WriteByte(0);
        w.WriteInt(0);
        w.WriteByte(0);
        w.WriteInt(0);
        w.WriteByte(0xFF);
        w.WriteByte(0xFF);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteLong(0);
    }
}
