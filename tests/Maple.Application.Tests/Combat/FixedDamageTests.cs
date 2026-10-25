using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

/// <summary>P135：怪物 fixedDamage／onlyNormalAttack（對照 Java DamageParse.applyAttack:270、applyAttackMagic:681）。</summary>
public sealed class FixedDamageTests
{
    [Theory]
    [InlineData(-1, false, 0, false, 25L)]   // 一般怪：客戶端值（負值行不計）
    [InlineData(1, false, 0, false, 3L)]     // 固定 1：每行（含 miss 行）都 1
    [InlineData(1, false, 1001004, false, 3L)]
    [InlineData(1, true, 0, false, 3L)]      // 固定＋只吃普攻：普攻仍固定值
    [InlineData(1, true, 1001004, false, 0L)] // 技能 → 0
    [InlineData(1, true, 0, true, 0L)]       // 魔法一律視為技能
    [InlineData(-1, true, 0, false, 25L)]    // 只吃普攻：普攻保留客戶端值
    [InlineData(-1, true, 1001004, false, 0L)]
    [InlineData(-1, true, 2001004, true, 0L)]
    public void AdjustedTotalDamage_MatchesJava(int fixedDamage, bool onlyNormal, int skillId, bool isMagic, long expected)
    {
        var stats = new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1, FixedDamage: fixedDamage, OnlyNormalAttack: onlyNormal);
        var attack = new CombatAttack([], SkillId: skillId, IsMagic: isMagic);

        Assert.Equal(expected, CombatService.AdjustedTotalDamage(stats, attack, new CombatAttackTarget(1, [10, 15, -30])));
    }

    [Fact]
    public void ApplyAttack_FixedDamageMob_TakesFixedDamagePerLine()
    {
        var service = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = new Player(new Character { Id = 1, Name = "Atk" }, new Position(0, 0, 0, 0));
        var mob = new Mob(
            new MapMonster { MonsterId = 9300061, X = 0, Y = 0, Fh = 1 },
            new MobStats(9300061, 10, MaxMp: 0, Level: 1, Exp: 1, FixedDamage: 1),
            objectId: 100001);
        field.Add(player);
        field.Add(mob);

        var hit = Assert.Single(service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [9999, 9999])])).Hits);

        Assert.Equal(2, hit.AppliedDamage);
        Assert.Equal(8, mob.Hp);
    }

    [Fact]
    public void ApplyAttack_OnlyNormalAttackMob_IgnoresSkillDamage_AndDoesNotRecordAttacker()
    {
        var service = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = new Player(new Character { Id = 1, Name = "Atk" }, new Position(0, 0, 0, 0));
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1, OnlyNormalAttack: true),
            objectId: 100001);
        field.Add(player);
        field.Add(mob);

        service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [50])], SkillId: 1001004));

        Assert.Equal(100, mob.Hp);
        Assert.Empty(mob.GetRecentAttackers(DateTimeOffset.UtcNow));
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => null;
    }
}
