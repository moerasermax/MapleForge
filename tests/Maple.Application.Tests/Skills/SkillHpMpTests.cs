using Maple.Application.Skills;
using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Skills;

/// <summary>P133/P134：applyTo 的 HP/MP 結算（攻擊尾段與 SPECIAL_MOVE 共用）（對照 Java MapleStatEffect.applyTo、calcHPChange、calcMPChange）。</summary>
public sealed class SkillHpMpTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MpCon_IsDeducted_AndUpdatesCarryMpThenHp()
    {
        var (service, player) = Setup(Skill(2101004, mpCon: 30));

        var cost = service.ApplyAttackSkillCost(player, 2101004, targetCount: 1, isMagic: true)!;

        Assert.True(cost.Applied);
        Assert.Equal((short)70, player.Mp);
        Assert.Equal(
            [new PlayerStatUpdate(PlayerStatKind.Mp, 70), new PlayerStatUpdate(PlayerStatKind.Hp, 500)],
            cost.Updates);
    }

    [Fact]
    public void NotEnoughMp_ReturnsNotApplied_WithoutChangingMp()
    {
        var (service, player) = Setup(Skill(2101004, mpCon: 30));
        player.Character.Stats.Mp = 20;

        var cost = service.ApplyAttackSkillCost(player, 2101004, 1, isMagic: true)!;

        Assert.False(cost.Applied);
        Assert.Empty(cost.Updates);
        Assert.Equal((short)20, player.Mp);
    }

    [Fact]
    public void ZeroCostSkill_StillSendsHpUpdate()
    {
        var (service, player) = Setup(Skill(1001004));

        var cost = service.ApplyAttackSkillCost(player, 1001004, 1, isMagic: false)!;

        Assert.True(cost.Applied);
        Assert.Equal([new PlayerStatUpdate(PlayerStatKind.Hp, 500)], cost.Updates);
    }

    [Fact]
    public void ElementalAmplification_MultipliesMpCon()
    {
        var amp = new MapleSkill { Id = 2110001, Effects = [new MapleStatEffect { SourceId = 2110001, Level = 1, X = 150 }] };
        var (service, player) = Setup(Skill(2121003, mpCon: 30), amp);
        player.Character.Job = 212;
        player.ChangeSkillLevel(2110001, 1, 1);

        service.ApplyAttackSkillCost(player, 2121003, 1, isMagic: true);

        Assert.Equal((short)55, player.Mp); // 30 * 1.5 = 45
    }

    [Fact]
    public void ElementalAmplification_IgnoredForOtherJobs()
    {
        var amp = new MapleSkill { Id = 2110001, Effects = [new MapleStatEffect { SourceId = 2110001, Level = 1, X = 150 }] };
        var (service, player) = Setup(Skill(2121003, mpCon: 30), amp);
        player.Character.Job = 222;
        player.ChangeSkillLevel(2110001, 1, 1);

        service.ApplyAttackSkillCost(player, 2121003, 1, isMagic: true);

        Assert.Equal((short)70, player.Mp);
    }

    [Fact]
    public void Concentrate_ReducesMpConBySkillX()
    {
        var concentrate = new MapleStatEffect
        {
            SourceId = 3121008,
            Level = 1,
            X = 20,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new BuffStatValue(MapleBuffStat.CONCENTRATE, 20)],
        };
        var (service, player) = Setup(Skill(3121004, mpCon: 31), new MapleSkill { Id = 3121008, Effects = [concentrate] });
        player.ApplySkillEffect(concentrate, T0);
        var mpAfterBuff = player.Mp;

        service.ApplyAttackSkillCost(player, 3121004, 1, isMagic: false);

        Assert.Equal(mpAfterBuff - 25, player.Mp); // 31 - 31*20/100(=6)
    }

    [Fact]
    public void Infinity_ZeroesMpChange()
    {
        var infinity = new MapleStatEffect
        {
            SourceId = 2121004,
            Level = 1,
            IsOverTime = true,
            DurationMilliseconds = 60_000,
            Statups = [new BuffStatValue(MapleBuffStat.INFINITY, 1)],
        };
        var (service, player) = Setup(Skill(2121003, mpCon: 30));
        player.ApplySkillEffect(infinity, T0);

        var cost = service.ApplyAttackSkillCost(player, 2121003, 1, isMagic: true)!;

        Assert.Equal((short)100, player.Mp);
        Assert.Equal([new PlayerStatUpdate(PlayerStatKind.Hp, 500)], cost.Updates);
    }

    [Fact]
    public void HpRateAndHpCon_AreDeducted()
    {
        // 龍咆哮：Java hpR = -x/100（WzSkillCatalog 已換算成 HpRate）。
        var (service, player) = Setup(Skill(1311006, hpRate: -0.1, hpCon: 5));

        service.ApplyAttackSkillCost(player, 1311006, 1, isMagic: false);

        Assert.Equal((short)(500 - 100 - 5), player.Hp);
    }

    [Fact]
    public void NotEnoughHp_ReturnsNotApplied()
    {
        var (service, player) = Setup(Skill(1311006, hpCon: 50));
        player.Character.Stats.Hp = 50;

        var cost = service.ApplyAttackSkillCost(player, 1311006, 1, isMagic: false)!;

        Assert.False(cost.Applied);
        Assert.Equal((short)50, player.Hp);
    }

    [Theory]
    [InlineData(0, 1, false, false)]
    [InlineData(21101003, 1, false, false)]
    [InlineData(5110001, 1, false, false)]
    [InlineData(4331003, 0, false, false)]
    [InlineData(4331003, 1, false, true)]
    [InlineData(2301002, 1, true, false)]
    [InlineData(2101004, 0, true, true)]
    [InlineData(1001004, 0, false, true)]
    public void AppliesAttackEffect_MatchesJavaConditions(int skillId, int targets, bool isMagic, bool expected)
        => Assert.Equal(expected, SkillService.AppliesAttackEffect(skillId, targets, isMagic));

    [Fact]
    public void UnlearnedSkill_ReturnsNull()
    {
        var (service, player) = Setup(Skill(2101004, mpCon: 30));

        Assert.Null(service.ApplyAttackSkillCost(player, 2111002, 1, isMagic: true));
        Assert.Equal((short)100, player.Mp);
    }

    [Fact]
    public void Cast_UsesSameMpFormula_ElementalAmplification()
    {
        var amp = new MapleSkill { Id = 2110001, Effects = [new MapleStatEffect { SourceId = 2110001, Level = 1, X = 150 }] };
        var (service, player) = Setup(Skill(2111005, mpCon: 20), amp);
        player.Character.Job = 211;
        player.ChangeSkillLevel(2110001, 1, 1);

        var result = service.Cast(player, 2111005, 1, T0);

        Assert.Equal(SkillCastStatus.Success, result.Status);
        Assert.Equal((short)70, player.Mp); // 20 * 1.5
    }

    [Fact]
    public void Cast_NotEnoughMp_DoesNotApplyBuff()
    {
        var buff = new MapleSkill
        {
            Id = 2001002,
            Effects =
            [
                new MapleStatEffect
                {
                    SourceId = 2001002,
                    Level = 1,
                    MpCon = 200,
                    IsOverTime = true,
                    DurationMilliseconds = 60_000,
                    Statups = [new BuffStatValue(MapleBuffStat.MAGIC_GUARD, 10)],
                },
            ],
        };
        var (service, player) = Setup(buff);

        var result = service.Cast(player, 2001002, 1, T0);

        Assert.Equal(SkillCastStatus.NotEnoughMp, result.Status);
        Assert.Null(result.AppliedBuff);
        Assert.Null(player.GetBuffValue(MapleBuffStat.MAGIC_GUARD));
    }

    [Fact]
    public void Cast_MpRecovery_TradesTenPercentBaseMaxHpForMp()
    {
        var skill = new MapleSkill { Id = 5101005, Effects = [new MapleStatEffect { SourceId = 5101005, Level = 1, Y = 300 }] };
        var (service, player) = Setup(skill);

        Assert.Equal(SkillCastStatus.Success, service.Cast(player, 5101005, 1, T0).Status);

        Assert.Equal((short)400, player.Hp);  // 1000/100*10 = 100
        Assert.Equal((short)400, player.Mp);  // 100 + (100/100)*300
    }

    [Fact]
    public void Cast_MpRecovery_LowHp_CopiesJavaQuirk()
    {
        var skill = new MapleSkill { Id = 5101005, Effects = [new MapleStatEffect { SourceId = 5101005, Level = 1, Y = 300 }] };
        var (service, player) = Setup(skill);
        player.Character.Stats.Hp = 50;

        service.Cast(player, 5101005, 1, T0);

        // Java：hpchange = hp - 1（正值）→ HP 反而 +49；MP 不變。
        Assert.Equal((short)99, player.Hp);
        Assert.Equal((short)100, player.Mp);
    }

    private static MapleSkill Skill(int id, short mpCon = 0, double hpRate = 0, short hpCon = 0)
        => new()
        {
            Id = id,
            Effects = [new MapleStatEffect { SourceId = id, Level = 1, MpCon = mpCon, HpRate = hpRate, HpCon = hpCon }],
        };

    private static (SkillService Service, Player Player) Setup(MapleSkill attack, params MapleSkill[] extra)
    {
        var player = new Player(new Character { Id = 1, Name = "Atk", Job = 200 }, new Position(0, 0, 0, 0));
        player.Character.Stats.MaxHp = 1000;
        player.Character.Stats.Hp = 500;
        player.Character.Stats.MaxMp = 1000;
        player.Character.Stats.Mp = 100;
        player.ChangeSkillLevel(attack.Id, 1, 1);
        return (new SkillService(new InMemorySkillCatalog([attack, .. extra])), player);
    }
}
