using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

/// <summary>P142：buff／被動／武器衍生的怪物異常（對照 Java DamageParse.applyAttack:398-512）。</summary>
public sealed class OnHitStatusApplierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ThiefSkill_WithCharge_SlowsNonBoss()
    {
        var charge = Effect(4111001, x: -20, y: 5);
        var (applier, player) = Setup([Skill(charge)], new FixedRandom(0));
        player.ApplyBuffStats(Buff(charge, MapleBuffStat.WK_CHARGE), T0);

        var applied = applier.Apply(MakeMob(), player, 4001344, T0);

        var slow = Assert.Single(applied);
        Assert.Equal(MonsterStatus.Speed, slow.Status);
        Assert.Equal(-20, slow.Value);
        Assert.Equal(T0.AddSeconds(5), slow.ExpiresAt);
        Assert.Empty(applier.Apply(MakeMob(boss: true), player, 4001344, T0));
        Assert.Empty(applier.Apply(MakeMob(), player, 2001004, T0)); // 不在技能組
    }

    [Fact]
    public void Venom_FirstLearnedSkillOnly_PoisonsWithDamage()
    {
        var venom = Effect(4220005, prop: 100, durationMs: 8000);
        var (applier, player) = Setup([Skill(venom)], new FixedRandom(0));
        player.Character.Job = 422;
        player.ChangeSkillLevel(4220005, 1, 1);

        var poison = Assert.Single(applier.Apply(MakeMob(), player, 4201005, T0));

        Assert.Equal(MonsterStatus.Poison, poison.Status);
        Assert.Equal(4220005, poison.SkillId);
        Assert.True(poison.PoisonDamage > 0);
        Assert.Equal(T0.AddSeconds(8), poison.ExpiresAt);
    }

    [Fact]
    public void AranSkill_WithBodyPressure_Neutralises()
    {
        var pressure = Effect(21101003, x: 3, prop: 100);
        var (applier, player) = Setup([Skill(pressure)], new FixedRandom(0));
        player.ApplyBuffStats(Buff(pressure, MapleBuffStat.BODY_PRESSURE), T0);

        var applied = Assert.Single(applier.Apply(MakeMob(), player, 21100001, T0));

        Assert.Equal(MonsterStatus.Neutralise, applied.Status);
        Assert.Equal(T0.AddSeconds(3), applied.ExpiresAt);
    }

    [Theory]
    [InlineData(24, true)]
    [InlineData(25, false)]
    public void WeaponStatus_25PercentChance(int roll, bool applies)
    {
        var (applier, player) = Setup([], new FixedRandom(roll));
        player.Character.Equips.Add(new EquipEntry { Position = PlayerLocalStatsCalculator.WeaponSlot, ItemId = 1302108 });

        var applied = applier.Apply(MakeMob(), player, 0, T0);

        if (applies)
        {
            var slow = Assert.Single(applied);
            Assert.Equal((MonsterStatus.Speed, -50, 3121007), (slow.Status, slow.Value, slow.SkillId));
            Assert.Equal(T0.AddSeconds(10), slow.ExpiresAt);
        }
        else
        {
            Assert.Empty(applied);
        }
    }

    [Fact]
    public void BlindAndHamstring_ApplyAccAndSpeed()
    {
        var blind = Effect(3221006, x: -30, y: 4, prop: 100);
        var hamstring = Effect(3121007, x: -40, y: 6, prop: 100);
        var (applier, player) = Setup([Skill(blind), Skill(hamstring)], new FixedRandom(0));
        player.ApplyBuffStats(Buff(blind, MapleBuffStat.BLIND), T0);
        player.ApplyBuffStats(Buff(hamstring, MapleBuffStat.HAMSTRING), T0);

        var applied = applier.Apply(MakeMob(), player, 0, T0);

        Assert.Equal([MonsterStatus.Acc, MonsterStatus.Speed], applied.Select(static a => a.Status));
        Assert.Equal(-30, applied[0].Value);
        Assert.Equal(3121007, applied[1].SkillId);
    }

    [Fact]
    public void PaladinIceCharge_Freezes()
    {
        var ice = Effect(1211006, y: 3);
        var (applier, player) = Setup([Skill(ice)], new FixedRandom(0));
        player.Character.Job = 122;
        player.ChangeSkillLevel(1211006, 1, 1);
        player.ApplyBuffStats(Buff(ice, MapleBuffStat.WK_CHARGE), T0);

        var freeze = Assert.Single(applier.Apply(MakeMob(), player, 1001004, T0));

        Assert.Equal(MonsterStatus.Freeze, freeze.Status);
        Assert.Equal(T0.AddSeconds(6), freeze.ExpiresAt);
    }

    [Fact]
    public void CombatService_WithCatalog_ReportsOnHitStatuses_ButNotForMagic()
    {
        var charge = Effect(4111001, x: -20, y: 5);
        var catalog = new InMemorySkillCatalog([Skill(charge)]);
        var service = new CombatService(new MapService(new EmptyDataProvider()), random: new FixedRandom(0), skills: catalog);
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        player.ApplyBuffStats(Buff(charge, MapleBuffStat.WK_CHARGE), T0);
        var mob = MakeMob();
        field.Add(player);
        field.Add(mob);

        var hit = Assert.Single(service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [10])], null, 4001344)).Hits);
        Assert.Equal(MonsterStatus.Speed, Assert.Single(hit.AppliedStatuses!).Status);

        mob.CancelStatus(MonsterStatus.Speed);
        var magic = Assert.Single(service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [10])], null, 4001344, IsMagic: true)).Hits);
        Assert.Null(magic.AppliedStatuses);
    }

    private static MapleStatEffect Effect(int id, int x = 0, int y = 0, int prop = 100, int durationMs = 60_000)
        => new() { SourceId = id, Level = 1, X = x, Y = y, Prop = prop, DurationMilliseconds = durationMs };

    private static MapleSkill Skill(MapleStatEffect effect) => new() { Id = effect.SourceId, Effects = [effect] };

    private static MapleStatEffect Buff(MapleStatEffect source, MapleBuffStat stat)
        => new()
        {
            SourceId = source.SourceId,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new BuffStatValue(stat, source.X)],
        };

    private static (OnHitStatusApplier Applier, Player Player) Setup(IEnumerable<MapleSkill> skills, Random random)
        => (new OnHitStatusApplier(new InMemorySkillCatalog(skills), random), MakePlayer());

    private static Player MakePlayer() => new(new Character { Id = 1, Name = "Atk", Job = 412, Level = 70 }, new Position(0, 0, 0, 0));

    private static Mob MakeMob(bool boss = false)
        => new(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 10_000, MaxMp: 0, Level: 1, Exp: 1, Boss: boss),
            100001);

    private sealed class FixedRandom(int value) : Random
    {
        public override int Next(int maxValue) => Math.Min(value, maxValue - 1);

        public override double NextDouble() => 0;
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => null;
    }
}
