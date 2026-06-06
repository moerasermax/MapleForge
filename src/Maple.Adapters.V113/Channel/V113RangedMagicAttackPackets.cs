using Maple.Application.Combat;
using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113AttackPosition(short X, short Y);

internal sealed record V113RangedAttack(
    byte EncodedTargetsAndHits,
    byte Targets,
    byte Hits,
    int SkillId,
    int Charge,
    byte Unknown,
    byte Display,
    byte Animation,
    byte Speed,
    int LastAttackTickCount,
    short ProjectileSlot,
    short CashProjectileSlot,
    byte AreaOfEffect,
    V113AttackPosition Position,
    IReadOnlyList<V113AttackTarget> Damage)
{
    public CombatAttack ToCombatAttack()
        => new(Damage.Select(static d => new CombatAttackTarget(d.ObjectId, d.DamageLines)).ToList());

    public CombatRangedAttack ToCombatRangedAttack()
        => new(ToCombatAttack(), SkillId, ProjectileSlot, CashProjectileSlot, AreaOfEffect);
}

internal sealed record V113MagicAttack(
    byte EncodedTargetsAndHits,
    byte Targets,
    byte Hits,
    int SkillId,
    int Charge,
    byte Unknown,
    byte Display,
    byte Animation,
    byte Speed,
    int LastAttackTickCount,
    V113AttackPosition Position,
    IReadOnlyList<V113AttackTarget> Damage)
{
    public CombatAttack ToCombatAttack()
        => new(Damage.Select(static d => new CombatAttackTarget(d.ObjectId, d.DamageLines)).ToList());
}

/// <summary>v113 ranged/magic attack packets, aligned with Java DamageParse.parseDmgR/parseDmgMa.</summary>
internal static class V113RangedMagicAttackPackets
{
    public const short RangedAttackRecvOp = 0x26;
    public const short MagicAttackRecvOp = 0x27;
    public const short RangedAttackSendOp = unchecked((short)0xB3);
    public const short MagicAttackSendOp = unchecked((short)0xB4);

    private const int Hurricane = 3121004;
    private const int Pierce = 3221001;
    private const int Rapidfire = 5221004;
    private const int WindArcherHurricane = 13111002;

    private const int FirePoisonBigBang = 2121001;
    private const int IceLightningBigBang = 2221001;
    private const int BishopBigBang = 2321001;
    private const int EvanBreath = 22121000;
    private const int EvanFlameWheel = 22151001;

    public static V113RangedAttack ParseRangedAttack(PacketReader reader)
    {
        var common = ReadCommonAttackHeader(reader);
        if (RequiresRangedExtraBytes(common.SkillId))
        {
            reader.Skip(4);
        }

        var unknown = reader.ReadByte();
        var display = reader.ReadByte();
        var animation = reader.ReadByte();
        reader.Skip(1);
        var speed = reader.ReadByte();
        var tick = reader.ReadInt();
        var projectileSlot = reader.ReadShort();
        var cashProjectileSlot = reader.ReadShort();
        var areaOfEffect = reader.ReadByte();
        var damage = ReadTargets(reader, common.Targets, common.Hits);

        reader.Skip(4);
        var position = ReadPosition(reader);

        return new V113RangedAttack(
            common.Encoded,
            common.Targets,
            common.Hits,
            common.SkillId,
            -1,
            unknown,
            display,
            animation,
            speed,
            tick,
            projectileSlot,
            cashProjectileSlot,
            areaOfEffect,
            position,
            damage);
    }

    public static V113MagicAttack ParseMagicAttack(PacketReader reader)
    {
        var common = ReadCommonAttackHeader(reader);
        var charge = RequiresMagicCharge(common.SkillId) ? reader.ReadInt() : -1;

        reader.Skip(1);
        var unknown = (byte)0;
        var display = reader.ReadByte();
        var animation = reader.ReadByte();
        reader.Skip(1);
        var speed = reader.ReadByte();
        var tick = reader.ReadInt();
        var damage = ReadTargets(reader, common.Targets, common.Hits);
        var position = ReadPosition(reader);

        return new V113MagicAttack(
            common.Encoded,
            common.Targets,
            common.Hits,
            common.SkillId,
            charge,
            unknown,
            display,
            animation,
            speed,
            tick,
            position,
            damage);
    }

    public static byte[] RangedAttackBroadcast(
        int characterId,
        V113RangedAttack attack,
        byte characterLevel,
        int projectileItemId,
        byte skillLevel = 0,
        byte mastery = 0)
    {
        var w = new PacketWriter(64);
        w.WriteShort(RangedAttackSendOp);
        w.WriteInt(characterId);
        w.WriteByte(attack.EncodedTargetsAndHits);
        w.WriteByte(characterLevel);
        WriteSkill(w, attack.SkillId, skillLevel);
        w.WriteByte(attack.Unknown);
        w.WriteByte(attack.Display);
        w.WriteByte(attack.Animation);
        w.WriteByte(attack.Speed);
        w.WriteByte(mastery);
        w.WriteInt(projectileItemId);
        WriteTargetDamage(w, attack.Damage, targetMarker: 0x07);
        WritePosition(w, attack.Position);
        return w.ToArray();
    }

    public static byte[] MagicAttackBroadcast(
        int characterId,
        V113MagicAttack attack,
        byte characterLevel,
        byte skillLevel = 0)
    {
        var w = new PacketWriter(64);
        w.WriteShort(MagicAttackSendOp);
        w.WriteInt(characterId);
        w.WriteByte(attack.EncodedTargetsAndHits);
        w.WriteByte(characterLevel);
        w.WriteByte(skillLevel);
        w.WriteInt(attack.SkillId);
        w.WriteByte(attack.Unknown);
        w.WriteByte(attack.Display);
        w.WriteByte(attack.Animation);
        w.WriteByte(attack.Speed);
        w.WriteByte(0);
        w.WriteInt(0);
        WriteTargetDamage(w, attack.Damage, targetMarker: 0xFF);
        if (attack.Charge > 0)
        {
            w.WriteInt(attack.Charge);
        }

        return w.ToArray();
    }

    public static byte[] ModifyInventoryQuantity(InventoryQuantityMutation mutation)
    {
        var w = new PacketWriter(12);
        w.WriteShort(V113ChannelSendOp.ModifyInventoryItem);
        w.WriteByte(0);
        w.WriteByte(1);
        w.WriteByte(mutation.Removed ? 3 : 1);
        w.WriteByte((byte)mutation.Type);
        w.WriteShort(mutation.Slot);
        if (!mutation.Removed)
        {
            w.WriteShort(mutation.NewQuantity);
        }

        return w.ToArray();
    }

    private static (byte Encoded, byte Targets, byte Hits, int SkillId) ReadCommonAttackHeader(PacketReader reader)
    {
        reader.Skip(1);
        reader.Skip(8);
        var encoded = reader.ReadByte();
        var targets = (byte)((encoded >>> 4) & 0x0F);
        var hits = (byte)(encoded & 0x0F);
        reader.Skip(8);
        var skillId = reader.ReadInt();
        reader.Skip(12);
        return (encoded, targets, hits, skillId);
    }

    private static IReadOnlyList<V113AttackTarget> ReadTargets(PacketReader reader, byte targets, byte hits)
    {
        var damage = new List<V113AttackTarget>(targets);
        for (var i = 0; i < targets; i++)
        {
            var oid = reader.ReadInt();
            reader.Skip(14);

            var lines = new List<int>(hits);
            for (var j = 0; j < hits; j++)
            {
                lines.Add(reader.ReadInt());
            }

            reader.Skip(4);
            damage.Add(new V113AttackTarget(oid, lines));
        }

        return damage;
    }

    private static V113AttackPosition ReadPosition(PacketReader reader)
        => new(reader.ReadShort(), reader.ReadShort());

    private static void WritePosition(PacketWriter w, V113AttackPosition position)
    {
        w.WriteShort(position.X);
        w.WriteShort(position.Y);
    }

    private static void WriteSkill(PacketWriter w, int skillId, byte skillLevel)
    {
        if (skillId > 0)
        {
            w.WriteByte(skillLevel);
            w.WriteInt(skillId);
        }
        else
        {
            w.WriteByte(0);
        }
    }

    private static void WriteTargetDamage(PacketWriter w, IReadOnlyList<V113AttackTarget> damage, int targetMarker)
    {
        foreach (var target in damage)
        {
            w.WriteInt(target.ObjectId);
            w.WriteByte(targetMarker);
            foreach (var line in target.DamageLines)
            {
                w.WriteInt(line);
            }
        }
    }

    private static bool RequiresRangedExtraBytes(int skillId)
        => skillId is Hurricane or Pierce or Rapidfire or WindArcherHurricane;

    private static bool RequiresMagicCharge(int skillId)
        => skillId is FirePoisonBigBang or IceLightningBigBang or BishopBigBang or EvanBreath or EvanFlameWheel;
}
