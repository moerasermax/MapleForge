using Maple.Adapters.V113.Channel;
using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelRangedMagicAttackPacketTests
{
    [Fact]
    public void ParseRangedAttack_ReadsJavaParseDmgRFields()
    {
        var attack = V113RangedMagicAttackPackets.ParseRangedAttack(new PacketReader(BuildRangedAttackBody(), offset: 2));

        Assert.Equal(0x12, attack.EncodedTargetsAndHits);
        Assert.Equal(1, attack.Targets);
        Assert.Equal(2, attack.Hits);
        Assert.Equal(0, attack.SkillId);
        Assert.Equal(0xAA, attack.Unknown);
        Assert.Equal(0xBB, attack.Display);
        Assert.Equal(0xCC, attack.Animation);
        Assert.Equal(0xEE, attack.Speed);
        Assert.Equal(1234, attack.LastAttackTickCount);
        Assert.Equal(1, attack.ProjectileSlot);
        Assert.Equal(2, attack.CashProjectileSlot);
        Assert.Equal(0x29, attack.AreaOfEffect);
        Assert.Equal(30, attack.Position.X);
        Assert.Equal(40, attack.Position.Y);

        var target = Assert.Single(attack.Damage);
        Assert.Equal(100001, target.ObjectId);
        Assert.Equal(new[] { 10, 15 }, target.DamageLines);
        Assert.Equal(25, attack.ToCombatAttack().Targets[0].TotalDamage);
    }

    [Fact]
    public void RangedAttackBroadcast_MatchesJavaCreatorLayout_ForBasicAttack()
    {
        var attack = V113RangedMagicAttackPackets.ParseRangedAttack(new PacketReader(BuildRangedAttackBody(), offset: 2));

        var pkt = V113RangedMagicAttackPackets.RangedAttackBroadcast(
            1,
            attack,
            characterLevel: 10,
            projectileItemId: 2060000,
            mastery: 3);

        byte[] golden =
        {
            0xB3, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x12,
            0x0A,
            0x00,
            0xAA,
            0xBB,
            0xCC,
            0xEE,
            0x03,
            0xE0, 0x6E, 0x1F, 0x00,
            0xA1, 0x86, 0x01, 0x00,
            0x07,
            0x0A, 0x00, 0x00, 0x00,
            0x0F, 0x00, 0x00, 0x00,
            0x1E, 0x00,
            0x28, 0x00,
        };
        Assert.Equal(golden, pkt);
    }

    [Fact]
    public void ParseMagicAttack_ReadsJavaParseDmgMaFields()
    {
        var attack = V113RangedMagicAttackPackets.ParseMagicAttack(new PacketReader(BuildMagicAttackBody(), offset: 2));

        Assert.Equal(0x12, attack.EncodedTargetsAndHits);
        Assert.Equal(1, attack.Targets);
        Assert.Equal(2, attack.Hits);
        Assert.Equal(2121001, attack.SkillId);
        Assert.Equal(777, attack.Charge);
        Assert.Equal(0, attack.Unknown);
        Assert.Equal(0xBB, attack.Display);
        Assert.Equal(0xCC, attack.Animation);
        Assert.Equal(0xEE, attack.Speed);
        Assert.Equal(1234, attack.LastAttackTickCount);
        Assert.Equal(30, attack.Position.X);
        Assert.Equal(40, attack.Position.Y);

        var target = Assert.Single(attack.Damage);
        Assert.Equal(100001, target.ObjectId);
        Assert.Equal(new[] { 10, 15 }, target.DamageLines);
    }

    [Fact]
    public void MagicAttackBroadcast_MatchesJavaCreatorLayout_AndAppendsCharge()
    {
        var attack = V113RangedMagicAttackPackets.ParseMagicAttack(new PacketReader(BuildMagicAttackBody(), offset: 2));

        var pkt = V113RangedMagicAttackPackets.MagicAttackBroadcast(
            1,
            attack,
            characterLevel: 10,
            skillLevel: 5);

        byte[] golden =
        {
            0xB4, 0x00,
            0x01, 0x00, 0x00, 0x00,
            0x12,
            0x0A,
            0x05,
            0x29, 0x5D, 0x20, 0x00,
            0x00,
            0xBB,
            0xCC,
            0xEE,
            0x00,
            0x00, 0x00, 0x00, 0x00,
            0xA1, 0x86, 0x01, 0x00,
            0xFF,
            0x0A, 0x00, 0x00, 0x00,
            0x0F, 0x00, 0x00, 0x00,
            0x09, 0x03, 0x00, 0x00,
        };
        Assert.Equal(golden, pkt);
    }

    [Fact]
    public void ModifyInventoryQuantity_WritesUseQuantityMutation()
    {
        var mutation = new InventoryQuantityMutation(InventoryType.Use, 1, 2060000, 10, 8);

        Assert.Equal(
            new byte[] { 0x1B, 0x00, 0x00, 0x01, 0x01, 0x02, 0x01, 0x00, 0x08, 0x00 },
            V113RangedMagicAttackPackets.ModifyInventoryQuantity(mutation));
    }

    private static byte[] BuildRangedAttackBody()
    {
        var w = BeginAttackBody(V113RangedMagicAttackPackets.RangedAttackRecvOp, skillId: 0);
        w.WriteByte(0xAA);
        w.WriteByte(0xBB);
        w.WriteByte(0xCC);
        w.WriteByte(0xDD);
        w.WriteByte(0xEE);
        w.WriteInt(1234);
        w.WriteShort(1);
        w.WriteShort(2);
        w.WriteByte(0x29);
        WriteOneTarget(w);
        w.WriteInt(0x11223344);
        w.WriteShort(30);
        w.WriteShort(40);
        return w.ToArray();
    }

    private static byte[] BuildMagicAttackBody()
    {
        var w = BeginAttackBody(V113RangedMagicAttackPackets.MagicAttackRecvOp, skillId: 2121001);
        w.WriteInt(777);
        w.WriteByte(0xAA);
        w.WriteByte(0xBB);
        w.WriteByte(0xCC);
        w.WriteByte(0xDD);
        w.WriteByte(0xEE);
        w.WriteInt(1234);
        WriteOneTarget(w);
        w.WriteShort(30);
        w.WriteShort(40);
        return w.ToArray();
    }

    private static PacketWriter BeginAttackBody(short opcode, int skillId)
    {
        var w = new PacketWriter(128);
        w.WriteShort(opcode);
        w.WriteByte(0);
        w.WriteZeroBytes(8);
        w.WriteByte(0x12);
        w.WriteZeroBytes(8);
        w.WriteInt(skillId);
        w.WriteZeroBytes(12);
        return w;
    }

    private static void WriteOneTarget(PacketWriter w)
    {
        w.WriteInt(100001);
        w.WriteZeroBytes(14);
        w.WriteInt(10);
        w.WriteInt(15);
        w.WriteInt(0);
    }
}

