namespace Maple.Core.World;

public sealed partial class Player
{
    public int FollowTargetCharacterId { get; private set; }

    public int FollowFollowerCharacterId { get; private set; }

    public bool IsFollowOn { get; private set; }

    public bool IsFollowInitiator { get; private set; }

    public int PendingFollowTargetCharacterId { get; private set; }

    public int PendingFollowRequesterCharacterId { get; private set; }

    public bool HasActiveFollow =>
        IsFollowOn &&
        (FollowTargetCharacterId > 0 || FollowFollowerCharacterId > 0);

    public bool HasPendingFollow =>
        PendingFollowTargetCharacterId > 0 || PendingFollowRequesterCharacterId > 0;

    public bool BeginFollowRequest(int targetCharacterId)
    {
        if (targetCharacterId <= 0 || HasActiveFollow || HasPendingFollow)
        {
            return false;
        }

        PendingFollowTargetCharacterId = targetCharacterId;
        return true;
    }

    public bool ReceiveFollowRequest(int requesterCharacterId)
    {
        if (requesterCharacterId <= 0 || HasActiveFollow || HasPendingFollow)
        {
            return false;
        }

        PendingFollowRequesterCharacterId = requesterCharacterId;
        return true;
    }

    public void StartBeingFollowedBy(int followerCharacterId)
    {
        FollowTargetCharacterId = 0;
        FollowFollowerCharacterId = followerCharacterId > 0 ? followerCharacterId : 0;
        IsFollowOn = FollowFollowerCharacterId > 0;
        IsFollowInitiator = IsFollowOn;
        ClearPendingFollow();
    }

    public void StartFollowing(int targetCharacterId)
    {
        FollowTargetCharacterId = targetCharacterId > 0 ? targetCharacterId : 0;
        FollowFollowerCharacterId = 0;
        IsFollowOn = FollowTargetCharacterId > 0;
        IsFollowInitiator = false;
        ClearPendingFollow();
    }

    public void SetFollowOn(bool enabled)
    {
        if (FollowTargetCharacterId <= 0 && FollowFollowerCharacterId <= 0)
        {
            IsFollowOn = false;
            return;
        }

        IsFollowOn = enabled;
    }

    public void ClearPendingFollow()
    {
        PendingFollowTargetCharacterId = 0;
        PendingFollowRequesterCharacterId = 0;
    }

    public void ClearFollow()
    {
        FollowTargetCharacterId = 0;
        FollowFollowerCharacterId = 0;
        IsFollowOn = false;
        IsFollowInitiator = false;
        ClearPendingFollow();
    }
}
