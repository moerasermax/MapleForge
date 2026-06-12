using Maple.Application.NpcItemServices;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.NpcItemServices;
using Maple.Core.World;

namespace Maple.Application.Tests.NpcItemServices;

public sealed class OwlServiceTests
{
    [Fact]
    public void CanOpenOwl_RequiresFreeMarketAndOwlItem()
    {
        var player = CreatePlayer(mapId: 910000000, UseItem(OwlService.MinervaOwlItemId, 1, 3));
        var service = new OwlService(new EmptyOwlSearchCatalog());

        Assert.True(service.CanOpenOwl(player));

        player.Character.MapId = 100000000;
        Assert.False(service.CanOpenOwl(player));
        Assert.Equal(OwlSearchStatus.NotInFreeMarket, service.GetOpenFailure(player));
    }

    [Fact]
    public void UseMinerva_WithEmptyCatalog_ReturnsEmptyResultAndDoesNotConsumeItem()
    {
        var player = CreatePlayer(mapId: 910000000, UseItem(OwlService.MinervaOwlItemId, 2, 1));
        var service = new OwlService(new EmptyOwlSearchCatalog());

        var result = service.UseMinerva(player, 2, OwlService.MinervaOwlItemId, 2000000);

        Assert.Equal(OwlSearchStatus.Success, result.Status);
        Assert.Empty(result.Entries);
        Assert.Null(result.ConsumedItem);
        Assert.Equal(1, player.Inventory.By(InventoryType.Use).Get(2)!.Quantity);
    }

    [Fact]
    public void UseMinerva_WithResults_ConsumesOneItem()
    {
        var player = CreatePlayer(mapId: 910000000, UseItem(OwlService.MinervaOwlItemId, 2, 2));
        var service = new OwlService(new FakeOwlCatalog(new OwlSearchEntry(
            "Seller",
            910000001,
            "FM shop",
            Quantity: 1,
            Bundles: 2,
            Price: 1234,
            ListingObjectId: 9001,
            ChannelIndex: 0,
            InventoryType.Use)));

        var result = service.UseMinerva(player, 2, OwlService.MinervaOwlItemId, 2000000);

        Assert.Equal(OwlSearchStatus.Success, result.Status);
        Assert.Single(result.Entries);
        Assert.NotNull(result.ConsumedItem);
        Assert.Equal(1, player.Inventory.By(InventoryType.Use).Get(2)!.Quantity);
    }

    private static Player CreatePlayer(int mapId, ItemRecord item)
    {
        var character = new Character
        {
            Id = 1,
            Name = "OwlUser",
            MapId = mapId,
            Items = new List<ItemRecord> { item },
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static ItemRecord UseItem(int itemId, short slot, short quantity) => new()
    {
        Type = (byte)InventoryType.Use,
        ItemId = itemId,
        Slot = slot,
        Quantity = quantity,
        Expiration = -1,
    };

    private sealed class FakeOwlCatalog : IOwlSearchCatalog
    {
        private readonly IReadOnlyList<OwlSearchEntry> _entries;

        public FakeOwlCatalog(params OwlSearchEntry[] entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<OwlSearchEntry> Search(int itemId) => _entries;
    }
}
