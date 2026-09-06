using Maple.Application.Parties;

namespace Maple.Application.Tests.Parties;

public sealed class PartySearchCriteriaTests
{
    private static PartySearchCriteria Criteria(int jobMask) => new(MinLevel: 1, MaxLevel: 200, MemberNum: 6, JobMask: jobMask);

    [Fact]
    public void AllJobs_AllowsAnyJob()
    {
        var criteria = Criteria((int)PartySearchJobFilter.AllJobs);

        Assert.True(criteria.AllowsJob(0));
        Assert.True(criteria.AllowsJob(122));
        Assert.True(criteria.AllowsJob(2112));
    }

    [Fact]
    public void Beginner_ExcludesWildHunterEvenThoughWildHunterStartsAsBeginner()
    {
        var criteria = Criteria((int)PartySearchJobFilter.Beginner);

        Assert.True(criteria.AllowsJob(0));
        Assert.False(criteria.AllowsJob(2000)); // 狂狼勇士 job%1000==0 → IsBeginner true, but WildHunter carve-out excludes it
    }

    [Fact]
    public void Warrior_ExcludesWildHunter()
    {
        var criteria = Criteria((int)PartySearchJobFilter.Warrior);

        Assert.True(criteria.AllowsJob(100));
        Assert.False(criteria.AllowsJob(2100));
    }

    [Fact]
    public void Knight_MatchesBothPaladinAndDarkKnight()
    {
        var criteria = Criteria((int)PartySearchJobFilter.Knight);

        Assert.True(criteria.AllowsJob(122)); // 聖騎士
        Assert.True(criteria.AllowsJob(132)); // 黑騎士
        Assert.False(criteria.AllowsJob(112)); // 英雄
    }

    [Fact]
    public void UnusedDragonKnightBit_MatchesNothing()
    {
        // 0x40（Java 標籤「龍騎士」）在 Java checkJob 從未被檢查——原樣保留該死 bit。
        var criteria = Criteria(0x40);

        Assert.False(criteria.AllowsJob(132));
        Assert.False(criteria.AllowsJob(122));
    }

    [Fact]
    public void CygnusAndWildHunterFilters_MatchTheirCustomJobIds()
    {
        Assert.True(Criteria((int)PartySearchJobFilter.DawnWarrior).AllowsJob(1112));
        Assert.True(Criteria((int)PartySearchJobFilter.BlazeWizard).AllowsJob(1212));
        Assert.True(Criteria((int)PartySearchJobFilter.WindArcher).AllowsJob(1312));
        Assert.True(Criteria((int)PartySearchJobFilter.NightWalker).AllowsJob(1412));
        Assert.True(Criteria((int)PartySearchJobFilter.ThunderBreaker).AllowsJob(1512));
        Assert.True(Criteria((int)PartySearchJobFilter.WildHunter).AllowsJob(2112));
    }

    [Fact]
    public void CombinedMask_MatchesEitherFlag()
    {
        var criteria = Criteria((int)(PartySearchJobFilter.Warrior | PartySearchJobFilter.Magician));

        Assert.True(criteria.AllowsJob(100));
        Assert.True(criteria.AllowsJob(200));
        Assert.False(criteria.AllowsJob(300));
    }
}
