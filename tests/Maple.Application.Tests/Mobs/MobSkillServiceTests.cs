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
        var (field, mob) = Setup([new MobSkillRef(100, 1)]);
        var service = Service(Skill(100, coolMs: 10_000));

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
