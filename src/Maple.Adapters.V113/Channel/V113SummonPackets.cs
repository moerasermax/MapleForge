using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal readonly record struct V113MoveSummonRequest(
    int ObjectId,
    short StartX,
    short StartY,
    byte[] RawMovement);

internal readonly record struct V113DamageSummonRequest(
    byte Unknown,
    int Damage,
    int MonsterIdFrom);

internal readonly record struct V113SubSummonRequest(
    int ObjectId,
    int? SkillId);

internal sealed record V113SummonAttackTarget(int ObjectId, int Damage);

internal sealed record V113SummonAttackRequest(
    int SummonObjectId,
    int Tick,
    byte Animation,
    IReadOnlyList<V113SummonAttackTarget> Targets);

/// <summary>
/// v113 召喚獸封包。對照 Java SummonHandler 與 MaplePacketCreator
/// spawnSummon/removeSummon/moveSummon/summonAttack/damageSummon。
/// </summary>
internal static class V113SummonPackets
{
    public const short MoveSummonRecvOp = unchecked((short)0xAC);
    public const short SummonAttackRecvOp = unchecked((short)0xAD);
    public const short DamageSummonRecvOp = unchecked((short)0xAE);
    public const short SubSummonRecvOp = unchecked((short)0xAF);

    public const short SpawnSummonOp = unchecked((short)0xAA);
    public const short RemoveSummonOp = unchecked((short)0xAB);
    public const short SummonAttackOp = unchecked((short)0xAD);
    public const short MoveSummonOp = unchecked((short)0xAE);
    public const short DamageSummonOp = unchecked((short)0xAF);

    public static V113MoveSummonRequest ParseMoveSummon(PacketReader reader)
    {
        var objectId = reader.ReadInt();
        var startX = reader.ReadShort();
        var startY = reader.ReadShort();
        var rawMovement = reader.ReadBytes(reader.Remaining);
        return new V113MoveSummonRequest(objectId, startX, startY, rawMovement);
    }

    public static V113SummonAttackRequest ParseSummonAttack(PacketReader reader)
    {
        var summonObjectId = reader.ReadInt();
        SkipExact(reader, 8);
        var tick = reader.ReadInt();
        SkipExact(reader, 8);
        var animation = reader.ReadByte();
        SkipExact(reader, 8);
        var count = reader.ReadByte();
        SkipExact(reader, 8);

        var targets = new List<V113SummonAttackTarget>(count);
        for (var i = 0; i < count; i++)
        {
            var objectId = reader.ReadInt();
            SkipExact(reader, 18);
            var damage = reader.ReadInt();
            targets.Add(new V113SummonAttackTarget(objectId, damage));
        }

        return new V113SummonAttackRequest(summonObjectId, tick, animation, targets);
    }

    public static V113DamageSummonRequest ParseDamageSummon(PacketReader reader)
        => new(reader.ReadByte(), reader.ReadInt(), reader.ReadInt());

    public static V113SubSummonRequest ParseSubSummon(PacketReader reader)
    {
        var objectId = reader.ReadInt();
        var skillId = reader.Remaining >= 4 ? reader.ReadInt() : (int?)null;
        return new V113SubSummonRequest(objectId, skillId);
    }

    public static byte[] SpawnSummon(Summon summon, byte ownerLevel, bool facingLeft = false)
    {
        var w = new PacketWriter(64);
        w.WriteShort(SpawnSummonOp);
        w.WriteInt(summon.OwnerId);
        w.WriteInt(summon.ObjectId);
        w.WriteInt(summon.SkillId);
        w.WriteByte(Math.Max(0, ownerLevel - 1));
        w.WriteByte(1);
        w.WriteShort(summon.Position.X);
        w.WriteShort(summon.Position.Y);
        w.WriteByte(GetFacingByte(summon, facingLeft));
        w.WriteShort(0);
        w.WriteByte((byte)summon.MovementType);
        w.WriteByte(GetSummonType(summon.SkillId, summon.IsPuppet));
        w.WriteByte(0);
        w.WriteZeroBytes(20);
        return w.ToArray();
    }

    public static byte[] RemoveSummon(Summon summon, bool animated)
    {
        var w = new PacketWriter(12);
        w.WriteShort(RemoveSummonOp);
        w.WriteInt(summon.OwnerId);
        w.WriteInt(summon.ObjectId);
        w.WriteByte(animated ? 4 : 1);
        return w.ToArray();
    }

    public static byte[] MoveSummon(
        int ownerId,
        int objectId,
        short startX,
        short startY,
        ReadOnlySpan<byte> rawMovement)
    {
        var w = new PacketWriter(12 + rawMovement.Length);
        w.WriteShort(MoveSummonOp);
        w.WriteInt(ownerId);
        w.WriteInt(objectId);
        w.WriteShort(startX);
        w.WriteShort(startY);
        w.WriteBytes(rawMovement);
        return w.ToArray();
    }

    public static byte[] MoveSummon(int ownerId, V113MoveSummonRequest request)
        => MoveSummon(ownerId, request.ObjectId, request.StartX, request.StartY, request.RawMovement);

    public static byte[] SummonAttack(
        int ownerId,
        int summonObjectId,
        byte animation,
        IReadOnlyList<V113SummonAttackTarget> targets,
        byte ownerLevel)
    {
        var w = new PacketWriter(16 + (targets.Count * 9));
        w.WriteShort(SummonAttackOp);
        w.WriteInt(ownerId);
        w.WriteInt(summonObjectId);
        w.WriteByte(Math.Max(0, ownerLevel - 1));
        w.WriteByte(animation);
        w.WriteByte(targets.Count);

        foreach (var target in targets)
        {
            w.WriteInt(target.ObjectId);
            w.WriteByte(0x07);
            w.WriteInt(target.Damage);
        }

        return w.ToArray();
    }

    public static byte[] DamageSummon(int ownerId, int skillId, int damage, byte unknown, int monsterIdFrom)
    {
        var w = new PacketWriter(20);
        w.WriteShort(DamageSummonOp);
        w.WriteInt(ownerId);
        w.WriteInt(skillId);
        w.WriteByte(unknown);
        w.WriteInt(damage);
        w.WriteInt(monsterIdFrom);
        w.WriteByte(0);
        return w.ToArray();
    }

    private static byte GetFacingByte(Summon summon, bool facingLeft)
    {
        if (summon.IsPuppet)
        {
            return facingLeft ? (byte)4 : (byte)5;
        }

        return facingLeft ? (byte)5 : (byte)4;
    }

    private static byte GetSummonType(int skillId, bool isPuppet)
        => isPuppet
            ? (byte)0
            : skillId switch
            {
                1321007 => (byte)2,
                35111001 or 35111009 or 35111010 => (byte)3,
                35121009 => (byte)4,
                _ => (byte)1,
            };

    private static void SkipExact(PacketReader reader, int count)
    {
        if (count < 0 || reader.Remaining < count)
        {
            throw new InvalidDataException($"封包不足：需略過 {count} bytes，剩餘 {reader.Remaining}");
        }

        reader.Skip(count);
    }
}
