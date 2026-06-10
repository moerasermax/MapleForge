using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113TakeDamageRequest(
    int Tick,
    sbyte Type,
    int Damage,
    int MonsterIdFrom,
    int ObjectId,
    byte Direction)
{
    public bool IsMapDamage => Type is -2 or -3 or -4;
}

internal static class V113PlayerDamagePackets
{
    public static V113TakeDamageRequest ParseTakeDamage(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var type = unchecked((sbyte)reader.ReadByte());
        reader.Skip(1); // element
        var damage = reader.ReadInt();

        if (type is -2 or -3 or -4)
        {
            return new V113TakeDamageRequest(tick, type, damage, 0, 0, 0);
        }

        var monsterIdFrom = reader.ReadInt();
        var objectId = reader.ReadInt();
        var direction = reader.ReadByte();
        return new V113TakeDamageRequest(tick, type, damage, monsterIdFrom, objectId, direction);
    }

    public static byte[] DamagePlayer(
        int characterId,
        sbyte type,
        int damage,
        int monsterIdFrom,
        byte direction,
        int fake = 0)
    {
        var w = new PacketWriter(fake > 0 ? 29 : 25);
        w.WriteShort(V113ChannelSendOp.DamagePlayer);
        w.WriteInt(characterId);
        w.WriteByte(unchecked((byte)type));
        w.WriteInt(damage);
        w.WriteInt(monsterIdFrom);
        w.WriteByte(direction);
        w.WriteShort(0); // reflect
        w.WriteInt(damage);
        if (fake > 0)
        {
            w.WriteInt(fake);
        }

        return w.ToArray();
    }
}
