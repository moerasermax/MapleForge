using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.Stats;

public sealed class PlayerStatsTests
{
    [Fact]
    public void DistributeAbilityPoint_IncrementsStatAndConsumesAp()
    {
        var player = MakePlayer(ap: 2);

        var result = player.DistributeAbilityPoint(AbilityPointTarget.Str);

        Assert.True(result.Applied);
        Assert.Equal((short)13, player.Character.Stats.Str);
        Assert.Equal((short)1, player.Character.RemainingAp);
        Assert.Contains(result.Updates, u => u.Kind == PlayerStatKind.Str && u.Value == 13);
        Assert.Contains(result.Updates, u => u.Kind == PlayerStatKind.AvailableAp && u.Value == 1);
    }

    [Fact]
    public void DistributeAbilityPoint_RejectsWhenStatAtJavaCap()
    {
        var player = MakePlayer(ap: 1);
        player.Character.Stats.Dex = 999;

        var result = player.DistributeAbilityPoint(AbilityPointTarget.Dex);

        Assert.Equal(PlayerStatsFailure.StatLimitReached, result.Failure);
        Assert.Equal((short)999, player.Character.Stats.Dex);
        Assert.Equal((short)1, player.Character.RemainingAp);
    }

    [Fact]
    public void DistributeSkillPoint_ConsumesRemainingSpAndPersistsSkillLevel()
    {
        var player = MakePlayer(sp: 3, job: 100);

        var result = player.DistributeSkillPoint(1000000);

        Assert.True(result.Applied);
        Assert.Equal((short)2, player.Character.RemainingSp);
        Assert.Equal(1000000, result.SkillId);
        Assert.Equal((byte)1, result.SkillLevel);
        Assert.Equal(1, player.GetSkillLevel(1000000));
        Assert.Contains(result.Updates, u => u.Kind == PlayerStatKind.AvailableSp && u.Value == 2);
    }

    [Fact]
    public void DistributeBeginnerSkillPoint_UsesBeginnerPoolWithoutRemainingSp()
    {
        var player = MakePlayer(level: 4, sp: 0, job: 0);

        var result = player.DistributeSkillPoint(1000);

        Assert.True(result.Applied);
        Assert.Equal((short)0, player.Character.RemainingSp);
        Assert.Equal(1, player.GetSkillLevel(1000));
        Assert.Empty(result.Updates);
    }

    [Fact]
    public void GainExperience_LevelUpBeginnerConvertsApToStrAndRefreshesVitals()
    {
        var player = MakePlayer(level: 1, exp: 14, job: 0);

        var result = player.GainExperience(1, RollMin);

        Assert.True(result.Applied);
        Assert.Equal((byte)2, player.Character.Level);
        Assert.Equal(0, player.Character.Exp);
        Assert.Equal((short)17, player.Character.Stats.Str);
        Assert.Equal((short)0, player.Character.RemainingAp);
        Assert.Equal((short)62, player.MaxHp);
        Assert.Equal((short)62, player.Hp);
        Assert.Equal((short)15, player.MaxMp);
        Assert.Equal((short)15, player.Mp);
        Assert.Contains(result.Updates, u => u.Kind == PlayerStatKind.Level && u.Value == 2);
        Assert.Contains(result.Updates, u => u.Kind == PlayerStatKind.Exp && u.Value == 0);
        Assert.Contains(result.Updates, u => u.Kind == PlayerStatKind.Str && u.Value == 17);
    }

    [Fact]
    public void GainExperience_LevelUpNonBeginnerGrantsSpAndUsesCombatVitalsFields()
    {
        var player = MakePlayer(level: 10, exp: 1700, job: 100);

        var result = player.GainExperience(16, RollMax);

        Assert.True(result.Applied);
        Assert.Equal((byte)11, player.Character.Level);
        Assert.Equal((short)5, player.Character.RemainingAp);
        Assert.Equal((short)3, player.Character.RemainingSp);
        Assert.Equal((short)78, player.MaxHp);
        Assert.Equal((short)78, player.Hp);
        Assert.Equal((short)11, player.MaxMp);
        Assert.Equal((short)11, player.Mp);
        Assert.Contains(result.Updates, u => u.Kind == PlayerStatKind.AvailableSp && u.Value == 3);
    }

    [Fact]
    public void RecoverOverTime_HealsHpMpThroughSameVitalsAndRateLimits()
    {
        var player = MakePlayer(hp: 20, maxHp: 50, mp: 1, maxMp: 5);

        var first = player.RecoverOverTime(10, 3, nowUnixMilliseconds: 1000);
        Assert.True(first.Applied);
        Assert.Equal((short)30, player.Hp);
        Assert.Equal((short)4, player.Mp);

        var second = player.RecoverOverTime(10, 3, nowUnixMilliseconds: 1500);
        Assert.Equal(PlayerStatsFailure.NoChange, second.Failure);
        Assert.Equal((short)30, player.Hp);
        Assert.Equal((short)4, player.Mp);

        var third = player.RecoverOverTime(100, 100, nowUnixMilliseconds: 2100);
        Assert.True(third.Applied);
        Assert.Equal(player.MaxHp, player.Hp);
        Assert.Equal(player.MaxMp, player.Mp);
    }

    private static int RollMin(int min, int max) => min;

    private static int RollMax(int min, int max) => max;

    private static Player MakePlayer(
        byte level = 1,
        int exp = 0,
        short job = 0,
        short ap = 0,
        short sp = 0,
        short hp = 50,
        short maxHp = 50,
        short mp = 5,
        short maxMp = 5)
    {
        var chr = new Character
        {
            Id = 1,
            Name = "Tester",
            Level = level,
            Job = job,
            Exp = exp,
            RemainingAp = ap,
            RemainingSp = sp,
            Stats = new CharacterStats
            {
                Str = 12,
                Dex = 5,
                Int = 4,
                Luk = 4,
                Hp = hp,
                MaxHp = maxHp,
                Mp = mp,
                MaxMp = maxMp,
            },
        };

        return new Player(chr, new Position(0, 0, 0, 0));
    }
}
