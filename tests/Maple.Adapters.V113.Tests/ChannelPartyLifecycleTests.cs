using Maple.Adapters.V113.Channel;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

/// <summary>對照 Java <c>InterServerHandler</c>（登入 LOG_ONOFF）與 <c>MapleClient.disconnect</c>（登出 LOG_ONOFF + 隊長斷線轉移）。</summary>
public sealed class ChannelPartyLifecycleTests
{
    [Fact]
    public async Task NotifyLoginAsync_PlayerNotInParty_NoPacketsSent()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        var hook = new FakeSessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var player = Player(1, "Solo", level: 30, job: 100, mapId: 100000000);
        var selfPackets = new List<byte[]>();

        await handler.NotifyLoginAsync(player, channelIndex: 0, Capture(selfPackets), CancellationToken.None);

        Assert.Empty(selfPackets);
        Assert.Empty(hook.SentPackets);
    }

    [Fact]
    public async Task NotifyLoginAsync_InParty_MarksOnlineAndBroadcastsToOtherOnlineMember()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(1, Member(2, "Guest"));
        var hook = new FakeSessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var player = Player(2, "Guest", level: 30, job: 100, mapId: 100000000);
        var selfPackets = new List<byte[]>();

        await handler.NotifyLoginAsync(player, channelIndex: 0, Capture(selfPackets), CancellationToken.None);

        Assert.Single(selfPackets);
        var toLeader = Assert.Single(hook.SentPackets);
        Assert.Equal(1, toLeader.CharacterId);
        Assert.True(service.GetPartyForCharacter(2)!.GetMember(2)!.IsOnline);
    }

    [Fact]
    public async Task NotifyLogoutAsync_PlayerNotInParty_NoOp()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        var hook = new FakeSessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var player = Player(1, "Solo", level: 30, job: 100, mapId: 100000000);

        await handler.NotifyLogoutAsync(player, CancellationToken.None);

        Assert.Empty(hook.SentPackets);
    }

    [Fact]
    public async Task NotifyLogoutAsync_NonLeaderOffline_MarksOfflineAndNotifiesRemainingOnlineMembersOnly()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(1, Member(2, "Guest"));
        var hook = new FakeSessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var guest = Player(2, "Guest", level: 30, job: 100, mapId: 100000000);

        await handler.NotifyLogoutAsync(guest, CancellationToken.None);

        var toLeader = Assert.Single(hook.SentPackets);
        Assert.Equal(1, toLeader.CharacterId);
        Assert.False(service.GetPartyForCharacter(1)!.GetMember(2)!.IsOnline);
        Assert.Equal(1, service.GetPartyForCharacter(1)!.LeaderId); // no succession — leaving member wasn't leader
    }

    [Fact]
    public async Task NotifyLogoutAsync_LeaderOffline_PromotesHighestLevelOnlineMemberInSameMap()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(1, Member(2, "LowLevel", level: 20, mapId: 100000000));
        service.JoinParty(1, Member(3, "HighLevel", level: 80, mapId: 100000000));
        service.JoinParty(1, Member(4, "DifferentMap", level: 200, mapId: 999999999));
        var hook = new FakeSessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var leader = Player(1, "Leader", level: 50, job: 100, mapId: 100000000);

        await handler.NotifyLogoutAsync(leader, CancellationToken.None);

        // Highest-level online member in the *same map* as the disconnecting leader wins,
        // even though member 4 has a higher level but is in a different map.
        Assert.Equal(3, service.GetPartyForCharacter(3)!.LeaderId);
    }

    [Fact]
    public async Task NotifyLogoutAsync_LeaderOffline_NoEligibleSuccessorInSameMap_LeavesLeaderUnchanged()
    {
        var service = new PartyService(new InMemoryPartyRegistry(firstPartyId: 1));
        service.CreateParty(Member(1, "Leader"));
        service.JoinParty(1, Member(2, "DifferentMap", level: 80, mapId: 999999999));
        var hook = new FakeSessionHook();
        var handler = new V113PartyOperationHandler(service, hook);
        var leader = Player(1, "Leader", level: 50, job: 100, mapId: 100000000);

        await handler.NotifyLogoutAsync(leader, CancellationToken.None);

        Assert.Equal(1, service.GetPartyForCharacter(2)!.LeaderId);
    }

    private static Func<byte[], CancellationToken, Task> Capture(List<byte[]> sink) =>
        (packet, _) =>
        {
            sink.Add(packet);
            return Task.CompletedTask;
        };

    private static PartyMember Member(int id, string name, int level = 30, int mapId = 100000000) =>
        new(id, name, level, JobId: 100, mapId, ChannelIndex: 0);

    private static Player Player(int id, string name, int level, int job, int mapId) =>
        new(
            new Character { Id = id, Name = name, Level = (byte)level, Job = (short)job, MapId = mapId },
            new Position(0, 0, 0, 0));

    private sealed class FakeSessionHook : IV113PartySessionHook
    {
        public List<(int CharacterId, byte[] Packet)> SentPackets { get; } = new();

        public ValueTask<V113PartySessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct) =>
            ValueTask.FromResult<V113PartySessionPlayer?>(null);

        public Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
        {
            SentPackets.Add((characterId, packet));
            return Task.CompletedTask;
        }
    }
}
