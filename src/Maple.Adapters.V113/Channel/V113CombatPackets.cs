using Maple.Application.Combat;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

internal sealed record V113AttackTarget(int ObjectId, IReadOnlyList<int> DamageLines);

internal sealed record V113CloseRangeAttack(
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
    IReadOnlyList<V113AttackTarget> Damage)
{
    public CombatAttack ToCombatAttack()
        => new(Damage.Select(static d => new CombatAttackTarget(d.ObjectId, d.DamageLines)).ToList());
}

/// <summary>v113 戰鬥/怪物封包。對照 Java DamageParse.parseDmgM 與 MobPacket。</summary>
internal static class V113CombatPackets
{
    public const short CloseRangeAttackRecvOp = 0x25;
    public const short CloseRangeAttackSendOp = unchecked((short)0xB2);
    public const short SpawnMonsterOp = unchecked((short)0xE5);
    public const short KillMonsterOp = unchecked((short)0xE6);
    public const short SpawnMonsterControlOp = unchecked((short)0xE7);
    public const short MoveMonsterOp = unchecked((short)0xE8);
    public const short MoveMonsterResponseOp = unchecked((short)0xE9);
    public const short DamageMonsterOp = unchecked((short)0xEF);

    private const int CorkscrewBlow = 5101004;
    private const int GunslingerGrenade = 5201002;
    private const int NightWalkerPoisonBomb = 14111006;
    private const int ThunderBreakerCorkscrew = 15101003;

    public static V113CloseRangeAttack ParseCloseRangeAttack(PacketReader reader)
    {
        reader.Skip(1);
        reader.Skip(8);
        var encoded = reader.ReadByte();
        var targets = (byte)((encoded >>> 4) & 0x0F);
        var hits = (byte)(encoded & 0x0F);
        reader.Skip(8);
        var skill = reader.ReadInt();
        reader.Skip(12);

        var charge = RequiresCharge(skill) ? reader.ReadInt() : 0;
        var unknown = reader.ReadByte();
        var display = reader.ReadByte();
        var animation = reader.ReadByte();
        reader.Skip(1);
        var speed = reader.ReadByte();
        var tick = reader.ReadInt();

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

        if (reader.Remaining >= 4)
        {
            reader.Skip(4); // attack.position (x/y); Core position is updated by MOVE_PLAYER.
        }
        if (reader.Remaining == 4)
        {
            reader.Skip(4); // optional skillposition
        }

        return new V113CloseRangeAttack(encoded, targets, hits, skill, charge, unknown, display, animation, speed, tick, damage);
    }

    /// <summary>SPAWN_MONSTER (0xE5)。預設 spawnType=-2，對齊 Java map.spawnMonster。</summary>
    public static byte[] SpawnMonster(Mob mob, sbyte spawnType = -2, int effect = 0, int link = 0)
    {
        var w = new PacketWriter(64);
        w.WriteShort(SpawnMonsterOp);
        WriteMonsterBody(w, mob, spawnType, effect, link);
        return w.ToArray();
    }

    /// <summary>SPAWN_MONSTER_CONTROL (0xE7)。controlFlag: 1=控制, 2=控制且仇恨, 0=停止控制。</summary>
    public static byte[] SpawnMonsterControl(Mob mob, bool newSpawn = true, bool aggro = false)
    {
        var w = new PacketWriter(64);
        w.WriteShort(SpawnMonsterControlOp);
        w.WriteByte(aggro ? 2 : 1);
        WriteMonsterBody(w, mob, newSpawn ? (sbyte)-2 : (sbyte)-1, effect: 0, link: 0);
        return w.ToArray();
    }

    public static byte[] StopControllingMonster(int objectId)
    {
        var w = new PacketWriter(8);
        w.WriteShort(SpawnMonsterControlOp);
        w.WriteByte(0);
        w.WriteInt(objectId);
        return w.ToArray();
    }

    public static byte[] DamageMonster(int objectId, long damage)
    {
        var displayedDamage = damage <= 0 ? 0 : damage > int.MaxValue ? int.MaxValue : (int)damage;
        var w = new PacketWriter(12);
        w.WriteShort(DamageMonsterOp);
        w.WriteInt(objectId);
        w.WriteByte(0);
        w.WriteInt(displayedDamage);
        return w.ToArray();
    }

    public static byte[] KillMonster(int objectId, byte animation = 1)
    {
        var w = new PacketWriter(8);
        w.WriteShort(KillMonsterOp);
        w.WriteInt(objectId);
        w.WriteByte(animation);
        return w.ToArray();
    }

    public static byte[] CloseRangeAttackBroadcast(
        int characterId,
        V113CloseRangeAttack attack,
        byte characterLevel,
        byte skillLevel = 0,
        byte mastery = 0)
    {
        var w = new PacketWriter(64);
        w.WriteShort(CloseRangeAttackSendOp);
        w.WriteInt(characterId);
        w.WriteByte(attack.EncodedTargetsAndHits);
        w.WriteByte(characterLevel);
        if (attack.SkillId > 0)
        {
            w.WriteByte(skillLevel);
            w.WriteInt(attack.SkillId);
        }
        else
        {
            w.WriteByte(0);
        }

        w.WriteByte(attack.Unknown);
        w.WriteByte(attack.Display);
        w.WriteByte(attack.Animation);
        w.WriteByte(attack.Speed);
        w.WriteByte(mastery);
        w.WriteInt(0);

        foreach (var target in attack.Damage)
        {
            w.WriteInt(target.ObjectId);
            w.WriteByte(0x07);
            foreach (var line in target.DamageLines)
            {
                w.WriteInt(line);
            }
        }

        return w.ToArray();
    }

    private static void WriteMonsterBody(PacketWriter w, Mob mob, sbyte spawnType, int effect, int link)
    {
        w.WriteInt(mob.ObjectId);
        w.WriteByte(1); // 1 = Control normal, 5 = Control none
        w.WriteInt(mob.Definition.MonsterId);
        WriteEmptyMonsterStatus(w);
        w.WriteShort(mob.Position.X);
        w.WriteShort(mob.Position.Y);
        w.WriteByte(mob.Position.Stance);
        w.WriteShort(mob.Position.Foothold);
        w.WriteShort(mob.OriginFoothold);

        if (effect != 0 || link != 0)
        {
            w.WriteByte(effect != 0 ? effect : -3);
            w.WriteInt(link);
        }
        else
        {
            if (spawnType == 0)
            {
                w.WriteInt(effect);
            }
            w.WriteByte(spawnType);
        }

        w.WriteByte(mob.CarnivalTeam);
        w.WriteInt(0);
    }

    private static void WriteEmptyMonsterStatus(PacketWriter w)
    {
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteInt(0x08000000); // MonsterStatus.EMPTY
        w.WriteInt(0);          // EMPTY payload count
    }

    private static bool RequiresCharge(int skill)
        => skill is CorkscrewBlow or ThunderBreakerCorkscrew or GunslingerGrenade or NightWalkerPoisonBomb;
}
