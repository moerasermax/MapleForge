using Maple.Application.OnlinePlayers;
using Maple.Application.Social;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.Social;

public sealed class RingServiceTests
{
    [Fact]
    public void RequestAndAcceptProposal_CreatesRingStateForBothPlayers()
    {
        var online = new InMemoryOnlinePlayerRegistry();
        var runtime = new InMemoryOnlinePlayerRuntimeRegistry();
        var proposer = Player(1, "Proposer", Item(InventoryType.Use, 2240004, slot: 1));
        var target = Player(2, "Target");
        Register(online, runtime, proposer);
        Register(online, runtime, target);
        var service = new RingService(online, runtime);

        var request = service.RequestProposal(proposer, target.Character.Name, 2240004);
        var reply = service.ReplyToProposal(target, accepted: true, proposer.Character.Name, proposer.Character.Id);

        Assert.Equal(RingActionStatus.Success, request.Status);
        Assert.Equal(RingActionStatus.Success, reply.Status);
        Assert.True(proposer.HasVisibleMarriageRing);
        Assert.True(target.HasVisibleMarriageRing);
        Assert.Equal(target.Character.Id, proposer.MarriagePartnerCharacterId);
        Assert.Equal(proposer.Character.Id, target.MarriagePartnerCharacterId);
        Assert.Equal(1112300, proposer.MarriageRingItemId);
        Assert.Equal(1112300, target.MarriageRingItemId);
        Assert.DoesNotContain(proposer.Character.Items, i => i.ItemId == 2240004);
        Assert.Contains(proposer.Character.Items, i => i.ItemId == 1112300 && i.Type == (byte)InventoryType.Equip);
        Assert.Contains(target.Character.Items, i => i.ItemId == 1112300 && i.Type == (byte)InventoryType.Equip);
    }

    [Fact]
    public void DeclineProposal_ClearsPendingStateWithoutCreatingRing()
    {
        var online = new InMemoryOnlinePlayerRegistry();
        var runtime = new InMemoryOnlinePlayerRuntimeRegistry();
        var proposer = Player(1, "Proposer", Item(InventoryType.Use, 2240004, slot: 1));
        var target = Player(2, "Target");
        Register(online, runtime, proposer);
        Register(online, runtime, target);
        var service = new RingService(online, runtime);

        service.RequestProposal(proposer, target.Character.Name, 2240004);
        var reply = service.ReplyToProposal(target, accepted: false, proposer.Character.Name, proposer.Character.Id);

        Assert.Equal(RingActionStatus.Declined, reply.Status);
        Assert.False(proposer.HasVisibleMarriageRing);
        Assert.False(target.HasVisibleMarriageRing);
        Assert.Equal(0, proposer.PendingMarriagePartnerCharacterId);
        Assert.Equal(0, target.PendingMarriageRequesterCharacterId);
        Assert.True(proposer.HasMarriageProposalItem(2240004));
    }

    [Fact]
    public void RequestProposal_RequiresSameMapOnlineTarget()
    {
        var online = new InMemoryOnlinePlayerRegistry();
        var runtime = new InMemoryOnlinePlayerRuntimeRegistry();
        var proposer = Player(1, "Proposer", Item(InventoryType.Use, 2240004, slot: 1));
        var target = Player(2, "Target", mapId: 200000000);
        Register(online, runtime, proposer);
        Register(online, runtime, target);
        var service = new RingService(online, runtime);

        var result = service.RequestProposal(proposer, target.Character.Name, 2240004);

        Assert.Equal(RingActionStatus.DifferentMap, result.Status);
        Assert.False(target.HasPendingFollow);
        Assert.Equal(0, target.PendingMarriageRequesterCharacterId);
    }

    private static void Register(
        IOnlinePlayerRegistry online,
        IOnlinePlayerRuntimeRegistry runtime,
        Player player)
    {
        var token = new object();
        online.Register(new OnlinePlayer(player.Character.Id, player.Character.Name, 1, player.Character, SendNoop), token);
        runtime.Register(player, token);
    }

    private static Player Player(int id, string name, params ItemRecord[] items) => Player(id, name, 100000000, items);

    private static Player Player(int id, string name, int mapId, params ItemRecord[] items)
    {
        var character = new Character
        {
            Id = id,
            Name = name,
            MapId = mapId,
            Items = items.ToList(),
        };
        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static ItemRecord Item(InventoryType type, int itemId, short slot) => new()
    {
        Type = (byte)type,
        IsEquip = type == InventoryType.Equip,
        ItemId = itemId,
        Quantity = 1,
        Slot = slot,
    };

    private static Task SendNoop(byte[] packet, CancellationToken ct) => Task.CompletedTask;
}
