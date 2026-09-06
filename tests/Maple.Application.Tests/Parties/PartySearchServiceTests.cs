using Maple.Application.OnlinePlayers;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Application.Tests.Parties;

public sealed class PartySearchServiceTests
{
    [Fact]
    public void TryStartSearch_RejectsNonLeader()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        parties.JoinParty(1, Member(2, "Member"));

        var outcome = service.TryStartSearch(2, leaderLevel: 30, minLevel: 10, maxLevel: 40, memberNum: 4, jobMask: 0x1);

        Assert.False(outcome.Succeeded);
        Assert.Equal(PartySearchStartError.NotLeader, outcome.Error);
    }

    [Theory]
    [InlineData(50, 40, 4, 0x1, PartySearchStartError.MinAboveMax)]
    [InlineData(-1, 40, 4, 0x1, PartySearchStartError.LevelBelowZero)]
    [InlineData(10, 201, 4, 0x1, PartySearchStartError.LevelAboveCap)]
    [InlineData(10, 41, 4, 0x1, PartySearchStartError.RangeTooWide)]
    [InlineData(35, 40, 4, 0x1, PartySearchStartError.SelfOutOfRange)]
    [InlineData(10, 40, 1, 0x1, PartySearchStartError.MemberCountOutOfRange)]
    [InlineData(10, 40, 7, 0x1, PartySearchStartError.MemberCountOutOfRange)]
    [InlineData(10, 40, 4, 0, PartySearchStartError.NoJobSelected)]
    public void TryStartSearch_ValidatesRequest(int minLevel, int maxLevel, int memberNum, int jobMask, PartySearchStartError expected)
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));

        var outcome = service.TryStartSearch(1, leaderLevel: 30, minLevel, maxLevel, memberNum, jobMask);

        Assert.False(outcome.Succeeded);
        Assert.Equal(expected, outcome.Error);
        Assert.NotNull(outcome.RejectionMessage);
    }

    [Fact]
    public void TryStartSearch_RejectsWhenPartyAlreadyAtMemberNum()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        parties.JoinParty(1, Member(2, "M2"));
        parties.JoinParty(1, Member(3, "M3"));

        var outcome = service.TryStartSearch(1, leaderLevel: 30, minLevel: 10, maxLevel: 40, memberNum: 3, jobMask: 0x1);

        Assert.False(outcome.Succeeded);
        Assert.Equal(PartySearchStartError.PartyAlreadyAtSize, outcome.Error);
    }

    [Fact]
    public void TryStartSearch_SucceedsAndReplacesPreviousSearch()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));

        Assert.True(service.TryStartSearch(1, 30, 10, 40, 4, 0x1).Succeeded);
        // Restarting should not throw/duplicate — mirrors Java stopSearch(chr) before re-registering.
        Assert.True(service.TryStartSearch(1, 30, 20, 50, 3, 0x8).Succeeded);
    }

    [Fact]
    public void CheckOnMapEntry_ReturnsNull_WhenCandidateAlreadyInParty()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        service.TryStartSearch(1, 30, 10, 40, 4, 0x1);
        RegisterOnline(online, 1, level: 30, job: 100, mapId: 100000000);
        parties.CreateParty(Member(2, "Candidate"));

        var match = service.CheckOnMapEntry(2, candidateLevel: 30, candidateJob: 100, candidateMapId: 100000000);

        Assert.Null(match);
    }

    [Fact]
    public void CheckOnMapEntry_ReturnsNull_WhenDifferentMap()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        service.TryStartSearch(1, 30, 10, 40, 4, 0x1);
        RegisterOnline(online, 1, level: 30, job: 100, mapId: 100000000);

        var match = service.CheckOnMapEntry(2, candidateLevel: 30, candidateJob: 100, candidateMapId: 999999999);

        Assert.Null(match);
    }

    [Fact]
    public void CheckOnMapEntry_ReturnsNull_WhenLevelOrJobDoesNotMatch()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        service.TryStartSearch(1, 30, 10, 20, 4, 0x8 /* Warrior */);
        RegisterOnline(online, 1, level: 30, job: 100, mapId: 100000000);

        var outOfLevel = service.CheckOnMapEntry(2, candidateLevel: 25, candidateJob: 100, candidateMapId: 100000000);
        var wrongJob = service.CheckOnMapEntry(2, candidateLevel: 15, candidateJob: 200 /* Magician */, candidateMapId: 100000000);

        Assert.Null(outOfLevel);
        Assert.Null(wrongJob);
    }

    [Fact]
    public void CheckOnMapEntry_MatchesEligibleCandidate_AndReturnsSearcherParty()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        service.TryStartSearch(1, leaderLevel: 30, minLevel: 10, maxLevel: 40, memberNum: 4, jobMask: 0x1 /* AllJobs */);
        RegisterOnline(online, 1, level: 30, job: 100, mapId: 100000000);

        var match = service.CheckOnMapEntry(2, candidateLevel: 20, candidateJob: 300, candidateMapId: 100000000);

        Assert.NotNull(match);
        Assert.Equal(1, match!.LeaderId);
    }

    [Fact]
    public void CheckOnMapEntry_StopsSearchWhenReachingMemberNumButStillReturnsMatch()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        service.TryStartSearch(1, leaderLevel: 30, minLevel: 10, maxLevel: 40, memberNum: 2, jobMask: 0x1);
        RegisterOnline(online, 1, level: 30, job: 100, mapId: 100000000);

        var match = service.CheckOnMapEntry(2, candidateLevel: 20, candidateJob: 300, candidateMapId: 100000000);
        Assert.NotNull(match);

        // Search should have been auto-stopped (party would reach memberNum after this invite),
        // so a second candidate should no longer match.
        var second = service.CheckOnMapEntry(3, candidateLevel: 20, candidateJob: 300, candidateMapId: 100000000);
        Assert.Null(second);
    }

    [Fact]
    public void CheckOnMapEntry_ReturnsNull_AndStopsSearch_WhenSearcherPartyAlreadyAtCap()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        parties.JoinParty(1, Member(4, "M4"));
        service.TryStartSearch(1, leaderLevel: 30, minLevel: 10, maxLevel: 40, memberNum: 2, jobMask: 0x1);
        RegisterOnline(online, 1, level: 30, job: 100, mapId: 100000000);

        var match = service.CheckOnMapEntry(2, candidateLevel: 20, candidateJob: 300, candidateMapId: 100000000);

        Assert.Null(match);
    }

    [Fact]
    public void StopSearch_RemovesActiveSearch()
    {
        var (service, parties, online) = CreateHarness();
        parties.CreateParty(Member(1, "Leader"));
        service.TryStartSearch(1, 30, 10, 40, 4, 0x1);
        RegisterOnline(online, 1, level: 30, job: 100, mapId: 100000000);

        service.StopSearch(1);

        var match = service.CheckOnMapEntry(2, candidateLevel: 20, candidateJob: 300, candidateMapId: 100000000);
        Assert.Null(match);
    }

    private static (PartySearchService Service, IPartyRegistry Parties, IOnlinePlayerRegistry Online) CreateHarness()
    {
        var parties = new InMemoryPartyRegistry(firstPartyId: 1);
        var online = new InMemoryOnlinePlayerRegistry();
        var registry = new InMemoryPartySearchRegistry();
        return (new PartySearchService(registry, parties, online), parties, online);
    }

    private static void RegisterOnline(IOnlinePlayerRegistry online, int characterId, int level, int job, int mapId)
    {
        var character = new Character { Id = characterId, Name = $"C{characterId}", Level = (byte)level, Job = (short)job, MapId = mapId };
        var player = new Player(character, new Position(0, 0, 0, 0));
        online.Register(player, channel: 1, SendNoop, new object());
    }

    private static PartyMember Member(int id, string name) =>
        new(id, name, Level: 30, JobId: 100, MapId: 100000000, ChannelIndex: 0);

    private static Task SendNoop(byte[] packet, CancellationToken ct) => Task.CompletedTask;
}
