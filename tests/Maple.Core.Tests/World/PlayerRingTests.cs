using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class PlayerRingTests
{
    [Fact]
    public void WearMarriageRing_RecordsPartnerAndRingStateWhenRingItemExists()
    {
        var player = CreatePlayer(new ItemRecord
        {
            Type = (byte)InventoryType.Equip,
            IsEquip = true,
            ItemId = 1112300,
            Slot = 1,
            Quantity = 1,
        });

        var worn = player.WearMarriageRing(partnerCharacterId: 2, ringItemId: 1112300, ringId: 30001);

        Assert.True(worn);
        Assert.True(player.HasVisibleMarriageRing);
        Assert.Equal(2, player.MarriagePartnerCharacterId);
        Assert.Equal(1112300, player.MarriageRingItemId);
        Assert.Equal(30001, player.MarriageRingId);
    }

    [Fact]
    public void WearMarriageRing_RejectsMissingRingItem()
    {
        var player = CreatePlayer();

        var worn = player.WearMarriageRing(partnerCharacterId: 2, ringItemId: 1112300, ringId: 30001);

        Assert.False(worn);
        Assert.False(player.HasVisibleMarriageRing);
    }

    [Fact]
    public void RemoveMarriageRing_ClearsVisibleStateButKeepsInventoryItem()
    {
        var player = CreatePlayer(new ItemRecord
        {
            Type = (byte)InventoryType.Equip,
            IsEquip = true,
            ItemId = 1112300,
            Slot = 1,
            Quantity = 1,
        });

        player.WearMarriageRing(partnerCharacterId: 2, ringItemId: 1112300, ringId: 30001);
        player.RemoveMarriageRing();

        Assert.False(player.HasVisibleMarriageRing);
        Assert.True(player.HasMarriageRingItem(1112300));
    }

    [Fact]
    public void MarriageProposal_TracksOutgoingAndIncomingRuntimeState()
    {
        var proposer = CreatePlayer(id: 1);
        var target = CreatePlayer(id: 2);

        Assert.True(proposer.BeginMarriageProposal(target.Character.Id, 2240004));
        Assert.True(target.ReceiveMarriageProposal(proposer.Character.Id, 2240004));

        Assert.Equal(2, proposer.PendingMarriagePartnerCharacterId);
        Assert.Equal(2240004, proposer.PendingMarriageProposalItemId);
        Assert.Equal(1, target.PendingMarriageRequesterCharacterId);
        Assert.Equal(2240004, target.PendingMarriageRequesterProposalItemId);
    }

    private static Player CreatePlayer(params ItemRecord[] items) => CreatePlayer(id: 1, items);

    private static Player CreatePlayer(int id, params ItemRecord[] items)
    {
        var character = new Character
        {
            Id = id,
            Name = $"Ring{id}",
            Items = items.ToList(),
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }
}
