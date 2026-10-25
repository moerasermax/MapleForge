using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>P131：對照 Java <c>MaplePacketCreator.spawnMist</c>／<c>removeMist</c>（send.properties SPAWN_MIST=0x10C、REMOVE_MIST=0x10D；unverified）。</summary>
internal static class V113MistPackets
{
    public const short SpawnMistOp = 0x10C;
    public const short RemoveMistOp = 0x10D;

    public static byte[] SpawnMist(Mist mist)
    {
        var w = new PacketWriter(43);
        w.WriteShort(SpawnMistOp);
        w.WriteInt(mist.ObjectId);
        w.WriteInt(mist.IsMobMist ? 0 : mist.PoisonMode != 0 ? 1 : 2);
        w.WriteInt(mist.OwnerId);
        w.WriteInt(mist.SkillId);
        w.WriteByte((byte)mist.SkillLevel);
        w.WriteShort(mist.SkillDelay);
        // Java writeRect：x, y, x + width, y + height
        w.WriteInt(mist.Box.X);
        w.WriteInt(mist.Box.Y);
        w.WriteInt(mist.Box.X + mist.Box.Width);
        w.WriteInt(mist.Box.Y + mist.Box.Height);
        w.WriteInt(0);
        return w.ToArray();
    }

    public static byte[] RemoveMist(int objectId)
    {
        var w = new PacketWriter(6);
        w.WriteShort(RemoveMistOp);
        w.WriteInt(objectId);
        return w.ToArray();
    }
}
