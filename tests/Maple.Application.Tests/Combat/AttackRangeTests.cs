using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

/// <summary>P138：攻擊射程檢查（對照 Java GameConstants.getAttackRange、DamageParse 的 range &gt; SkillRange → return）。</summary>
public sealed class AttackRangeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, 100_000)]
    [InlineData(1432000, 200_000)] // Java「槍」
    [InlineData(1442000, 180_000)] // Java「矛」
    [InlineData(1472000, 250_000)]
    [InlineData(1452000, 220_000)]
    [InlineData(1492000, 220_000)]
    [InlineData(1302000, 100_000)]
    public void NormalAttack_UsesWeaponTable(int? weaponId, double expected)
    {
        var player = MakePlayer();
        if (weaponId is { } id)
        {
            player.Character.Equips.Add(new EquipEntry { Position = PlayerLocalStatsCalculator.WeaponSlot, ItemId = id });
        }

        Assert.Equal(expected, AttackRange.MaxDistanceSq(player, null, 0, 0));
    }

    [Fact]
    public void SkillAttack_BaseReachPlusAreaPlusRange()
    {
        var effect = new MapleStatEffect { SourceId = 3001004, Range = 50, Area = new MobSkillArea(-300, -100, 20, 40) };

        Assert.Equal(300 * 300 + 100 * 100, effect.MaxDistanceSq);
        Assert.Equal(550.0 * 550 + 100_000 + 2500, AttackRange.MaxDistanceSq(MakePlayer(), effect, 3001004, defRange: 100));
    }

    [Fact]
    public void SkillTable_OverridesFormula()
        => Assert.Equal(750_000, AttackRange.MaxDistanceSq(MakePlayer(), new MapleStatEffect { SourceId = 2121007 }, 2121007, 0));

    [Theory]
    [InlineData(925020100, true)]
    [InlineData(280030000, true)]
    [InlineData(100000000, false)]
    public void IsBossMap_MatchesJava(int mapId, bool expected) => Assert.Equal(expected, AttackRange.IsBossMap(mapId));

    [Fact]
    public void DefRange_BowmanPassiveAddsRange()
    {
        var player = MakePlayer(job: 310);
        player.ChangeSkillLevel(3000002, 1, 1);
        var catalog = new InMemorySkillCatalog(
            [new MapleSkill { Id = 3000002, Effects = [new MapleStatEffect { SourceId = 3000002, Level = 1, Range = 30 }] }]);

        Assert.Equal(130, new PlayerLocalStatsCalculator(catalog).Calculate(player).DefRange);
        Assert.Equal(80, new PlayerLocalStatsCalculator(catalog).Calculate(MakePlayer(job: 2110)).DefRange);
        Assert.Equal(0, new PlayerLocalStatsCalculator(catalog).Calculate(MakePlayer(job: 110)).DefRange);
    }

    [Fact]
    public void UsingPortal_LastsTwoSeconds()
    {
        var player = MakePlayer();
        Assert.False(player.IsUsingPortal(T0));

        player.MarkUsingPortal(T0);

        Assert.True(player.IsUsingPortal(T0.AddSeconds(2)));
        Assert.False(player.IsUsingPortal(T0.AddSeconds(2.001)));
    }

    [Fact]
    public void CombatService_FarMob_AbortsBeforeDamage()
    {
        var (service, field, player, near, far) = Setup(farX: 400); // 400² = 160000 > 100000（徒手）

        var result = service.ApplyAttack(field, player, new CombatAttack(
            [new CombatAttackTarget(near.ObjectId, [10]), new CombatAttackTarget(far.ObjectId, [10])]));

        Assert.True(result.Aborted);
        Assert.Equal(90, near.Hp);
        Assert.Equal(100, far.Hp);
    }

    [Fact]
    public void CombatService_MobWithinRange_IsDamaged()
    {
        var (service, field, player, _, far) = Setup(farX: 300); // 90000 ≤ 100000

        var result = service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(far.ObjectId, [10])]));

        Assert.False(result.Aborted);
        Assert.Equal(90, far.Hp);
    }

    [Fact]
    public void CombatService_BossMap_SkipsRangeCheck()
    {
        var (service, field, player, _, far) = Setup(farX: 2000);
        player.Character.MapId = 280030000;

        Assert.False(service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(far.ObjectId, [10])])).Aborted);
        Assert.Equal(90, far.Hp);
    }

    [Fact]
    public void CombatService_PortalWindow_ExemptsPhysicalButNotMagic()
    {
        var (service, field, player, _, far) = Setup(farX: 2000, time: new FixedTime(T0));
        player.MarkUsingPortal(T0);

        Assert.False(service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(far.ObjectId, [10])])).Aborted);
        Assert.True(service.ApplyAttack(field, player, new CombatAttack(
            [new CombatAttackTarget(far.ObjectId, [10])],
            new MapleStatEffect { SourceId = 2101004 },
            2101004,
            IsMagic: true)).Aborted);
    }

    [Fact]
    public void CombatService_SummonAttack_SkipsRangeCheck()
    {
        var (service, field, player, _, far) = Setup(farX: 2000);

        Assert.False(service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(far.ObjectId, [10])], FromSummon: true)).Aborted);
    }

    private static (CombatService Service, FieldInstance Field, Player Player, Mob Near, Mob Far) Setup(short farX, TimeProvider? time = null)
    {
        var service = new CombatService(
            new MapService(new EmptyDataProvider()),
            timeProvider: time,
            localStats: new PlayerLocalStatsCalculator(new InMemorySkillCatalog([])));
        var field = new FieldInstance(100000100);
        var player = MakePlayer(level: 70);
        var near = MakeMob(100001, 50);
        var far = MakeMob(100002, farX);
        field.Add(player);
        field.Add(near);
        field.Add(far);
        return (service, field, player, near, far);
    }

    private static Player MakePlayer(int job = 100, int level = 30)
        => new(new Character { Id = 1, Name = "Atk", Job = (short)job, Level = (byte)level, MapId = 100000100 }, new Position(0, 0, 0, 0));

    private static Mob MakeMob(int objectId, short x)
        => new(
            new MapMonster { MonsterId = 100100, X = x, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1),
            objectId);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => null;
    }
}
