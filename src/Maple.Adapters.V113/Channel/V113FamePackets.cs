using Maple.Application.Fame;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113GiveFameRequest(int TargetCharacterId, byte Mode);

internal static class V113FamePackets
{
    public const short RecvGiveFame = 0x59;
    public const short SendFameResponse = 0x24;

    public static V113GiveFameRequest ParseGiveFame(PacketReader reader)
        => new(reader.ReadInt(), reader.ReadByte());

    public static byte[] GiveFameResponse(byte mode, string targetName, short newFame)
    {
        var w = new PacketWriter(16 + targetName.Length);
        w.WriteShort(SendFameResponse);
        w.WriteByte(0);
        w.WriteMapleString(targetName);
        w.WriteByte(mode);
        w.WriteShort(newFame);
        w.WriteShort(0);
        return w.ToArray();
    }

    public static byte[] ReceiveFame(byte mode, string giverName)
    {
        var w = new PacketWriter(8 + giverName.Length);
        w.WriteShort(SendFameResponse);
        w.WriteByte(5);
        w.WriteMapleString(giverName);
        w.WriteByte(mode);
        return w.ToArray();
    }

    public static byte[] GiveFameError(FameResultStatus status)
    {
        var w = new PacketWriter(3);
        w.WriteShort(SendFameResponse);
        w.WriteByte(ToClientStatus(status));
        return w.ToArray();
    }

    private static byte ToClientStatus(FameResultStatus status)
        => status switch
        {
            FameResultStatus.TargetNotFound => 1,
            FameResultStatus.UnderLevel => 2,
            FameResultStatus.AlreadyToday => 3,
            FameResultStatus.AlreadyThisMonth => 4,
            _ => 6,
        };
}
