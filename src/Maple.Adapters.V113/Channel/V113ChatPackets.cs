using Maple.Application.Chats;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal enum V113WhisperClientMode : byte
{
    Find = 5,
    Whisper = 6,
    BuddyFind = 68,
}

internal sealed record V113GroupChatRequest(
    GroupChatKind Kind,
    IReadOnlyList<int> RecipientCharacterIds,
    string Text);

internal static class V113ChatPackets
{
    public const short RecvGroupMessageOpcode = 0x70;
    public const short RecvWhisperOpcode = 0x71;
    public const short SendMultiChatOpcode = unchecked((short)0x84);
    public const short SendWhisperOpcode = unchecked((short)0x85);

    public static V113GroupChatRequest? ReadGroupChat(PacketReader reader)
    {
        var kind = (GroupChatKind)reader.ReadByte();
        var count = unchecked((sbyte)reader.ReadByte());
        if (count <= 0)
        {
            return null;
        }

        var recipients = new int[count];
        for (var i = 0; i < recipients.Length; i++)
        {
            recipients[i] = reader.ReadInt();
        }

        var text = reader.ReadMapleString();
        return new V113GroupChatRequest(kind, recipients, text);
    }

    public static V113WhisperClientMode ReadWhisperMode(PacketReader reader) =>
        (V113WhisperClientMode)reader.ReadByte();

    public static byte[] MultiChat(string senderName, string text, GroupChatKind kind)
    {
        var w = new PacketWriter();
        w.WriteShort(SendMultiChatOpcode);
        w.WriteByte((byte)kind);
        w.WriteMapleString(senderName);
        w.WriteMapleString(text);
        return w.ToArray();
    }

    public static byte[] Whisper(string senderName, int channel, string text)
    {
        var w = new PacketWriter();
        w.WriteShort(SendWhisperOpcode);
        w.WriteByte(0x12);
        w.WriteMapleString(senderName);
        w.WriteShort(channel - 1);
        w.WriteMapleString(text);
        return w.ToArray();
    }

    public static byte[] WhisperReply(string targetName, byte reply)
    {
        var w = new PacketWriter();
        w.WriteShort(SendWhisperOpcode);
        w.WriteByte(0x0A);
        w.WriteMapleString(targetName);
        w.WriteByte(reply);
        return w.ToArray();
    }

    public static byte[] FindReplyWithMap(string targetName, int mapId, bool buddy)
    {
        var w = new PacketWriter();
        w.WriteShort(SendWhisperOpcode);
        w.WriteByte(buddy ? 72 : 9);
        w.WriteMapleString(targetName);
        w.WriteByte(1);
        w.WriteInt(mapId);
        w.WriteZeroBytes(8);
        return w.ToArray();
    }

    public static byte[] FindReplyWithChannel(string targetName, int channel, bool buddy)
    {
        var w = new PacketWriter();
        w.WriteShort(SendWhisperOpcode);
        w.WriteByte(buddy ? 72 : 9);
        w.WriteMapleString(targetName);
        w.WriteByte(3);
        w.WriteInt(channel - 1);
        return w.ToArray();
    }
}
