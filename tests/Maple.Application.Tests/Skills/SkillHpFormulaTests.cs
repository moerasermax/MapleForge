using Maple.Application.Combat;
using Maple.Application.Maps;
using Maple.Application.Parties;
using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.Parties;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

/// <summary>P146：治癒術／氣絕的 HP 公式（Java calcHPChange、makeHealHP）與治癒術打非不死怪中止。</summary>
public sealed class SkillHpFormulaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.0, 900)]     // (int)(100*3*3) = 900
    [InlineData(0.9999, 1500)] // (int)(100*5*3) = 1500
    public void MakeHealHp_RangeLowerToUpper(double roll, int expected)
        => Assert.Equal(expected, SkillHpFormula.MakeHealHp(3.0, 100, 3, 5, new FixedRandom(roll)));

    [Fact]
    public void SkillHp_UsesTotalMagic_AndZombifyNegates()
    {
        var player = MakePlayer();
        var stats = new PlayerLocalStats(4, 4, 100, 4, 0, 200, 100, 100, 100, 100, 108, 1f);
        var heal = new MapleStatEffect { SourceId = 2301002, Hp = 300 };

        Assert.Equal(1800, SkillHpFormula.CalcHpChange(player, heal, primary: true, stats, new FixedRandom(0)));

        player.TryGiveDisease(PlayerDisease.Zombify, 50, 60_000, 133, 1, T0);
        Assert.Equal(-1800, SkillHpFormula.CalcHpChange(player, heal, primary: true, stats, new FixedRandom(0)));
    }

    [Fact]
    public void Chakra_OverwritesWithLukDexFormula()
    {
        var player = MakePlayer();
        player.Character.Stats.Luk = 200;
        player.Character.Stats.Dex = 50;
        var chakra = new MapleStatEffect { SourceId = SkillHpFormula.ChakraSkillId, Y = 50, HpCon = 999 };

        // v38 = (int)(0*100+1)+100 = 101；(101*200*0.033 + 50) * 150 * 0.002 = 214.98 → 214；
        // makeHealHP(0.5, 200, 2.3, 3.5) 下限 (int)(200*2.3*0.5) = (int)229.99999999999997 = 229（double 截斷，同 Java）
        Assert.Equal(214 + 229, SkillHpFormula.CalcHpChange(player, chakra, primary: true, fromStats: null, new FixedRandom(0)));
    }

    [Fact]
    public void Cast_Heal_HealsCaster()
    {
        var heal = new MapleSkill { Id = 2301002, Effects = [new MapleStatEffect { SourceId = 2301002, Level = 1, Hp = 300, MpCon = 10 }] };
        var player = MakePlayer();
        player.ChangeSkillLevel(2301002, 1, 1);
        player.Character.Stats.Hp = 100;

        var result = new SkillService(new InMemorySkillCatalog([heal]), new FixedRandom(0)).Cast(player, 2301002, 1, T0);

        Assert.Equal(SkillCastStatus.Success, result.Status);
        Assert.Equal((short)(100 + 900), player.Hp); // 總魔攻 = INT 100 → 下限 900
    }

    [Fact]
    public void PartyHeal_UsesCasterMagic()
    {
        var parties = new InMemoryPartyRegistry();
        var field = new FieldInstance(100000000);
        var caster = MakePlayer(1);
        var member = MakePlayer(2);
        member.Character.Stats.Hp = 100;
        var created = parties.CreateParty(PartyMember.FromCharacter(caster.Character, channelIndex: 1));
        parties.JoinParty(created.Party!.Id, PartyMember.FromCharacter(member.Character, channelIndex: 1));
        field.Add(caster);
        field.Add(member);
        var heal = new MapleStatEffect { SourceId = 2301002, Level = 1, Hp = 300, Area = new MobSkillArea(-200, -100, 200, 100) };
        var service = new PartyBuffService(parties, new FixedRandom(0), new PlayerLocalStatsCalculator(new InMemorySkillCatalog([])));

        Assert.Single(service.Apply(field, caster, heal, facingLeft: true, T0));

        Assert.Equal((short)1000, member.Hp); // 100 + 900，夾在 1000
    }

    [Fact]
    public void HealMagicAttack_NonUndeadMob_Aborts_UndeadIsHit()
    {
        var service = new CombatService(new MapService(new EmptyDataProvider()));
        var field = new FieldInstance(100000100);
        var player = MakePlayer();
        var normal = MakeMob(100001, undead: false);
        var undead = MakeMob(100002, undead: true);
        field.Add(player);
        field.Add(normal);
        field.Add(undead);

        var aborted = service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(normal.ObjectId, [10])], null, 2301002, IsMagic: true));
        var hit = service.ApplyAttack(field, player, new CombatAttack([new CombatAttackTarget(undead.ObjectId, [10])], null, 2301002, IsMagic: true));

        Assert.True(aborted.Aborted);
        Assert.Equal(100, normal.Hp);
        Assert.False(hit.Aborted);
        Assert.Equal(90, undead.Hp);
    }

    private static Player MakePlayer(int id = 1)
    {
        var player = new Player(new Character { Id = id, Name = $"C{id}", Job = 230, Level = 50, MapId = 100000000 }, new Position(0, 0, 0, 0));
        player.Character.Stats.MaxHp = 1000;
        player.Character.Stats.Hp = 500;
        player.Character.Stats.MaxMp = 1000;
        player.Character.Stats.Mp = 500;
        player.Character.Stats.Int = 100;
        return player;
    }

    private static Mob MakeMob(int objectId, bool undead)
        => new(
            new MapMonster { MonsterId = 100100, X = 0, Y = 0, Fh = 1 },
            new MobStats(100100, 100, MaxMp: 0, Level: 1, Exp: 1, Undead: undead),
            objectId);

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;

        public override int Next(int maxValue) => 0;
    }

    private sealed class EmptyDataProvider : IDataProvider
    {
        public IDataNode GetRoot(string fileName) => throw new NotSupportedException();

        public IDataNode? GetAt(string fileName, string path) => null;
    }
}
