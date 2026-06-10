using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal static class V113MonsterBookPackets
{
    public static int ParseChangeCover(PacketReader reader) => reader.ReadInt();

    public static byte[] ChangeCover(int cardId)
    {
        var w = new PacketWriter(6);
        w.WriteShort(V113ChannelSendOp.MonsterBookChangeCover);
        w.WriteInt(cardId);
        return w.ToArray();
    }

    public static bool IsMonsterCardOrClear(int itemId) => itemId == 0 || itemId / 10000 == 238;
}
