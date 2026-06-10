using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal static class V113ChalkboardPackets
{
    public static byte[] Chalkboard(int characterId, string? message)
    {
        var hasMessage = !string.IsNullOrEmpty(message);
        var w = new PacketWriter(hasMessage ? message!.Length + 10 : 7);
        w.WriteShort(V113ChannelSendOp.Chalkboard);
        w.WriteInt(characterId);
        w.WriteByte(hasMessage ? (byte)1 : (byte)0);
        if (hasMessage)
        {
            w.WriteMapleString(message!);
        }

        return w.ToArray();
    }
}
