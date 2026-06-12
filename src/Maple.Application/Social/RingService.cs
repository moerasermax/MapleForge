using Maple.Application.OnlinePlayers;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Social;

public enum RingActionStatus
{
    Success,
    InvalidRequest,
    TargetNotFound,
    Self,
    DifferentMap,
    AlreadyEngaged,
    TargetAlreadyEngaged,
    MissingProposalItem,
    RingAlreadyOwned,
    InventoryFull,
    TargetInventoryFull,
    TargetRuntimeMissing,
    ProposalNotFound,
    Declined,
}

public sealed record RingProposalResult(
    RingActionStatus Status,
    OnlinePlayer? Target = null,
    Player? TargetPlayer = null,
    int ProposalItemId = 0,
    int RingItemId = 0);

public sealed record RingReplyResult(
    RingActionStatus Status,
    OnlinePlayer? Proposer = null,
    Player? ProposerPlayer = null,
    int ProposalItemId = 0,
    int RingItemId = 0,
    long RingId = 0);

public sealed class RingService
{
    private readonly IOnlinePlayerRegistry _onlinePlayers;
    private readonly IOnlinePlayerRuntimeRegistry _runtimePlayers;
    private long _nextRingId = 30_000;

    public RingService(
        IOnlinePlayerRegistry onlinePlayers,
        IOnlinePlayerRuntimeRegistry runtimePlayers)
    {
        _onlinePlayers = onlinePlayers;
        _runtimePlayers = runtimePlayers;
    }

    public RingProposalResult RequestProposal(Player proposer, string targetName, int proposalItemId)
    {
        if (string.IsNullOrWhiteSpace(targetName) || !Player.IsMarriageProposalItem(proposalItemId))
        {
            return new RingProposalResult(RingActionStatus.InvalidRequest);
        }

        if (proposer.HasVisibleMarriageRing)
        {
            return new RingProposalResult(RingActionStatus.AlreadyEngaged);
        }

        var ringItemId = Player.ToMarriageRingItemId(proposalItemId);
        if (proposer.HasMarriageRingItem(ringItemId))
        {
            return new RingProposalResult(RingActionStatus.RingAlreadyOwned, RingItemId: ringItemId);
        }

        var target = _onlinePlayers.FindByName(targetName);
        if (target is null)
        {
            return new RingProposalResult(RingActionStatus.TargetNotFound, RingItemId: ringItemId);
        }

        if (target.CharacterId == proposer.Character.Id)
        {
            return new RingProposalResult(RingActionStatus.Self, target, RingItemId: ringItemId);
        }

        if (target.Character.MapId != proposer.Character.MapId)
        {
            return new RingProposalResult(RingActionStatus.DifferentMap, target, RingItemId: ringItemId);
        }

        if (!proposer.CanGainItem(InventoryType.Equip))
        {
            return new RingProposalResult(RingActionStatus.InventoryFull, target, RingItemId: ringItemId);
        }

        var targetPlayer = _runtimePlayers.FindById(target.CharacterId);
        if (targetPlayer is null)
        {
            return new RingProposalResult(RingActionStatus.TargetRuntimeMissing, target, RingItemId: ringItemId);
        }

        if (!targetPlayer.CanGainItem(InventoryType.Equip))
        {
            return new RingProposalResult(RingActionStatus.TargetInventoryFull, target, targetPlayer, RingItemId: ringItemId);
        }

        if (targetPlayer.HasVisibleMarriageRing || targetPlayer.PendingMarriageRequesterCharacterId > 0)
        {
            return new RingProposalResult(RingActionStatus.TargetAlreadyEngaged, target, targetPlayer, RingItemId: ringItemId);
        }

        if (!proposer.HasMarriageProposalItem(proposalItemId))
        {
            return new RingProposalResult(RingActionStatus.MissingProposalItem, target, targetPlayer, proposalItemId, ringItemId);
        }

        proposer.BeginMarriageProposal(target.CharacterId, proposalItemId);
        targetPlayer.ReceiveMarriageProposal(proposer.Character.Id, proposalItemId);
        return new RingProposalResult(RingActionStatus.Success, target, targetPlayer, proposalItemId, ringItemId);
    }

    public RingReplyResult ReplyToProposal(Player replier, bool accepted, string proposerName, int proposerCharacterId)
    {
        if (proposerCharacterId <= 0 || string.IsNullOrWhiteSpace(proposerName))
        {
            return new RingReplyResult(RingActionStatus.InvalidRequest);
        }

        var proposer = _onlinePlayers.FindById(proposerCharacterId);
        if (proposer is null || !string.Equals(proposer.Name, proposerName, StringComparison.OrdinalIgnoreCase))
        {
            replier.ClearIncomingMarriageProposal();
            return new RingReplyResult(RingActionStatus.ProposalNotFound);
        }

        var proposerPlayer = _runtimePlayers.FindById(proposerCharacterId);
        if (proposerPlayer is null ||
            replier.PendingMarriageRequesterCharacterId != proposerCharacterId ||
            proposerPlayer.PendingMarriagePartnerCharacterId != replier.Character.Id)
        {
            replier.ClearIncomingMarriageProposal();
            proposerPlayer?.ClearMarriageProposal();
            return new RingReplyResult(RingActionStatus.ProposalNotFound, proposer, proposerPlayer);
        }

        var proposalItemId = replier.PendingMarriageRequesterProposalItemId;
        if (!Player.IsMarriageProposalItem(proposalItemId) ||
            proposerPlayer.PendingMarriageProposalItemId != proposalItemId)
        {
            replier.ClearIncomingMarriageProposal();
            proposerPlayer.ClearMarriageProposal();
            return new RingReplyResult(RingActionStatus.ProposalNotFound, proposer, proposerPlayer);
        }

        var ringItemId = Player.ToMarriageRingItemId(proposalItemId);
        if (!accepted)
        {
            replier.ClearIncomingMarriageProposal();
            proposerPlayer.ClearMarriageProposal();
            return new RingReplyResult(RingActionStatus.Declined, proposer, proposerPlayer, proposalItemId, ringItemId);
        }

        if (proposer.Character.MapId != replier.Character.MapId)
        {
            return new RingReplyResult(RingActionStatus.DifferentMap, proposer, proposerPlayer, proposalItemId, ringItemId);
        }

        if (!proposerPlayer.HasMarriageProposalItem(proposalItemId))
        {
            return new RingReplyResult(RingActionStatus.MissingProposalItem, proposer, proposerPlayer, proposalItemId, ringItemId);
        }

        if (!proposerPlayer.CanGainItem(InventoryType.Equip))
        {
            return new RingReplyResult(RingActionStatus.InventoryFull, proposer, proposerPlayer, proposalItemId, ringItemId);
        }

        if (!replier.CanGainItem(InventoryType.Equip))
        {
            return new RingReplyResult(RingActionStatus.TargetInventoryFull, proposer, proposerPlayer, proposalItemId, ringItemId);
        }

        if (!proposerPlayer.TryTakeItemById(InventoryType.Use, proposalItemId, quantity: 1, out _))
        {
            return new RingReplyResult(RingActionStatus.MissingProposalItem, proposer, proposerPlayer, proposalItemId, ringItemId);
        }

        if (proposerPlayer.GainItem(InventoryType.Equip, ringItemId) is null ||
            replier.GainItem(InventoryType.Equip, ringItemId) is null)
        {
            return new RingReplyResult(RingActionStatus.InventoryFull, proposer, proposerPlayer, proposalItemId, ringItemId);
        }

        var ringId = Interlocked.Increment(ref _nextRingId);
        proposerPlayer.WearMarriageRing(replier.Character.Id, ringItemId, ringId);
        replier.WearMarriageRing(proposerPlayer.Character.Id, ringItemId, ringId);
        proposerPlayer.ClearMarriageProposal();
        replier.ClearIncomingMarriageProposal();
        proposerPlayer.FlushInventory();
        replier.FlushInventory();

        return new RingReplyResult(RingActionStatus.Success, proposer, proposerPlayer, proposalItemId, ringItemId, ringId);
    }

    public static byte JavaEngagementError(RingActionStatus status) => status switch
    {
        RingActionStatus.MissingProposalItem or RingActionStatus.InvalidRequest => 0x0D,
        RingActionStatus.TargetNotFound or RingActionStatus.TargetRuntimeMissing => 0x12,
        RingActionStatus.DifferentMap => 0x13,
        RingActionStatus.InventoryFull => 0x14,
        RingActionStatus.TargetInventoryFull => 0x15,
        RingActionStatus.AlreadyEngaged or RingActionStatus.RingAlreadyOwned => 0x17,
        RingActionStatus.TargetAlreadyEngaged => 0x18,
        RingActionStatus.ProposalNotFound => 0x1D,
        RingActionStatus.Declined => 0x1E,
        _ => 0,
    };
}
