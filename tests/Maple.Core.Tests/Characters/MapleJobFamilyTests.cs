using Maple.Core.Characters;

namespace Maple.Core.Tests.Characters;

public sealed class MapleJobFamilyTests
{
    [Theory]
    [InlineData(0, true)]      // 初心者
    [InlineData(2001, true)]   // 弓箭手見習
    [InlineData(3001, true)]   // 盜賊見習
    [InlineData(4001, true)]   // 海盜見習
    [InlineData(6000, true)]   // noblesse beginner
    [InlineData(13000, true)]  // aran beginner
    [InlineData(1000, true)]   // job%1000==0 → 皇家騎士團 beginner
    [InlineData(2000, true)]   // job%1000==0 → 狂狼勇士 beginner
    [InlineData(100, false)]   // 劍士（非初心者）
    [InlineData(122, false)]   // 聖騎士
    public void IsBeginner_MatchesJavaOracle(int job, bool expected)
    {
        Assert.Equal(expected, MapleJobFamily.IsBeginner(job));
    }

    [Fact]
    public void BranchPredicates_MatchVanillaJobIds()
    {
        Assert.True(MapleJobFamily.IsWarrior(100));
        Assert.True(MapleJobFamily.IsMagician(200));
        Assert.True(MapleJobFamily.IsBowman(300));
        Assert.True(MapleJobFamily.IsThief(400));
        Assert.True(MapleJobFamily.IsPirate(500));
        Assert.True(MapleJobFamily.IsThief(600));  // getJobBranch==6 counts as both thief and pirate
        Assert.True(MapleJobFamily.IsPirate(600));
    }

    [Theory]
    [InlineData(112, true)]  // 英雄
    [InlineData(122, false)]
    public void IsHero_MatchesJobId(int job, bool expected) => Assert.Equal(expected, MapleJobFamily.IsHero(job));

    [Fact]
    public void ThirdJobPredicates_MatchVanillaJobIds()
    {
        Assert.True(MapleJobFamily.IsPaladin(122));
        Assert.True(MapleJobFamily.IsDarkKnight(132));
        Assert.True(MapleJobFamily.IsFpArchMage(212));
        Assert.True(MapleJobFamily.IsIlArchMage(222));
        Assert.True(MapleJobFamily.IsBishop(232));
        Assert.True(MapleJobFamily.IsBowmaster(312));
        Assert.True(MapleJobFamily.IsMarksman(322));
        Assert.True(MapleJobFamily.IsNightLord(412));
        Assert.True(MapleJobFamily.IsShadower(422));
        Assert.True(MapleJobFamily.IsBuccaneer(512));
        Assert.True(MapleJobFamily.IsCorsair(522));
    }

    [Fact]
    public void CygnusAndWildHunter_MatchCustomJobIds()
    {
        Assert.True(MapleJobFamily.IsDawnWarrior(1100));
        Assert.True(MapleJobFamily.IsDawnWarrior(1112));
        Assert.True(MapleJobFamily.IsBlazeWizard(1212));
        Assert.True(MapleJobFamily.IsWindArcher(1312));
        Assert.True(MapleJobFamily.IsNightWalker(1412));
        Assert.True(MapleJobFamily.IsThunderBreaker(1512));
        Assert.True(MapleJobFamily.IsWildHunter(2000));
        Assert.True(MapleJobFamily.IsWildHunter(2112));
    }
}
