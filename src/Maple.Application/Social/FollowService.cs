using Maple.Application.OnlinePlayers;
using Maple.Core.World;

namespace Maple.Application.Social;

public enum FollowActionStatus
{
    Success,
    InvalidRequest,
    TargetNotFound,
    TargetRuntimeMissing,
    Self,
    DifferentMap,
    Busy,
    PendingNotFound,
    Declined,
    Canceled,
}

public sealed record FollowRequestResult(
    FollowActionStatus Status,
    OnlinePlayer? Target = null,
    Player? TargetPlayer = null,
    OnlinePlayer? Other = null,
    Player? OtherPlayer = null);

public sealed record FollowReplyResult(
    FollowActionStatus Status,
    OnlinePlayer? Requester = null,
    Player? RequesterPlayer = null);

public sealed class FollowService
{
    private readonly IOnlinePlayerRegistry _onlinePlayers;

    public FollowService(
        IOnlinePlayerRegistry onlinePlayers)
    {
        _onlinePlayers = onlinePlayers;
    }

    public FollowRequestResult RequestFollow(
        Player requester,
        int targetCharacterId,
        bool mapChangeResume,
        bool cancel)
    {
        if (mapChangeResume)
        {
            requester.SetFollowOn(true);
            var resumed = FindFollowOther(requester);
            resumed.Player?.SetFollowOn(true);
            return new FollowRequestResult(FollowActionStatus.Success, Other: resumed.Online, OtherPlayer: resumed.Player);
        }

        if (cancel)
        {
            var canceled = CancelFollow(requester);
            return new FollowRequestResult(FollowActionStatus.Canceled, Other: canceled.Online, OtherPlayer: canceled.Player);
        }

        var target = _onlinePlayers.FindById(targetCharacterId);
        if (target is null)
        {
            return new FollowRequestResult(FollowActionStatus.TargetNotFound);
        }

        if (target.CharacterId == requester.Character.Id)
        {
            return new FollowRequestResult(FollowActionStatus.Self, target);
        }

        if (target.Character.MapId != requester.Character.MapId)
        {
            return new FollowRequestResult(FollowActionStatus.DifferentMap, target);
        }

        if (requester.HasActiveFollow || requester.HasPendingFollow)
        {
            return new FollowRequestResult(FollowActionStatus.Busy, target);
        }

        var targetPlayer = target.Player;

        if (targetPlayer.HasActiveFollow || targetPlayer.HasPendingFollow)
        {
            return new FollowRequestResult(FollowActionStatus.Busy, target, targetPlayer);
        }

        requester.BeginFollowRequest(target.CharacterId);
        targetPlayer.ReceiveFollowRequest(requester.Character.Id);
        return new FollowRequestResult(FollowActionStatus.Success, target, targetPlayer);
    }

    public FollowReplyResult ReplyToFollow(Player replier, int requesterCharacterId, bool accepted)
    {
        var requesterEntry = _onlinePlayers.FindById(requesterCharacterId);
        var requesterPlayer = requesterEntry?.Player;
        if (requesterEntry is null || requesterPlayer is null ||
            replier.PendingFollowRequesterCharacterId != requesterCharacterId ||
            requesterPlayer.PendingFollowTargetCharacterId != replier.Character.Id)
        {
            replier.ClearPendingFollow();
            requesterPlayer?.ClearPendingFollow();
            return new FollowReplyResult(FollowActionStatus.PendingNotFound, requesterEntry, requesterPlayer);
        }

        if (!accepted)
        {
            replier.ClearPendingFollow();
            requesterPlayer.ClearPendingFollow();
            return new FollowReplyResult(FollowActionStatus.Declined, requesterEntry, requesterPlayer);
        }

        if (requesterEntry.Character.MapId != replier.Character.MapId)
        {
            replier.ClearPendingFollow();
            requesterPlayer.ClearPendingFollow();
            return new FollowReplyResult(FollowActionStatus.DifferentMap, requesterEntry, requesterPlayer);
        }

        requesterPlayer.StartBeingFollowedBy(replier.Character.Id);
        replier.StartFollowing(requesterPlayer.Character.Id);
        return new FollowReplyResult(FollowActionStatus.Success, requesterEntry, requesterPlayer);
    }

    public (OnlinePlayer? Online, Player? Player) CancelFollow(Player player)
    {
        var otherId = player.FollowTargetCharacterId > 0
            ? player.FollowTargetCharacterId
            : player.FollowFollowerCharacterId;

        var entry = otherId > 0 ? _onlinePlayers.FindById(otherId) : null;
        var other = entry?.Player;
        player.ClearFollow();
        other?.ClearFollow();
        return (entry, other);
    }

    private (OnlinePlayer? Online, Player? Player) FindFollowOther(Player player)
    {
        var otherId = player.FollowTargetCharacterId > 0
            ? player.FollowTargetCharacterId
            : player.FollowFollowerCharacterId;

        if (otherId <= 0) return (null, null);
        var entry = _onlinePlayers.FindById(otherId);
        return (entry, entry?.Player);
    }
}
