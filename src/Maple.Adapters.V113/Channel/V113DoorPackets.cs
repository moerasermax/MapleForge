using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113UseDoorRequest(int OwnerId, bool Backwarp);

internal static class V113DoorPackets
{
    public const short RecvUseDoor = 0x7D;
    public const short SendSpawnPortal = 0x3C;
    public const short SendSpawnDoor = 0x10E;
    public const short SendRemoveDoor = 0x10F;
    private const int NoDoorMapId = 999999999;

    public static V113UseDoorRequest ParseUseDoor(PacketReader reader)
    {
        var ownerId = reader.ReadInt();
        var backwarp = reader.ReadByte() == 0;
        return new V113UseDoorRequest(ownerId, backwarp);
    }

    public static byte[] SpawnDoor(int ownerId, Position position, bool town)
    {
        var w = new PacketWriter(11);
        w.WriteShort(SendSpawnDoor);
        w.WriteByte(town ? 1 : 0);
        w.WriteInt(ownerId);
        WritePosition(w, position);
        return w.ToArray();
    }

    public static byte[] RemoveDoor(int ownerId)
    {
        var w = new PacketWriter(7);
        w.WriteShort(SendRemoveDoor);
        w.WriteByte(1);
        w.WriteInt(ownerId);
        return w.ToArray();
    }

    public static byte[] RemoveTownPortal()
    {
        var w = new PacketWriter(10);
        w.WriteShort(SendSpawnPortal);
        w.WriteInt(NoDoorMapId);
        w.WriteInt(NoDoorMapId);
        return w.ToArray();
    }

    public static byte[] SpawnPortal(int townMapId, int targetMapId, Position? targetPosition)
    {
        var w = new PacketWriter(targetPosition.HasValue ? 14 : 10);
        w.WriteShort(SendSpawnPortal);
        w.WriteInt(townMapId);
        w.WriteInt(targetMapId);
        if (targetPosition.HasValue)
        {
            WritePosition(w, targetPosition.Value);
        }

        return w.ToArray();
    }

    private static void WritePosition(PacketWriter writer, Position position)
    {
        writer.WriteShort(position.X);
        writer.WriteShort(position.Y);
    }
}
