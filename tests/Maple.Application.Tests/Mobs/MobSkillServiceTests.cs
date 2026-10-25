using Maple.Application.Mobs;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Tests.Mobs;

/// <summary>P122：怪物技能資料與挑選（對照 Java MobHandler.MoveMonster、MobSkill、DumpMobSkills）。</summary>
public sealed class MobSkillServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryUseSkill_NoSkills_ReturnsNull()
    {
        var (field, mob) = Setup(skills: null);

        Assert.Null(Service(Skill(100)).TryUseSkill(field, mob, T0));
    }

    [Fact]
    public void TryUseSkill_FirstUse_SucceedsAndConsumesMp()
    {
        var (field, mob) = Setup([new MobSkillRef(100, 1)], mp: 50);

        var use = Service(Skill(100, mpCon: 5)).TryUseSkill(field, mob, T0);

        Assert.Equal(100, use!.Skill.SkillId);
        Assert.Equal(45, mob.Mp);
    }

    [Fact]
    public void TryUseSkill_Cooldown_IsEffectivelyDoubledAsInJava()
    {
        // 用無自身 buff 的技能（120 封印類），避免 P123 套上 buff 後被 checkCurrentBuff 擋下。
        var (field, mob) = Setup([new MobSkillRef(120, 1)]);
        var service = Service(Skill(120, coolMs: 10_000));

        Assert.NotNull(service.TryUseSkill(field, mob, T0));
        Assert.Null(service.TryUseSkill(field, mob, T0.AddSeconds(15)));   // now - (T0+10s) = 5s ≤ 10s
        Assert.NotNull(service.TryUseSkill(field, mob, T0.AddSeconds(20.001)));
    }

    [Fact]
    public void TryUseSkill_HpAboveThreshold_NotUsedButCooldownStarts()
    {
        var (field, mob) = Setup([new MobSkillRef(200, 1)], hp: 100, maxHp: 100);
        var service = Service(Skill(200, hpThreshold: 50, limit: 10));

        Assert.Null(service.TryUseSkill(field, mob, T0));
        Assert.Equal(T0, mob.GetLastSkillUsed(200));
    }

    [Fact]
    public void TryUseSkill_BuffAlreadyActive_Skipped()
    {
        var (field, mob) = Setup([new MobSkillRef(100, 1)]);
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.WeaponAttackUp, 10, 0, new MobSkillSource(100, 1), null));

        Assert.Null(Service(Skill(100)).TryUseSkill(field, mob, T0));
    }

    [Fact]
    public void IsBuffAlreadyActive_101ChecksWeaponDefenseUp_JavaQuirk()
    {
        var (field, mob) = Setup([new MobSkillRef(101, 1)]);
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.WeaponDefenseUp, 10, 0, null, null));

        Assert.True(MobSkillService.IsBuffAlreadyActive(Skill(101), mob, field));
    }

    [Fact]
    public void IsBuffAlreadyActive_SummonLimitReached()
    {
        var (field, mob) = Setup([new MobSkillRef(200, 1)]);

        Assert.True(MobSkillService.IsBuffAlreadyActive(Skill(200, limit: 1), mob, field));
        Assert.False(MobSkillService.IsBuffAlreadyActive(Skill(200, limit: 2), mob, field));
    }

    [Fact]
    public void SetLastSkillUsed_140And141_LockEachOther()
    {
        var (_, mob) = Setup(null);

        mob.SetLastSkillUsed(140, T0, 1000);

        Assert.Equal(T0.AddSeconds(2), mob.GetLastSkillUsed(140));
        Assert.Equal(T0, mob.GetLastSkillUsed(141));
    }

    [Fact]
    public void WzMobSkillCatalog_ReadsLevelNodeWithJavaDefaults()
    {
        var level = new Node("1", children: new Dictionary<string, IDataNode>
        {
            ["interval"] = new Node("interval", 45),
            ["hp"] = new Node("hp", 75),
            ["limit"] = new Node("limit", 5),
            ["summonEffect"] = new Node("summonEffect", 2),
            ["0"] = new Node("0", 7130200),
            ["1"] = new Node("1", 210100),
            ["lt"] = new Node("lt", new DataVector(-100, -50)),
            ["rb"] = new Node("rb", new DataVector(100, 10)),
        });
        var catalog = new WzMobSkillCatalog(new FakeProvider("MobSkill.img/200/level/1", level));

        var skill = catalog.Get(200, 1)!;

        Assert.Equal(45_000, skill.CoolTimeMilliseconds);
        Assert.Equal(75, skill.Hp);
        Assert.Equal(1, skill.X);          // Java 預設 1
        Assert.Equal(1.0, skill.Prop);     // 預設 100 → 1.0
        Assert.Equal(new[] { 7130200, 210100 }, skill.Summons);
        Assert.Equal(new MobSkillArea(-100, -50, 100, 10), skill.Area);
        Assert.Null(catalog.Get(200, 2));
    }

    // ── P123：自身 buff（Java applyEffect stats 分支 + applyMonsterBuff）──

    [Fact]
    public void TryUseSkill_WeaponAttackUp_AppliesToSelfWithMobSkillSource()
    {
        var (field, mob) = Setup([new MobSkillRef(100, 1)]);

        var use = Service(Skill(100)).TryUseSkill(field, mob, T0)!;

        var buff = Assert.Single(use.Buffs!);
        Assert.Equal(mob.ObjectId, buff.ObjectId);
        var effect = Assert.Single(buff.Applied);
        Assert.Equal(MonsterStatus.WeaponAttackUp, effect.Status);
        Assert.Equal(10, effect.Value);
        Assert.Equal(new MobSkillSource(100, 1), effect.MobSkill);
        Assert.Equal(T0.AddSeconds(30), effect.ExpiresAt);
    }

    [Fact]
    public void TryUseSkill_ReflectSkill145_AppliesFourStatuses()
    {
        var (field, mob) = Setup([new MobSkillRef(145, 1)]);

        var use = Service(Skill(145)).TryUseSkill(field, mob, T0)!;

        Assert.Equal(4, Assert.Single(use.Buffs!).Applied.Count);
        Assert.True(mob.Statuses.ContainsKey(MonsterStatus.MagicDamageReflect));
    }

    [Fact]
    public void ApplyMonsterBuff_ExistingStatus_IsCancelledFirst()
    {
        var (_, mob) = Setup(null);
        mob.TryApplyStatus(new MonsterStatusEffect(MonsterStatus.Speed, -30, 2101003, null, null));

        var (cancelled, applied) = mob.ApplyMonsterBuff(
            new Dictionary<MonsterStatus, int> { [MonsterStatus.Speed] = 20 },
            new MobSkillSource(156, 1),
            T0);

        Assert.Equal(new[] { MonsterStatus.Speed }, cancelled);
        Assert.Equal(20, mob.Statuses[MonsterStatus.Speed].Value);
        Assert.Single(applied);
    }

    [Fact]
    public void TryUseSkill_AreaSkill_BuffsMobsInsideBoundingBoxOnly()
    {
        var (field, caster) = Setup([new MobSkillRef(150, 1)]);
        var near = AddMob(field, 100002, x: 50, y: 0);
        var far = AddMob(field, 100003, x: 500, y: 0);
        var area = new MobSkillArea(-100, -50, 100, 50);
        var skill = Skill(150) with { Area = area };

        var use = Service(skill).TryUseSkill(field, caster, T0)!;

        Assert.Equal(new[] { caster.ObjectId, near.ObjectId }, use.Buffs!.Select(static b => b.ObjectId).Order());
        Assert.False(far.Statuses.ContainsKey(MonsterStatus.WeaponAttackUp));
    }

    [Fact]
    public void BoundingBox_FacingRight_MirrorsHorizontally()
    {
        var (_, mob) = Setup(null); // stance 5 → 面向左
        mob.MoveTo(new Position(0, 0, 4, 1)); // 面向右

        var box = MobSkillService.BoundingBox(new MobSkillArea(-200, -50, 20, 10), mob);

        Assert.Equal((-20, -50, 220, 60), box);
    }

    // ── P125：補血（Java applyEffect case 114 → heal(getHP(), 0, true)）──

    [Fact]
    public void TryUseSkill_Heal_RestoresHpByHpFieldCappedAtMax()
    {
        var (field, mob) = Setup([new MobSkillRef(114, 1)], hp: 40, maxHp: 100);

        var use = Service(Skill(114, hpThreshold: 50)).TryUseSkill(field, mob, T0)!;

        Assert.Equal(new MobHeal(mob.ObjectId, 50), Assert.Single(use.Heals!));
        Assert.Equal(90, mob.Hp);
    }

    [Fact]
    public void Heal_CapsAtMaxHp()
    {
        var (_, mob) = Setup(null, hp: 90, maxHp: 100);

        mob.Heal(500, 0);

        Assert.Equal(100, mob.Hp);
    }

    // ── P126：玩家異常（只施加在控制者身上）──

    [Fact]
    public void TryUseSkill_DiseaseSkill_GivesDiseaseToController()
    {
        var (field, mob) = Setup([new MobSkillRef(123, 1)]);
        var controller = new Player(new Maple.Core.Characters.Character { Id = 9, Name = "Ctrl" }, new Position(0, 0, 0, 0));

        var use = Service(Skill(123)).TryUseSkill(field, mob, T0, controller)!;

        Assert.Equal(PlayerDisease.Stun, use.Disease!.Disease);
        Assert.Equal(10, use.Disease.X);
        Assert.Equal(30_000, use.Disease.DurationMilliseconds);
        Assert.True(controller.HasDisease(PlayerDisease.Stun));
    }

    [Fact]
    public void TryUseSkill_DiseaseAreaSkill_ControllerOutOfRange_NoDisease()
    {
        var (field, mob) = Setup([new MobSkillRef(120, 1)]);
        var controller = new Player(new Maple.Core.Characters.Character { Id = 9, Name = "Ctrl" }, new Position(900, 0, 0, 0));
        var skill = Skill(120) with { Area = new MobSkillArea(-100, -50, 100, 50) };

        var use = Service(skill).TryUseSkill(field, mob, T0, controller)!;

        Assert.Null(use.Disease);
        Assert.Empty(controller.Diseases);
    }

    // ── P128：召喚（Java applyEffect case 200）──

    [Fact]
    public void TryUseSkill_Summon_SpawnsOnFootholdBelowWithController()
    {
        var (field, mob) = Setup([new MobSkillRef(200, 1)]);
        var controller = new Player(new Maple.Core.Characters.Character { Id = 9, Name = "Ctrl" }, new Position(0, 0, 0, 0));
        var skill = Skill(200, limit: 10) with { Summons = [100100, 8820007], SpawnEffect = 2 };
        var service = new MobSkillService(new SingleSkillCatalog(skill), new FirstRandom(), new Maple.Application.Maps.MapService(new SummonMapProvider()));

        var use = service.TryUseSkill(field, mob, T0, controller)!;

        var summoned = Assert.Single(use.Summoned!); // 8820007 跳過
        Assert.Equal(100100, summoned.Definition.MonsterId);
        Assert.Equal(new Position(0, 30, Mob.DefaultStance, 1), summoned.Position);
        Assert.Equal(9, summoned.ControllerId);
        Assert.Same(summoned, field.Get(summoned.ObjectId));
    }

    [Fact]
    public void TryUseSkill_Summon_WithoutMapService_DoesNotSpawn()
    {
        var (field, mob) = Setup([new MobSkillRef(200, 1)]);
        var skill = Skill(200, limit: 10) with { Summons = [100100] };

        Assert.Null(Service(skill).TryUseSkill(field, mob, T0)!.Summoned);
    }

    private sealed class SummonMapProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => (fileName, path) switch
        {
            ("Map", "Map/Map1/100000000.img") => new Node("100000000.img", children: new Dictionary<string, IDataNode>
            {
                ["foothold"] = new Node("foothold", children: new Dictionary<string, IDataNode>
                {
                    ["0"] = new Node("0", children: new Dictionary<string, IDataNode>
                    {
                        ["1"] = new Node("1", children: new Dictionary<string, IDataNode>
                        {
                            ["1"] = new Node("1", children: new Dictionary<string, IDataNode>
                            {
                                ["x1"] = new Node("x1", -100),
                                ["y1"] = new Node("y1", 30),
                                ["x2"] = new Node("x2", 100),
                                ["y2"] = new Node("y2", 30),
                            }),
                        }),
                    }),
                }),
            }),
            ("Mob", "0100100.img") => new Node("0100100.img", children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                {
                    ["maxHP"] = new Node("maxHP", 42),
                }),
            }),
            _ => null,
        };
    }

    // ── P129：驅散玩家 buff（Java applyEffect case 127 → player.dispel）──

    [Fact]
    public void TryUseSkill_Dispel_CancelsControllerTimedSkillBuffs()
    {
        var (field, mob) = Setup([new MobSkillRef(127, 1)]);
        var controller = new Player(new Maple.Core.Characters.Character { Id = 9, Name = "Ctrl" }, new Position(0, 0, 0, 0));
        controller.ApplySkillEffect(TimedBuff(2001002, Maple.Core.Skills.MapleBuffStat.MAGIC_GUARD), T0);
        controller.ApplySkillEffect(TimedBuff(5110001, Maple.Core.Skills.MapleBuffStat.ENERGY_CHARGE), T0);

        var use = Service(Skill(127)).TryUseSkill(field, mob, T0, controller)!;

        Assert.Equal(2001002, Assert.Single(use.DispelledBuffs!).SourceId);
        Assert.DoesNotContain(controller.ActiveBuffs, b => b.SourceId == 2001002);
        Assert.Contains(controller.ActiveBuffs, b => b.SourceId == 5110001); // 能量蓄積不驅散
    }

    private static Maple.Core.Skills.MapleStatEffect TimedBuff(int sourceId, Maple.Core.Skills.MapleBuffStat stat)
        => new()
        {
            SourceId = sourceId,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new Maple.Core.Skills.BuffStatValue(stat, 1)],
        };

    // ── P130：放逐（Java case 129 → changeMapBanish）──

    [Fact]
    public void TryUseSkill_Banish_ReturnsDestinationForController()
    {
        var field = new FieldInstance(100000000);
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 100, Level: 1, Exp: 1, Skills: [new MobSkillRef(129, 1)], Banish: new MobBanishInfo("滾出去", 211000000, "sp")),
            objectId: 100001);
        field.Add(mob);
        var controller = new Player(new Maple.Core.Characters.Character { Id = 9, Name = "Ctrl" }, new Position(0, 0, 0, 0));

        var use = Service(Skill(129)).TryUseSkill(field, mob, T0, controller)!;

        Assert.Equal(new MobBanishInfo("滾出去", 211000000, "sp"), use.Banish);
    }

    [Fact]
    public void TryUseSkill_Banish_MobWithoutBanishInfo_NoBanish()
    {
        var (field, mob) = Setup([new MobSkillRef(129, 1)]);
        var controller = new Player(new Maple.Core.Characters.Character { Id = 9, Name = "Ctrl" }, new Position(0, 0, 0, 0));

        Assert.Null(Service(Skill(129)).TryUseSkill(field, mob, T0, controller)!.Banish);
    }

    [Fact]
    public void MapService_LoadMobStats_ReadsBanInfo()
    {
        var stats = new Maple.Application.Maps.MapService(new BanProvider()).LoadMobStats(100100)!;

        Assert.Equal(new MobBanishInfo("bye", 100000000, "out00"), stats.Banish);
    }

    private sealed class BanProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string path) => (fileName, path) switch
        {
            ("Mob", "0100100.img") => new Node("0100100.img", children: new Dictionary<string, IDataNode>
            {
                ["info"] = new Node("info", children: new Dictionary<string, IDataNode>
                {
                    ["maxHP"] = new Node("maxHP", 10),
                    ["ban"] = new Node("ban", children: new Dictionary<string, IDataNode>
                    {
                        ["banMsg"] = new Node("banMsg", "bye"),
                        ["banMap"] = new Node("banMap", children: new Dictionary<string, IDataNode>
                        {
                            ["0"] = new Node("0", children: new Dictionary<string, IDataNode>
                            {
                                ["field"] = new Node("field", 100000000),
                                ["portal"] = new Node("portal", "out00"),
                            }),
                        }),
                    }),
                }),
            }),
            _ => null,
        };
    }

    // ── P131：怪物毒霧（Java case 131）──

    [Fact]
    public void TryUseSkill_Mist_SpawnsFacingLeftBoxForXTimesTen()
    {
        var (field, mob) = Setup([new MobSkillRef(131, 1)]);
        mob.MoveTo(new Position(100, 50, 4, 1)); // 面向右也照 Java 以面向左計算
        var skill = Skill(131) with { Area = new MobSkillArea(-200, -100, 200, 20) };

        var use = Service(skill).TryUseSkill(field, mob, T0)!;

        var mist = use.SpawnedMist!;
        Assert.Equal((-100, -50, 400, 120), mist.Box);
        Assert.True(mist.IsMobMist);
        Assert.Equal(100100, mist.OwnerId);
        Assert.Equal(T0.AddMilliseconds(100), mist.ExpiresAt); // x=10 → 100ms
        Assert.Same(mist, field.Get(mist.ObjectId));
    }

    [Fact]
    public void TryUseSkill_MistWithoutArea_NoMist()
    {
        var (field, mob) = Setup([new MobSkillRef(131, 1)]);

        Assert.Null(Service(Skill(131)).TryUseSkill(field, mob, T0)!.SpawnedMist);
    }

    private static Mob AddMob(FieldInstance field, int objectId, short x, short y)
    {
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = x, Y = y, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 100, Level: 1, Exp: 1),
            objectId);
        field.Add(mob);
        return mob;
    }

    private static MobSkillService Service(MobSkillDefinition skill)
        => new(new SingleSkillCatalog(skill), new FirstRandom());

    private static MobSkillDefinition Skill(int id, int mpCon = 0, int coolMs = 0, int hpThreshold = 100, int limit = 0)
        => new(id, 1, hpThreshold, mpCon, 10, 1, 30_000, coolMs, 1.0, limit, 0, Array.Empty<int>(), null, false);

    private static (FieldInstance Field, Mob Mob) Setup(IReadOnlyList<MobSkillRef>? skills, int mp = 100, int hp = 100, int maxHp = 100)
    {
        var field = new FieldInstance(100000000);
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, maxHp, MaxMp: mp, Level: 1, Exp: 1, Skills: skills),
            objectId: 100001);
        if (hp < maxHp)
        {
            mob.TakeDamage(maxHp - hp);
        }

        field.Add(mob);
        return (field, mob);
    }

    private sealed class SingleSkillCatalog(MobSkillDefinition skill) : IMobSkillCatalog
    {
        public MobSkillDefinition? Get(int skillId, int level) => skillId == skill.SkillId ? skill : null;
    }

    private sealed class FirstRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }

    private sealed class FakeProvider(string path, IDataNode node) : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => new Node(fileName);

        public IDataNode? GetAt(string fileName, string p) => fileName == "Skill" && p == path ? node : null;
    }

    private sealed class Node(string name, object? value = null, IReadOnlyDictionary<string, IDataNode>? children = null) : IDataNode
    {
        public string Name { get; } = name;

        public object? Value { get; } = value;

        public IReadOnlyDictionary<string, IDataNode> Children { get; } = children ?? new Dictionary<string, IDataNode>();

        public IDataNode? this[string key] => Children.TryGetValue(key, out var child) ? child : null;
    }
}
