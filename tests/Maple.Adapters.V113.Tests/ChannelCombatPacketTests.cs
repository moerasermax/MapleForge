using Maple.Adapters.V113.Channel;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelCombatPacketTests
{
    [Fact]
    public void SpawnMonster_MatchesJavaMobPacketLayout()
    {
        var pkt = V113CombatPackets.SpawnMonster(SampleMob());

        byte[] golden =
        {
            0xE5, 0x00,
            0xA1, 0x86, 0x01, 0x00,
            0x01,
            0x04, 0x87, 0x01, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x08,
            0x00, 0x00, 0x00, 0x00,
            0x1E, 0x00,
            0x28, 0x00,
            0x05,
            0x07, 0x00,
            0x07, 0x00,
            0xFE,
            0xFF,
            0x00, 0x00, 0x00, 0x00,
        };
        Assert.Equal(golden, pkt);
    }

    [Fact]
    public void SpawnMonsterControl_PrefixesControlFlag()
    {
        var pkt = V113CombatPackets.SpawnMonsterControl(SampleMob(), newSpawn: true, aggro: true);
        var r = new PacketReader(pkt);

        Assert.Equal(V113CombatPackets.SpawnMonsterControlOp, r.ReadShort());
        Assert.Equal(2, r.ReadByte());
        Assert.Equal(100001, r.ReadInt());
    }

    [Fact]
    public void StopControllingMonster_WritesJavaLayout()
    {
        // 對照 Java MobPacket.stopControllingMonster：SPAWN_MONSTER_CONTROL + byte 0 + int objectId。
        var pkt = V113CombatPackets.StopControllingMonster(100001);
        var r = new PacketReader(pkt);

        Assert.Equal(V113CombatPackets.SpawnMonsterControlOp, r.ReadShort());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(100001, r.ReadInt());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void DamageMonster_AndKillMonster_UsePropertiesOpcodes()
    {
        Assert.Equal(
            new byte[] { 0xEF, 0x00, 0xA1, 0x86, 0x01, 0x00, 0x00, 0x39, 0x30, 0x00, 0x00 },
            V113CombatPackets.DamageMonster(100001, 12345));

        Assert.Equal(
            new byte[] { 0xE6, 0x00, 0xA1, 0x86, 0x01, 0x00, 0x01 },
            V113CombatPackets.KillMonster(100001));
    }

    [Fact]
    public void ParseCloseRangeAttack_ReadsJavaParseDmgMFields()
    {
        var body = BuildCloseRangeAttackBody();

        var attack = V113CombatPackets.ParseCloseRangeAttack(new PacketReader(body, offset: 2));

        Assert.Equal(0x12, attack.EncodedTargetsAndHits);
        Assert.Equal(1, attack.Targets);
        Assert.Equal(2, attack.Hits);
        Assert.Equal(0, attack.SkillId);
        Assert.Equal(0xAA, attack.Unknown);
        Assert.Equal(0xBB, attack.Display);
        Assert.Equal(0xCC, attack.Animation);
        Assert.Equal(0xEE, attack.Speed);
        Assert.Equal(1234, attack.LastAttackTickCount);

        var target = Assert.Single(attack.Damage);
        Assert.Equal(100001, target.ObjectId);
        Assert.Equal(new[] { 10, 15 }, target.DamageLines);
        Assert.Equal(25, attack.ToCombatAttack().Targets[0].TotalDamage);
    }

    [Fact]
    public void CloseRangeAttackBroadcast_MatchesJavaCreatorLayout_ForBasicAttack()
    {
        var attack = V113CombatPackets.ParseCloseRangeAttack(new PacketReader(BuildCloseRangeAttackBody(), offset: 2));

        var pkt = V113CombatPackets.CloseRangeAttackBroadcast(1, attack, characterLevel: 10, mastery: 3);

        byte[] golden =
        {
            0xB2, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x12,
            0x0A,
            0x00,
            0xAA,
            0xBB,
            0xCC,
            0xEE,
            0x03,
            0x00, 0x00, 0x00, 0x00,
            0xA1, 0x86, 0x01, 0x00,
            0x07,
            0x0A, 0x00, 0x00, 0x00,
            0x0F, 0x00, 0x00, 0x00,
        };
        Assert.Equal(golden, pkt);
    }

    private static Mob SampleMob()
    {
        var def = new MapMonster { MonsterId = 100100, X = 30, Y = 40, Fh = 7, Team = -1 };
        var stats = new MobStats(100100, MaxHp: 42, MaxMp: 7, Level: 2, Exp: 12);
        return new Mob(def, stats, objectId: 100001);
    }

    private static byte[] BuildCloseRangeAttackBody()
    {
        var w = new PacketWriter(96);
        w.WriteShort(V113CombatPackets.CloseRangeAttackRecvOp);
        w.WriteByte(0);
        w.WriteZeroBytes(8);
        w.WriteByte(0x12);
        w.WriteZeroBytes(8);
        w.WriteInt(0);
        w.WriteZeroBytes(12);
        w.WriteByte(0xAA);
        w.WriteByte(0xBB);
        w.WriteByte(0xCC);
        w.WriteByte(0xDD);
        w.WriteByte(0xEE);
        w.WriteInt(1234);
        w.WriteInt(100001);
        w.WriteZeroBytes(14);
        w.WriteInt(10);
        w.WriteInt(15);
        w.WriteInt(0);
        w.WriteShort(30);
        w.WriteShort(40);
        return w.ToArray();
    }
}
