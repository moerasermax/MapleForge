using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Combat;

/// <summary>P143：魔力吸收（對照 Java MapleStatEffect.applyPassive、MapleMonster.absorbMP/canAbsorbMP）。</summary>
public sealed class MpEaterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MagicHit_AbsorbsMp_AndReportsSkill()
    {
        var (service, field, player, mob) = Setup(time: new FixedTime(T0));

        var hit = Assert.Single(Attack(service, field, player, mob).Hits);

        // min((int)(700 * 20 / 70.0), 700) = 200
        Assert.Equal(2100000, hit.MpEaterSkillId);
        Assert.Equal(500, mob.Mp);
        Assert.Equal((short)300, player.Mp);
    }

    [Fact]
    public void SecondHitWithinTenSeconds_DoesNotAbsorb()
    {
        var (service, field, player, mob) = Setup(time: new FixedTime(T0));
        Attack(service, field, player, mob);

        var hit = Assert.Single(Attack(service, field, player, mob).Hits);

        Assert.Equal(0, hit.MpEaterSkillId);
        Assert.Equal(500, mob.Mp);
    }

    [Fact]
    public void Boss_IsNotAbsorbed()
    {
        var (service, field, player, mob) = Setup(boss: true);

        Assert.Equal(0, Assert.Single(Attack(service, field, player, mob).Hits).MpEaterSkillId);
        Assert.Equal(700, mob.Mp);
    }

    [Fact]
    public void AbsorbedMp_ClampedToCurrentMaxMp()
    {
        var (service, field, player, mob) = Setup();
        player.Character.Stats.Mp = 950;

        Attack(service, field, player, mob);

        Assert.Equal((short)1000, player.Mp);
    }

    [Fact]
    public void ClericUsesOwnEaterSkill_PhysicalAttackNever()
    {
        var (service, field, player, mob) = Setup(job: 231, eaterId: 2300000);
        Assert.Equal(2300000, Assert.Single(Attack(service, field, player, mob).Hits).MpEaterSkillId);

        var (service2, field2, player2, mob2) = Setup();
        var physical = service2.ApplyAttack(field2, player2, new CombatAttack([new CombatAttackTarget(mob2.ObjectId, [10])], null, 2001004));
        Assert.Equal(0, Assert.Single(physical.Hits).MpEaterSkillId);
    }

    private static CombatAttackResult Attack(CombatService service, FieldInstance field, Player player, Mob mob)
        => service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(mob.ObjectId, [10])], null, 2001004, IsMagic: true));

    private static (CombatService, FieldInstance, Player, Mob) Setup(bool boss = false, int job = 211, int eaterId = 2100000, TimeProvider? time = null)
    {
        var catalog = new InMemorySkillCatalog(
            [new MapleSkill { Id = eaterId, Effects = [new MapleStatEffect { SourceId = eaterId, Level = 1, X = 20, Prop = 100 }] }]);
        var service = new CombatService(new MapService(new EmptyDataProvider()), timeProvider: time, skills: catalog);
        var field = new FieldInstance(100000100);
        var player = new Player(new Character { Id = 1, Name = "Mage", Job = (short)job, Level = 70 }, new Position(0, 0, 0, 0));
        player.Character.Stats.MaxMp = 1000;
        player.Character.Stats.Mp = 100;
        player.ChangeSkillLevel(eaterId, 1, 1);
        var mob = new Mob(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 10_000, MaxMp: 700, Level: 1, Exp: 1, Boss: boss),
            100001);
        field.Add(player);
        field.Add(mob);
        return (service, field, player, mob);
    }

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
