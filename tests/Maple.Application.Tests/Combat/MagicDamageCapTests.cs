using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

/// <summary>P137：魔法傷害上限（對照 Java DamageParse.applyAttackMagic、calculateMaxMagicDamagePerHit、SkillConstants.getMaxDamage）。</summary>
public sealed class MagicDamageCapTests
{
    private static readonly PlayerLocalStats Stats = new(4, 4, 100, 4, 0, 200, 100, 100, 100.0, 100.0, 108, 1f);

    [Fact]
    public void BaseMax_UsesTotalMagicBaseIntAndSkillMad()
    {
        var effect = new MapleStatEffect { SourceId = 2121003, Matk = 100 };

        // 200 * (50 + (5.8)^2 + 330) / 100 * 1.04
        Assert.Equal(200 * (50 + 5.8 * 5.8 + 330) / 100 * 1.04, MagicDamageCap.BaseMaxDamagePerHit(2121003, effect, Stats, 100), 6);
    }

    [Theory]
    [InlineData(1000, 40 * 1.04)]
    [InlineData(10001020, 1 * 1.04)]
    public void BaseMax_SnailAndPyramidAreFixed(int skillId, double expected)
        => Assert.Equal(expected, MagicDamageCap.BaseMaxDamagePerHit(skillId, null, Stats, 100), 6);

    [Fact]
    public void MaxPerHit_AppliesDefenseThenDamageRate()
    {
        var mob = MakeMob(magicDefense: 100);

        // (1000 - 50) + (950 * 100 / 100) = 1900
        Assert.Equal(1900, MagicDamageCap.MaxDamagePerHit(MakePlayer(), 2121003, mob, Stats, Element.Fire, 1000), 6);
    }

    [Theory]
    [InlineData(ElementalEffectiveness.Weak, 3000)]
    [InlineData(ElementalEffectiveness.Strong, 1000)]
    [InlineData(ElementalEffectiveness.Immune, 2)]
    public void MaxPerHit_ElementEffectiveness(ElementalEffectiveness effectiveness, double expected)
    {
        var mob = MakeMob(resistances: new Dictionary<Element, ElementalEffectiveness> { [Element.Fire] = effectiveness });

        Assert.Equal(expected, MagicDamageCap.MaxDamagePerHit(MakePlayer(), 2121003, mob, Stats, Element.Fire, 1000), 6);
    }

    [Fact]
    public void MaxPerHit_DoomedMobIsNormal()
    {
        var mob = MakeMob(resistances: new Dictionary<Element, ElementalEffectiveness> { [Element.Fire] = ElementalEffectiveness.Immune });
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Doom, 1, 2311005, null, null));

        Assert.Equal(2000, MagicDamageCap.MaxDamagePerHit(MakePlayer(), 2121003, mob, Stats, Element.Fire, 1000), 6);
    }

    [Fact]
    public void MaxPerHit_HighEvasionMob_IsZero()
    {
        // minAccuracy = 500 * 51 / 120 = 212 > floor(100/10) + floor(4/10) = 10
        Assert.Equal(0, MagicDamageCap.MaxDamagePerHit(MakePlayer(), 2121003, MakeMob(eva: 500), Stats, Element.Fire, 1000));
    }

    [Fact]
    public void MaxPerHit_CappedAt199999()
        => Assert.Equal(199_999, MagicDamageCap.MaxDamagePerHit(MakePlayer(), 2121003, MakeMob(), Stats, Element.Fire, 500_000));

    [Theory]
    [InlineData(1500, 1500)]  // 未超過上限×2
    [InlineData(5000, 4000)]  // 超過 → (int)(上限×2)
    public void AdjustLine_ClampsToTwiceMax(int eachd, int expected)
        => Assert.Equal(expected, MagicDamageCap.AdjustLine(eachd, MakePlayer(level: 100), 2121003, null, MakeMob(), 1000, 2000));

    [Fact]
    public void AdjustLine_ClampedAtOrAbove10000_Aborts()
        => Assert.Null(MagicDamageCap.AdjustLine(30000, MakePlayer(level: 100), 2121003, null, MakeMob(), 1000, 5000));

    [Fact]
    public void AdjustLine_OverLevelThreshold_Aborts_UnlessEqualToMobMaxHp()
    {
        var player = MakePlayer(level: 30); // 門檻 2200
        Assert.Null(MagicDamageCap.AdjustLine(2500, player, 2121003, null, MakeMob(maxHp: 100_000), 1000, 1500));
        Assert.Equal(2500, MagicDamageCap.AdjustLine(2500, player, 2121003, null, MakeMob(maxHp: 2500), 1000, 1500));
    }

    [Fact]
    public void AdjustLine_ShieldedMob_ClampsToBaseMax()
    {
        var mob = MakeMob();
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.MagicImmunity, 1, 0, new MobSkillSource(141, 1), null));

        Assert.Equal(1000, MagicDamageCap.AdjustLine(5000, MakePlayer(level: 100), 2121003, null, mob, 1000.9, 2000));
    }

    [Fact]
    public void AdjustLine_FixedDamageMob_IgnoresCap()
        => Assert.Equal(1, MagicDamageCap.AdjustLine(99999, MakePlayer(level: 100), 2121003, null, MakeMob(fixedDamage: 1), 10, 10));

    [Fact]
    public void AdjustLine_NoviceSnail_OverFixDamage_Aborts()
    {
        var effect = new MapleStatEffect { SourceId = 1000, FixDamage = 10 };

        Assert.Null(MagicDamageCap.AdjustLine(11, MakePlayer(level: 5), 1000, effect, MakeMob(), 41.6, 41.6));
        Assert.Equal(10, MagicDamageCap.AdjustLine(10, MakePlayer(level: 5), 1000, effect, MakeMob(), 41.6, 41.6));
    }

    [Theory]
    [InlineData(3, 1001004, 96)]
    [InlineData(20, 2121003, 1000)]
    [InlineData(55, 2121003, 8000)]
    [InlineData(70, 2121003, 200_000)]
    public void MaxDamageByLevel_MatchesJavaTable(int level, int skillId, int expected)
        => Assert.Equal(expected, MagicDamageCap.MaxDamageByLevel(level, skillId));

    [Fact]
    public void CombatService_MagicAttack_ClampsAndAbortsLikeJava()
    {
        var calculator = new PlayerLocalStatsCalculator(new InMemorySkillCatalog([]));
        var service = new CombatService(new MapService(new EmptyDataProvider()), localStats: calculator);
        var field = new FieldInstance(100000100);
        var player = MakePlayer(level: 30);
        player.Character.Stats.Int = 100;
        var first = MakeMob(objectId: 100001, maxHp: 100_000);
        var second = MakeMob(objectId: 100002, maxHp: 100_000);
        field.Add(player);
        field.Add(first);
        field.Add(second);
        var effect = new MapleStatEffect { SourceId = 2121003, Matk = 100, SourceElement = Element.Fire };

        // base = 100 * (50 + 33.64 + 330) / 100 * 1.04 = 430.19；上限 = 860.37；×2 = 1720
        var result = service.ApplyAttack(field, player, new CombatAttack(
            [new CombatAttackTarget(first.ObjectId, [2000]), new CombatAttackTarget(second.ObjectId, [5000])],
            effect,
            2121003,
            IsMagic: true));

        Assert.True(result.Aborted);
        Assert.Equal(1720, Assert.Single(result.Hits).AppliedDamage);
        Assert.Equal(100_000 - 1720, first.Hp);
        Assert.Equal(100_000, second.Hp);
    }

    [Fact]
    public void CombatService_WithoutCalculator_KeepsClientDamage()
    {
        var service = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = MakePlayer(level: 30);
        var mob = MakeMob(maxHp: 100_000);
        field.Add(player);
        field.Add(mob);

        var result = service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [5000])], null, 2121003, IsMagic: true));

        Assert.False(result.Aborted);
        Assert.Equal(5000, Assert.Single(result.Hits).AppliedDamage);
    }

    private static Player MakePlayer(int level = 50)
        => new(new Character { Id = 1, Name = "Mage", Job = 212, Level = (byte)level }, new Position(0, 0, 0, 0));

    private static Mob MakeMob(
        int objectId = 100001,
        long maxHp = 100_000,
        int eva = 0,
        int magicDefense = 0,
        int fixedDamage = -1,
        IReadOnlyDictionary<Element, ElementalEffectiveness>? resistances = null)
        => new(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, maxHp, MaxMp: 0, Level: 1, Exp: 1, Resistances: resistances, FixedDamage: fixedDamage, Eva: eva, MagicDefense: magicDefense),
            objectId);

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => null;
    }
}
