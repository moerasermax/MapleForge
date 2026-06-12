using Maple.Core.Characters;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class PlayerFollowTests
{
    [Fact]
    public void BeginAndReceiveFollowRequest_TracksPendingState()
    {
        var requester = CreatePlayer(1);
        var target = CreatePlayer(2);

        Assert.True(requester.BeginFollowRequest(target.Character.Id));
        Assert.True(target.ReceiveFollowRequest(requester.Character.Id));

        Assert.Equal(2, requester.PendingFollowTargetCharacterId);
        Assert.Equal(1, target.PendingFollowRequesterCharacterId);
    }

    [Fact]
    public void StartFollowingAndBeingFollowedBy_RecordRelation()
    {
        var leader = CreatePlayer(1);
        var follower = CreatePlayer(2);

        leader.StartBeingFollowedBy(follower.Character.Id);
        follower.StartFollowing(leader.Character.Id);

        Assert.True(leader.HasActiveFollow);
        Assert.True(leader.IsFollowInitiator);
        Assert.Equal(2, leader.FollowFollowerCharacterId);
        Assert.True(follower.HasActiveFollow);
        Assert.False(follower.IsFollowInitiator);
        Assert.Equal(1, follower.FollowTargetCharacterId);
    }

    [Fact]
    public void ClearFollow_RemovesRelationAndPendingState()
    {
        var player = CreatePlayer(1);

        player.BeginFollowRequest(2);
        player.StartBeingFollowedBy(2);
        player.ClearFollow();

        Assert.False(player.HasActiveFollow);
        Assert.False(player.HasPendingFollow);
        Assert.Equal(0, player.FollowFollowerCharacterId);
        Assert.Equal(0, player.FollowTargetCharacterId);
    }

    private static Player CreatePlayer(int id) =>
        new(new Character { Id = id, Name = $"Follow{id}" }, new Position(0, 0, 0, 0));
}
