using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal static class V113CashItemPackets
{
    public static byte[] PlayCashSong(int itemId, string characterName)
    {
        var w = new PacketWriter(characterName.Length + 8);
        w.WriteShort(V113ChannelSendOp.CashSong);
        w.WriteInt(itemId);
        w.WriteMapleString(characterName);
        return w.ToArray();
    }
}
