using Maple.Core.Characters;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113TrockAddMapRequest(byte Operation, byte Vip, int MapId)
{
    public bool IsDelete => Operation == 0;
    public bool IsAdd => Operation == 1;
    public bool IsVip => Vip == 1;
}

internal static class V113TrockPackets
{
    public static V113TrockAddMapRequest ParseAddMap(PacketReader reader)
    {
        var operation = reader.ReadByte();
        var vip = reader.ReadByte();
        var mapId = operation == 0 ? reader.ReadInt() : 0;
        return new V113TrockAddMapRequest(operation, vip, mapId);
    }

    public static byte[] MapTransferResult(Character character, byte vip, bool delete)
    {
        var maps = vip == 1
            ? character.GetVipRockSlots()
            : character.GetRegularRockSlots();
        var expectedCount = vip == 1 ? 10 : 5;

        var w = new PacketWriter(2 + 1 + 1 + (expectedCount * 4));
        w.WriteShort(V113ChannelSendOp.MapTransferResult);
        w.WriteByte(delete ? (byte)2 : (byte)3);
        w.WriteByte(vip);
        for (var i = 0; i < expectedCount; i++)
        {
            w.WriteInt(i < maps.Count ? maps[i] : Character.EmptyRockMapId);
        }

        return w.ToArray();
    }

    /// <summary>MAP_TRANSFER_RESULT MVP response for USE_TELE_ROCK. Java has no matching helper; mode 0/1 is MapleForge candidate.</summary>
    public static byte[] MapTransferUseResult(bool success)
    {
        var w = new PacketWriter(3);
        w.WriteShort(V113ChannelSendOp.MapTransferResult);
        w.WriteByte(success ? (byte)0 : (byte)1);
        return w.ToArray();
    }
}
