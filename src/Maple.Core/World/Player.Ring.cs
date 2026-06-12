using Maple.Core.Inventory;

namespace Maple.Core.World;

public sealed partial class Player
{
    public const int EngagementBoxItemIdStart = 2240004;
    public const int EngagementBoxItemIdEnd = 2240015;
    public const int MarriageRingItemIdBase = 1112300;

    public int MarriagePartnerCharacterId { get; private set; }

    public int MarriageRingItemId { get; private set; }

    public long MarriageRingId { get; private set; }

    public int PendingMarriagePartnerCharacterId { get; private set; }

    public int PendingMarriageProposalItemId { get; private set; }

    public int PendingMarriageRequesterCharacterId { get; private set; }

    public int PendingMarriageRequesterProposalItemId { get; private set; }

    public bool HasVisibleMarriageRing =>
        MarriagePartnerCharacterId > 0 &&
        MarriageRingItemId > 0 &&
        MarriageRingId > 0;

    public static bool IsMarriageProposalItem(int itemId) =>
        itemId is >= EngagementBoxItemIdStart and <= EngagementBoxItemIdEnd;

    public static int ToMarriageRingItemId(int proposalItemId)
    {
        if (!IsMarriageProposalItem(proposalItemId))
        {
            throw new ArgumentOutOfRangeException(nameof(proposalItemId), proposalItemId, "Unsupported engagement item id.");
        }

        return MarriageRingItemIdBase + (proposalItemId - EngagementBoxItemIdStart);
    }

    public static bool IsMarriageRingItem(int itemId) => itemId switch
    {
        >= 1112300 and <= 1112311 => true,
        >= 1112315 and <= 1112320 => true,
        1112803 or 1112806 or 1112807 or 1112808 or 1112809 => true,
        _ => false,
    };

    public bool HasMarriageProposalItem(int itemId) =>
        IsMarriageProposalItem(itemId) &&
        HasItem(InventoryType.Use, itemId);

    public bool HasMarriageRingItem(int itemId) =>
        IsMarriageRingItem(itemId) &&
        (Inventory.By(InventoryType.Equip).CountById(itemId) > 0 ||
         Character.Equips.Any(e => e.ItemId == itemId));

    public bool BeginMarriageProposal(int partnerCharacterId, int proposalItemId)
    {
        if (partnerCharacterId <= 0 || !IsMarriageProposalItem(proposalItemId))
        {
            return false;
        }

        PendingMarriagePartnerCharacterId = partnerCharacterId;
        PendingMarriageProposalItemId = proposalItemId;
        return true;
    }

    public bool ReceiveMarriageProposal(int requesterCharacterId, int proposalItemId)
    {
        if (requesterCharacterId <= 0 || !IsMarriageProposalItem(proposalItemId))
        {
            return false;
        }

        PendingMarriageRequesterCharacterId = requesterCharacterId;
        PendingMarriageRequesterProposalItemId = proposalItemId;
        return true;
    }

    public void ClearMarriageProposal()
    {
        PendingMarriagePartnerCharacterId = 0;
        PendingMarriageProposalItemId = 0;
    }

    public void ClearIncomingMarriageProposal()
    {
        PendingMarriageRequesterCharacterId = 0;
        PendingMarriageRequesterProposalItemId = 0;
    }

    public bool WearMarriageRing(int partnerCharacterId, int ringItemId, long ringId)
    {
        if (partnerCharacterId <= 0 || ringId <= 0 || !HasMarriageRingItem(ringItemId))
        {
            return false;
        }

        MarriagePartnerCharacterId = partnerCharacterId;
        MarriageRingItemId = ringItemId;
        MarriageRingId = ringId;
        return true;
    }

    public void RemoveMarriageRing()
    {
        MarriagePartnerCharacterId = 0;
        MarriageRingItemId = 0;
        MarriageRingId = 0;
    }
}
