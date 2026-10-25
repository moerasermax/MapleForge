using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

/// <summary>P132：玩家技能霧（對照 Java MapleStatEffect.isMist／calculateBoundingBox、MapleMist、MapleMap.spawnMist 排程）。</summary>
public sealed class PlayerMistServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly MobSkillArea Area = new(-200, -150, 50, 150);

    [Theory]
    [InlineData(true, -100, 250)]  // lt.x + x = -100、rb.x + x = 150
    [InlineData(false, 50, 250)]   // -rb.x + x = 50、-lt.x + x = 300
    public void TrySpawn_BoxFollowsJavaCalculateBoundingBox(bool facingLeft, int expectedX, int expectedWidth)
    {
        var (field, owner) = Setup();

        var mist = new PlayerMistService().TrySpawn(field, owner, PoisonMist(), new Position(100, 50, 0, 0), facingLeft, T0)!;

        Assert.Equal((expectedX, -100, expectedWidth, 300), mist.Box);
        Assert.False(mist.IsMobMist);
        Assert.Equal(1, mist.PoisonMode);
        Assert.Equal(owner.Character.Id, mist.OwnerId);
        Assert.Equal(30, mist.SkillLevel);
        Assert.Equal((short)8, mist.SkillDelay);
        Assert.Equal(T0.AddSeconds(40), mist.ExpiresAt);
        Assert.Equal(T0.AddMilliseconds(2000), mist.NextEffectAt);
        Assert.Same(mist, field.Get(mist.ObjectId));
    }

    [Fact]
    public void TrySpawn_WithoutArea_UsesJavaDegenerateRectangle()
    {
        var (field, owner) = Setup();
        var effect = PoisonMist(noArea: true);

        var left = new PlayerMistService().TrySpawn(field, owner, effect, new Position(10, 20, 0, 0), facingLeft: true, T0)!;
        var right = new PlayerMistService().TrySpawn(field, owner, effect, new Position(10, 20, 0, 0), facingLeft: false, T0)!;

        Assert.Equal((10, 20, 1, 1), left.Box);
        Assert.Equal((10, 20, -1, 1), right.Box);
    }

    [Theory]
    [InlineData(4221006, 0)]
    [InlineData(2111003, 1)]
    [InlineData(12111005, 1)]
    [InlineData(14111006, 1)]
    [InlineData(22161003, 2)]
    public void PoisonMode_MatchesJavaMapleMistConstructor(int skillId, int mode)
    {
        Assert.True(PlayerMistService.IsMistSkill(skillId));
        Assert.Equal(mode, PlayerMistService.PoisonModeOf(skillId));
    }

    [Fact]
    public void TrySpawn_NonMistSkill_ReturnsNull()
    {
        var (field, owner) = Setup();

        Assert.Null(new PlayerMistService().TrySpawn(field, owner, PoisonMist(sourceId: 2101004), owner.Position, true, T0));
        Assert.Empty(field.Objects.OfType<Mist>());
    }

    [Fact]
    public void SmokeScreen_HasNoPeriodicEffect()
    {
        var (field, owner) = Setup();

        var mist = new PlayerMistService().TrySpawn(field, owner, PoisonMist(sourceId: 4221006), owner.Position, true, T0)!;

        Assert.Null(mist.NextEffectAt);
        Assert.True(new PlayerMistService().Tick(field, T0.AddSeconds(5)).IsEmpty);
    }

    [Fact]
    public void Tick_PoisonMist_PoisonsMobsInsideBoxAfterFirstDelay()
    {
        var (field, owner) = Setup();
        var inside = AddMob(field, 200001, 0, 0);
        var outside = AddMob(field, 200002, 900, 0);
        var service = new PlayerMistService();
        var mist = service.TrySpawn(field, owner, PoisonMist(), new Position(0, 0, 0, 0), true, T0)!;

        Assert.True(service.Tick(field, T0.AddMilliseconds(1999)).IsEmpty);

        var tick = service.Tick(field, T0.AddMilliseconds(2000));

        var (objectId, status) = Assert.Single(tick.Poisoned);
        Assert.Equal(inside.ObjectId, objectId);
        Assert.Equal(MonsterStatus.Poison, status.Status);
        Assert.Equal(2111003, status.SkillId);
        Assert.Equal(owner.Character.Id, status.FromCharacterId);
        Assert.True(status.PoisonDamage > 0);
        // Java applyStatus(..., duration = 霧總時間)。
        Assert.Equal(T0.AddMilliseconds(2000 + 40_000), status.ExpiresAt);
        Assert.True(inside.Statuses.ContainsKey(MonsterStatus.Poison));
        Assert.False(outside.Statuses.ContainsKey(MonsterStatus.Poison));
        Assert.Equal(T0.AddMilliseconds(4500), mist.NextEffectAt);
    }

    [Fact]
    public void Tick_AlreadyPoisonedOrBoss_IsSkipped()
    {
        var (field, owner) = Setup();
        var service = new PlayerMistService();
        var boss = AddMob(field, 200001, 0, 0, boss: true);
        service.TrySpawn(field, owner, PoisonMist(), new Position(0, 0, 0, 0), true, T0);

        Assert.Empty(service.Tick(field, T0.AddMilliseconds(2000)).Poisoned);
        Assert.False(boss.Statuses.ContainsKey(MonsterStatus.Poison));

        var normal = AddMob(field, 300002, 0, 0);
        Assert.Single(service.Tick(field, T0.AddMilliseconds(4500)).Poisoned);
        Assert.Empty(service.Tick(field, T0.AddMilliseconds(7000)).Poisoned);
        Assert.True(normal.Statuses.ContainsKey(MonsterStatus.Poison));
    }

    [Fact]
    public void Tick_OwnerLeftField_DoesNotPoison()
    {
        var (field, owner) = Setup();
        AddMob(field, 200001, 0, 0);
        var service = new PlayerMistService();
        service.TrySpawn(field, owner, PoisonMist(), new Position(0, 0, 0, 0), true, T0);
        field.Remove(owner.ObjectId);

        Assert.True(service.Tick(field, T0.AddMilliseconds(2000)).IsEmpty);
    }

    [Fact]
    public void Tick_ExpiredMist_HasNoEffect()
    {
        var (field, owner) = Setup();
        AddMob(field, 200001, 0, 0);
        var service = new PlayerMistService();
        service.TrySpawn(field, owner, PoisonMist(durationMs: 1000), new Position(0, 0, 0, 0), true, T0);

        Assert.True(service.Tick(field, T0.AddMilliseconds(2000)).IsEmpty);
    }

    [Fact]
    public void Tick_RecoveryAura_RestoresMpByPercentOfCurrentMaxMp()
    {
        var (field, owner) = Setup();
        owner.Character.Stats.MaxMp = 1000;
        owner.Character.Stats.Mp = 100;
        var far = new Player(new Character { Id = 2, Name = "Far" }, new Position(900, 0, 0, 0));
        far.Character.Stats.MaxMp = 1000;
        far.Character.Stats.Mp = 100;
        field.Add(far);
        var service = new PlayerMistService();
        var aura = new MapleStatEffect { SourceId = 22161003, Level = 30, X = 15, DurationMilliseconds = 30_000, Area = Area };
        service.TrySpawn(field, owner, aura, new Position(0, 0, 0, 0), true, T0);

        var tick = service.Tick(field, T0.AddMilliseconds(2000));

        Assert.Same(owner, Assert.Single(tick.MpRestored));
        Assert.Equal((short)250, owner.Mp);
        Assert.Equal((short)100, far.Mp);
    }

    private static MapleStatEffect PoisonMist(int sourceId = 2111003, int durationMs = 40_000, bool noArea = false)
        => new()
        {
            SourceId = sourceId,
            Level = 30,
            DurationMilliseconds = durationMs,
            Prop = 100,
            Area = noArea ? null : Area,
        };

    private static (FieldInstance Field, Player Owner) Setup()
    {
        var field = new FieldInstance(100000000);
        var owner = new Player(new Character { Id = 1, Name = "FP", Job = 211 }, new Position(0, 0, 0, 0));
        owner.ChangeSkillLevel(2111003, 30, 30);
        owner.ChangeSkillLevel(22161003, 30, 30);
        field.Add(owner);
        return (field, owner);
    }

    private static Mob AddMob(FieldInstance field, int objectId, short x, short y, bool boss = false)
    {
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = x, Y = y, Fh = 1 },
            new MobStats(100100, 10_000, MaxMp: 100, Level: 1, Exp: 1, Boss: boss),
            objectId);
        field.Add(mob);
        return mob;
    }
}
