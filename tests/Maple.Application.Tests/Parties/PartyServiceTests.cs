using Maple.Application.Parties;
using Maple.Core.Parties;

namespace Maple.Application.Tests.Parties;

public sealed class PartyServiceTests
{
    [Fact]
    public void CreateJoinLeave_TracksMembershipAndBroadcastRecipients()
    {
        var service = CreateService(firstPartyId: 100);

        var create = service.CreateParty(Member(1, "Leader"));
        Assert.True(create.Succeeded);
        Assert.Equal(100, create.Party!.Id);
        Assert.Equal(1, create.Party.LeaderId);

        var join = service.JoinParty(100, Member(2, "Guest"));
        Assert.True(join.Succeeded);
        Assert.Equal(PartyUpdateKind.Join, join.UpdateKind);
        Assert.Equal(new[] { 1, 2 }, join.Recipients);
        Assert.Equal(new[] { 1, 2 }, join.Party!.Members.Select(m => m.CharacterId).ToArray());

        var leave = service.LeaveParty(2);
        Assert.True(leave.Succeeded);
        Assert.Equal(PartyUpdateKind.Leave, leave.UpdateKind);
        Assert.Equal(new[] { 1, 2 }, leave.Recipients);
        Assert.Null(service.GetPartyForCharacter(2));
        Assert.Equal(new[] { 1 }, service.GetParty(100)!.Members.Select(m => m.CharacterId).ToArray());
    }

    [Fact]
    public void JoinParty_RejectsSeventhMember()
    {
        var service = CreateService(firstPartyId: 1);
        service.CreateParty(Member(1, "M1"));

        for (var id = 2; id <= 6; id++)
        {
            Assert.True(service.JoinParty(1, Member(id, $"M{id}")).Succeeded);
        }

        var rejected = service.JoinParty(1, Member(7, "M7"));

        Assert.Equal(PartyCommandStatus.PartyFull, rejected.Status);
        Assert.Equal(6, service.GetParty(1)!.Members.Count);
        Assert.Null(service.GetPartyForCharacter(7));
    }

    [Fact]
    public void LeaderLeave_DisbandsPartyForAllMembers()
    {
        var service = CreateService(firstPartyId: 50);
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(50, Member(2, "Guest"));

        var result = service.LeaveParty(1);

        Assert.True(result.Succeeded);
        Assert.Equal(PartyUpdateKind.Disband, result.UpdateKind);
        Assert.Equal(new[] { 1, 2 }, result.Recipients);
        Assert.Null(service.GetParty(50));
        Assert.Null(service.GetPartyForCharacter(1));
        Assert.Null(service.GetPartyForCharacter(2));
    }

    [Fact]
    public void ChangeLeaderAndExpel_RequireCurrentLeader()
    {
        var service = CreateService(firstPartyId: 10);
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(10, Member(2, "Next"));
        service.JoinParty(10, Member(3, "Guest"));

        var changed = service.ChangeLeader(1, 2);

        Assert.True(changed.Succeeded);
        Assert.Equal(PartyUpdateKind.ChangeLeader, changed.UpdateKind);
        Assert.Equal(2, changed.Party!.LeaderId);
        Assert.Equal(new[] { 1, 2, 3 }, changed.Recipients);

        var oldLeaderExpel = service.ExpelMember(1, 3);
        Assert.Equal(PartyCommandStatus.NotLeader, oldLeaderExpel.Status);

        var expelled = service.ExpelMember(2, 3);
        Assert.True(expelled.Succeeded);
        Assert.Equal(PartyUpdateKind.Expel, expelled.UpdateKind);
        Assert.Equal(new[] { 1, 2, 3 }, expelled.Recipients);
        Assert.Null(service.GetPartyForCharacter(3));
        Assert.Equal(new[] { 1, 2 }, service.GetParty(10)!.Members.Select(m => m.CharacterId).ToArray());
    }

    private static PartyService CreateService(int firstPartyId) =>
        new(new InMemoryPartyRegistry(firstPartyId));

    private static PartyMember Member(int id, string name, int channelIndex = 0, int mapId = 100000000) =>
        new(id, name, Level: 30, JobId: 100, mapId, channelIndex);
}
