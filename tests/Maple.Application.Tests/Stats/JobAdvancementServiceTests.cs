using Maple.Application.Skills;
using Maple.Application.Stats;
using Maple.Core.Characters;
using Maple.Core.Skills;
using Maple.Core.World;

namespace Maple.Application.Tests.Stats;

/// <summary>P167：轉職（對照 Java MapleCharacter.changeJob／resetStats／resetStatsByJob／baseSkills）。</summary>
public sealed class JobAdvancementServiceTests
{
    [Fact]
    public void FirstJobWarrior_ResetsStats_GrantsSp_AddsHp()
    {
        var player = MakePlayer(level: 10, job: 0);
        var result = new JobAdvancementService(random: new MinRandom()).ChangeJob(player, 100);

        Assert.False(result.AbnormalLevel);
        var stats = player.Character.Stats;
        Assert.Equal((25, 4, 4, 4), ((int)stats.Str, (int)stats.Dex, (int)stats.Int, (int)stats.Luk));
        Assert.Equal(12 + 5 + 4 + 4 + 0 - 37, player.Character.RemainingAp); // total − 四素質
        Assert.Equal(1, player.Character.RemainingSp);
        Assert.Equal((short)(50 + 200), player.Character.Stats.MaxHp);
        Assert.Equal(player.Character.Stats.MaxHp, player.Character.Stats.Hp);
        Assert.Equal([new PlayerStatUpdate(PlayerStatKind.AvailableSp, 1), new PlayerStatUpdate(PlayerStatKind.Job, 100)], result.SpAndJob);
        Assert.Equal(5, result.ResetStats.Count);
    }

    [Fact]
    public void FourthJob_GrantsThreeSp_AndBaseSkillMasterLevels()
    {
        var fourth = new MapleSkill { Id = 1120004, MasterLevel = 10, Effects = [new MapleStatEffect { SourceId = 1120004, Level = 1 }] };
        var hidden = new MapleSkill { Id = 1121000, MasterLevel = 10, IsInvisible = true };
        var player = MakePlayer(level: 120, job: 111);
        var service = new JobAdvancementService(new InMemorySkillCatalog([fourth, hidden]), new MinRandom());

        var result = service.ChangeJob(player, 112);

        Assert.Equal(3, player.Character.RemainingSp);
        Assert.Empty(result.ResetStats); // 非一轉不重設
        Assert.Equal([(1120004, (byte)0, (byte)10)], result.BaseSkills);
        Assert.Equal(10, player.GetMasterLevel(1120004));
    }

    [Fact]
    public void ThirdJobBowman_AddsHpTwice_AbnormalLevelFlagged()
    {
        var player = MakePlayer(level: 71, job: 310);
        var result = new JobAdvancementService(random: new MinRandom()).ChangeJob(player, 311);
        Assert.True(result.AbnormalLevel);
        Assert.Equal((short)50, player.Character.Stats.MaxHp); // 311 不在 HP 表

        var bowman = MakePlayer(level: 30, job: 300);
        new JobAdvancementService(random: new MinRandom()).ChangeJob(bowman, 310);
        Assert.Equal((short)(50 + 300 + 150), bowman.Character.Stats.MaxHp);
    }

    private static Player MakePlayer(byte level, short job)
    {
        var chr = new Character { Id = 1, Name = "Job", Level = level, Job = job };
        (chr.Stats.Str, chr.Stats.Dex, chr.Stats.Int, chr.Stats.Luk) = (12, 5, 4, 4);
        (chr.Stats.MaxHp, chr.Stats.Hp, chr.Stats.MaxMp, chr.Stats.Mp) = (50, 50, 5, 5);
        return new Player(chr, new Position(0, 0, 0, 0));
    }

    private sealed class MinRandom : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
    }
}
