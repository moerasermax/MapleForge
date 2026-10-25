using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

/// <summary>P141：攻擊時的攻擊者 HP 效果與疾風步／隱身術解除（對照 Java DamageParse.applyAttack）。</summary>
public sealed class AttackerDrainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Drain_UsesCumulativeTotalDamage_CappedAtHalfBaseMaxHp()
    {
        var (service, field, player) = Setup();
        var first = AddMob(field, 100001, 10_000);
        var second = AddMob(field, 100002, 10_000);

        service.ApplyAttack(field, player, new CombatAttack(
            [new CombatAttackTarget(first.ObjectId, [1000]), new CombatAttackTarget(second.ObjectId, [500])],
            new MapleStatEffect { SourceId = 4101005, X = 20 },
            4101005));

        // 第一隻：min(200, 500) → 300；第二隻：totDamage 1500 → +300 → 600
        Assert.Equal((short)600, player.Hp);
    }

    [Fact]
    public void Drain_CappedByHalfMaxHpAndMobMaxHp()
    {
        var (service, field, player) = Setup();
        var mob = AddMob(field, 100001, 150);

        service.ApplyAttack(field, player, new CombatAttack(
            [new CombatAttackTarget(mob.ObjectId, [100_000])],
            new MapleStatEffect { SourceId = 5111004, X = 50 },
            5111004));

        Assert.Equal((short)(100 + 150), player.Hp); // 怪物最大 HP 150 < 1000/2
    }

    [Fact]
    public void ComboDrain_UsesBuffValue()
    {
        var (service, field, player) = Setup();
        player.ApplyBuffStats(Buff(21100005, MapleBuffStat.COMBO_DRAIN, 10), T0);
        var mob = AddMob(field, 100001, 10_000);

        service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [1000])], null, 21000002));

        Assert.Equal((short)200, player.Hp);
    }

    [Fact]
    public void HomingSkill_LinksLastHitMob()
    {
        var (service, field, player) = Setup();
        var first = AddMob(field, 100001, 10_000);
        var second = AddMob(field, 100002, 10_000);

        service.ApplyAttack(field, player, new CombatAttack(
            [new CombatAttackTarget(first.ObjectId, [10]), new CombatAttackTarget(second.ObjectId, [10])],
            new MapleStatEffect { SourceId = 5211006 },
            5211006));

        Assert.Equal(second.ObjectId, player.LinkMobObjectId);
    }

    [Fact]
    public void Sacrifice_CostsHpButLeavesOne()
    {
        var (service, field, player) = Setup();
        player.Character.Stats.Hp = 500;
        var mob = AddMob(field, 100001, 100_000);

        service.ApplyAttack(field, player, new CombatAttack(
            [new CombatAttackTarget(mob.ObjectId, [1000])],
            new MapleStatEffect { SourceId = 1311005, X = 5 },
            1311005));
        Assert.Equal((short)450, player.Hp);

        service.ApplyAttack(field, player, new CombatAttack(
            [new CombatAttackTarget(mob.ObjectId, [50_000])],
            new MapleStatEffect { SourceId = 1311005, X = 5 },
            1311005));
        Assert.Equal((short)1, player.Hp);
    }

    [Fact]
    public void MagicAndSummonAttacks_DoNotDrain()
    {
        var (service, field, player) = Setup();
        var mob = AddMob(field, 100001, 100_000);
        var drain = new MapleStatEffect { SourceId = 4101005, X = 20 };

        service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [1000])], drain, 4101005, IsMagic: true));
        service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [1000])], drain, 4101005, FromSummon: true));

        Assert.Equal((short)100, player.Hp);
    }

    [Fact]
    public void CancelBuffsOnAttack_CancelsWindWalkAndDarkSight()
    {
        var skills = new SkillService(new InMemorySkillCatalog([]));
        var player = MakePlayer();
        player.ApplyBuffStats(Buff(4001003, MapleBuffStat.DARKSIGHT, 1), T0);
        player.ApplyBuffStats(Buff(11101005, MapleBuffStat.WIND_WALK, 1), T0);
        player.ApplyBuffStats(Buff(1101004, MapleBuffStat.BOOSTER, 2), T0);

        var cancelled = skills.CancelBuffsOnAttack(player);

        Assert.Equal([11101005, 4001003], cancelled.Select(static c => c.SourceId));
        Assert.Null(player.GetBuffValue(MapleBuffStat.DARKSIGHT));
        Assert.Equal(2, player.GetBuffValue(MapleBuffStat.BOOSTER));
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(0, true)]
    public void CancelBuffsOnAttack_AdvancedDarkSight_KeepsOnChance(int prop, bool expectCancelled)
    {
        var advanced = Buff(SkillService.AdvancedDarkSightSkillId, MapleBuffStat.DARKSIGHT, 1);
        var catalog = new InMemorySkillCatalog(
        [
            new MapleSkill
            {
                Id = SkillService.AdvancedDarkSightSkillId,
                Effects = [new MapleStatEffect { SourceId = SkillService.AdvancedDarkSightSkillId, Level = 1, Prop = prop }],
            },
        ]);
        var player = MakePlayer();
        player.ApplyBuffStats(advanced, T0);

        var cancelled = new SkillService(catalog).CancelBuffsOnAttack(player);

        Assert.Equal(expectCancelled, cancelled.Count == 1);
    }

    private static MapleStatEffect Buff(int sourceId, MapleBuffStat stat, int value)
        => new()
        {
            SourceId = sourceId,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new BuffStatValue(stat, value)],
        };

    private static (CombatService Service, FieldInstance Field, Player Player) Setup()
    {
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        field.Add(player);
        return (new CombatService(new MapService(new EmptyDataProvider())), field, player);
    }

    private static Player MakePlayer()
    {
        var player = new Player(new Character { Id = 1, Name = "Atk", Job = 410, Level = 70 }, new Position(0, 0, 0, 0));
        player.Character.Stats.MaxHp = 1000;
        player.Character.Stats.Hp = 100;
        return player;
    }

    private static Mob AddMob(FieldInstance field, int objectId, long maxHp)
    {
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, maxHp, MaxMp: 0, Level: 1, Exp: 1),
            objectId);
        field.Add(mob);
        return mob;
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => null;
    }
}
