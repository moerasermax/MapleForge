using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>v113 CHANGE_CHANNEL parser/serializer.</summary>
internal static class V113ChannelChangePackets
{
    public static byte ParseChangeChannel(PacketReader reader) => reader.ReadByte();

    public static byte[] ChangeChannel(byte[] ip, short port)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ChangeChannel);
        w.WriteByte(1);
        w.WriteBytes(ip);
        w.WriteShort(port);
        return w.ToArray();
    }
}
