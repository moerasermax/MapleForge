using Maple.Application.OnlinePlayers;
using Maple.Application.Social;
using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Application.Tests.Social;

public sealed class FollowServiceTests
{
    [Fact]
    public void RequestAndAcceptFollow_EstablishesLeaderAndFollower()
    {
        var online = new InMemoryOnlinePlayerRegistry();
        var leader = Player(1, "Leader");
        var follower = Player(2, "Follower");
        Register(online, leader);
        Register(online, follower);
        var service = new FollowService(online);

        var request = service.RequestFollow(leader, follower.Character.Id, mapChangeResume: false, cancel: false);
        var reply = service.ReplyToFollow(follower, leader.Character.Id, accepted: true);

        Assert.Equal(FollowActionStatus.Success, request.Status);
        Assert.Equal(FollowActionStatus.Success, reply.Status);
        Assert.True(leader.IsFollowInitiator);
        Assert.Equal(follower.Character.Id, leader.FollowFollowerCharacterId);
        Assert.False(follower.IsFollowInitiator);
        Assert.Equal(leader.Character.Id, follower.FollowTargetCharacterId);
    }

    [Fact]
    public void CancelFollow_ClearsBothSides()
    {
        var online = new InMemoryOnlinePlayerRegistry();
        var leader = Player(1, "Leader");
        var follower = Player(2, "Follower");
        Register(online, leader);
        Register(online, follower);
        var service = new FollowService(online);
        service.RequestFollow(leader, follower.Character.Id, mapChangeResume: false, cancel: false);
        service.ReplyToFollow(follower, leader.Character.Id, accepted: true);

        var canceled = service.RequestFollow(leader, targetCharacterId: 0, mapChangeResume: false, cancel: true);

        Assert.Equal(FollowActionStatus.Canceled, canceled.Status);
        Assert.False(leader.HasActiveFollow);
        Assert.False(follower.HasActiveFollow);
        Assert.Equal(follower, canceled.OtherPlayer);
    }

    [Fact]
    public void ReplyDeclined_ClearsPendingState()
    {
        var online = new InMemoryOnlinePlayerRegistry();
        var leader = Player(1, "Leader");
        var follower = Player(2, "Follower");
        Register(online, leader);
        Register(online, follower);
        var service = new FollowService(online);

        service.RequestFollow(leader, follower.Character.Id, mapChangeResume: false, cancel: false);
        var reply = service.ReplyToFollow(follower, leader.Character.Id, accepted: false);

        Assert.Equal(FollowActionStatus.Declined, reply.Status);
        Assert.False(leader.HasPendingFollow);
        Assert.False(follower.HasPendingFollow);
        Assert.False(leader.HasActiveFollow);
        Assert.False(follower.HasActiveFollow);
    }

    private static void Register(
        IOnlinePlayerRegistry online,
        Player player)
    {
        var token = new object();
        online.Register(player, 1, SendNoop, token);
    }

    private static Player Player(int id, string name) =>
        new(new Character { Id = id, Name = name, MapId = 100000000 }, new Position(0, 0, 0, 0));

    private static Task SendNoop(byte[] packet, CancellationToken ct) => Task.CompletedTask;
}
