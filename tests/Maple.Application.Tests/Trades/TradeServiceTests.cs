using Maple.Application.OnlinePlayers;
using Maple.Application.Trades;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Trade;
using Maple.Core.World;

namespace Maple.Application.Tests.Trades;

public sealed class TradeServiceTests
{
    [Fact]
    public void CompleteTrade_ExchangesItemsAndMesoAtomically()
    {
        var registry = new FakeOnlinePlayerRegistry();
        var service = new TradeService(registry);
        var alice = NewPlayer(1, "Alice", meso: 1000, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 1,
            Quantity = 3,
        });
        var bob = NewPlayer(2, "Bob", meso: 2000, new ItemRecord
        {
            Type = (byte)InventoryType.Etc,
            ItemId = 4000000,
            Slot = 1,
            Quantity = 2,
        });
        Register(service, registry, alice);
        Register(service, registry, bob);

        Assert.True(service.StartTrade(alice).Success);
        Assert.True(service.InviteTrade(alice, bob.Character.Id).Success);
        Assert.True(service.VisitTrade(bob).Success);
        Assert.True(service.OfferItem(alice, InventoryType.Use, 1, 2, -1).Success);
        Assert.True(service.OfferMeso(alice, 100).Success);
        Assert.True(service.OfferItem(bob, InventoryType.Etc, 1, 1, -1).Success);
        Assert.True(service.OfferMeso(bob, 200).Success);

        var firstConfirm = service.ConfirmTrade(alice);
        var secondConfirm = service.ConfirmTrade(bob);

        Assert.True(firstConfirm.Success);
        Assert.True(secondConfirm.Success);
        Assert.Contains(secondConfirm.Notices, n => n.Kind == TradeNoticeKind.Completion && n.RecipientCharacterId == alice.Character.Id);
        Assert.Contains(secondConfirm.Notices, n => n.Kind == TradeNoticeKind.Completion && n.RecipientCharacterId == bob.Character.Id);
        Assert.Null(alice.Trade);
        Assert.Null(bob.Trade);
        Assert.Equal(1100, alice.Character.Meso);
        Assert.Equal(1900, bob.Character.Meso);
        Assert.Equal(1, alice.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Equal(1, alice.Inventory.By(InventoryType.Etc).CountById(4000000));
        Assert.Equal(2, bob.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Equal(1, bob.Inventory.By(InventoryType.Etc).CountById(4000000));
        Assert.Equal(1, alice.Character.Items.Single(i => i.ItemId == 2000000).Quantity);
        Assert.Equal(2, bob.Character.Items.Single(i => i.ItemId == 2000000).Quantity);
    }

    [Fact]
    public void CompleteTrade_StacksIntoExistingStack_AndRecordsReceived()
    {
        // P202：Java CompleteTrade → addFromDrop，疊進收方同 ID 的既有堆疊。
        var registry = new FakeOnlinePlayerRegistry();
        var service = new TradeService(registry);
        var alice = NewPlayer(1, "Alice", meso: 1000, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 1,
            Quantity = 3,
        });
        var bob = NewPlayer(2, "Bob", meso: 0, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 4,
            Quantity = 10,
        });
        Register(service, registry, alice);
        Register(service, registry, bob);

        Assert.True(service.StartTrade(alice).Success);
        Assert.True(service.InviteTrade(alice, bob.Character.Id).Success);
        Assert.True(service.VisitTrade(bob).Success);
        Assert.True(service.OfferItem(alice, InventoryType.Use, 1, 3, -1).Success);
        Assert.True(service.OfferMeso(alice, 100).Success);
        var trade = alice.Trade!;
        service.ConfirmTrade(alice);
        service.ConfirmTrade(bob);

        var bag = bob.Inventory.By(InventoryType.Use);
        Assert.Equal(13, Assert.Single(bag.Items).Quantity);
        var (type, gain) = Assert.Single(trade.InviteeReceived);
        Assert.Equal(InventoryType.Use, type);
        Assert.Empty(gain.Added);
        var merged = Assert.Single(gain.Merged);
        Assert.Equal((short)4, merged.Slot);
        Assert.True(trade.InviteeReceivedMeso > 0);
        Assert.Empty(trade.InitiatorReceived);
    }

    [Fact]
    public void CompleteTrade_UniqueItemAlreadyOwned_CancelsWithPickupRestricted()
    {
        // P215：Java MapleTrade.check()——收方已有唯一道具 → 2（getTradeCancel 第 3 byte = 10）。
        var registry = new FakeOnlinePlayerRegistry();
        var service = new TradeService(registry, new UniqueItemCatalog(4030003));
        var alice = NewPlayer(1, "Alice", meso: 0, new ItemRecord { Type = (byte)InventoryType.Etc, ItemId = 4030003, Slot = 1, Quantity = 1 });
        var bob = NewPlayer(2, "Bob", meso: 0, new ItemRecord { Type = (byte)InventoryType.Etc, ItemId = 4030003, Slot = 1, Quantity = 1 });
        Register(service, registry, alice);
        Register(service, registry, bob);

        Assert.True(service.StartTrade(alice).Success);
        Assert.True(service.InviteTrade(alice, bob.Character.Id).Success);
        Assert.True(service.VisitTrade(bob).Success);
        Assert.True(service.OfferItem(alice, InventoryType.Etc, 1, 1, -1).Success);
        service.ConfirmTrade(alice);
        var result = service.ConfirmTrade(bob);

        Assert.All(result.Notices, n => Assert.Equal((TradeNoticeKind.Cancel, TradeCancelReason.PickupRestricted), (n.Kind, n.CancelReason)));
        Assert.Equal(1, alice.Inventory.By(InventoryType.Etc).CountById(4030003)); // 放回
        Assert.Equal(1, bob.Inventory.By(InventoryType.Etc).CountById(4030003));
    }

    private sealed class UniqueItemCatalog(int uniqueId) : Maple.Core.Items.IItemMakeCatalog
    {
        public Maple.Core.Items.ItemMakeGemRecipe? GetGemRecipe(int itemId) => null;
        public Maple.Core.Items.ItemMakeCreateRecipe? GetCreateRecipe(int itemId) => null;
        public int GetItemMakeLevel(int itemId) => 0;
        public int GetRequiredLevel(int itemId) => 0;
        public bool IsDropRestricted(int itemId) => false;
        public bool IsAccountShared(int itemId) => false;
        public bool IsPickupRestricted(int itemId) => itemId == uniqueId;
        public Equip? CreateEquip(int itemId) => null;
        public Maple.Core.Items.ItemMakeEnhanceStats? GetEnhanceStats(int itemId) => null;
        public int GemRecipeCount => 0;
        public int CreateRecipeCount => 0;
    }

    [Fact]
    public void CancelTrade_RestoresOfferedItemsAndMeso()
    {
        var registry = new FakeOnlinePlayerRegistry();
        var service = new TradeService(registry);
        var alice = NewPlayer(1, "Alice", meso: 1000, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 1,
            Quantity = 3,
        });
        var bob = NewPlayer(2, "Bob", meso: 2000);
        Register(service, registry, alice);
        Register(service, registry, bob);

        service.StartTrade(alice);
        service.InviteTrade(alice, bob.Character.Id);
        service.VisitTrade(bob);
        service.OfferItem(alice, InventoryType.Use, 1, 2, -1);
        service.OfferMeso(alice, 100);

        var result = service.CancelTrade(alice);

        Assert.True(result.Success);
        Assert.Contains(result.Notices, n => n.Kind == TradeNoticeKind.Cancel && n.RecipientCharacterId == alice.Character.Id);
        Assert.Contains(result.Notices, n => n.Kind == TradeNoticeKind.Cancel && n.RecipientCharacterId == bob.Character.Id);
        Assert.Null(alice.Trade);
        Assert.Null(bob.Trade);
        Assert.Equal(1000, alice.Character.Meso);
        Assert.Equal(3, alice.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Equal(3, alice.Character.Items.Single(i => i.ItemId == 2000000).Quantity);
    }

    [Fact]
    public void CompleteTrade_WhenRecipientInventoryFull_CancelsAndRestores()
    {
        var registry = new FakeOnlinePlayerRegistry();
        var service = new TradeService(registry);
        var alice = NewPlayer(1, "Alice", meso: 1000, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 1,
            Quantity = 1,
        });
        var bob = NewPlayer(2, "Bob", meso: 2000, FullUseInventory());
        Register(service, registry, alice);
        Register(service, registry, bob);

        service.StartTrade(alice);
        service.InviteTrade(alice, bob.Character.Id);
        service.VisitTrade(bob);
        service.OfferItem(alice, InventoryType.Use, 1, 1, -1);

        Assert.True(service.ConfirmTrade(alice).Success);
        var result = service.ConfirmTrade(bob);

        Assert.Equal(TradeServiceStatus.InventoryFull, result.Status);
        Assert.Contains(result.Notices, n => n.Kind == TradeNoticeKind.Cancel && n.CancelReason == Maple.Core.Trade.TradeCancelReason.InventoryFull);
        Assert.Null(alice.Trade);
        Assert.Null(bob.Trade);
        Assert.Equal(1000, alice.Character.Meso);
        Assert.Equal(1, alice.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Equal(1, alice.Character.Items.Single(i => i.ItemId == 2000000).Quantity);
        Assert.Equal(24, bob.Inventory.By(InventoryType.Use).Items.Count);
    }

    private static Player NewPlayer(int id, string name, int meso, params ItemRecord[] items)
        => new(
            new Character
            {
                Id = id,
                Name = name,
                MapId = 100000000,
                Meso = meso,
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private static ItemRecord[] FullUseInventory()
        => Enumerable.Range(1, 24)
            .Select(slot => new ItemRecord
            {
                Type = (byte)InventoryType.Use,
                ItemId = 2000000 + slot,
                Slot = (short)slot,
                Quantity = 1,
            })
            .ToArray();

    private static void Register(TradeService service, FakeOnlinePlayerRegistry registry, Player player)
    {
        registry.Register(player, 1, static (_, _) => Task.CompletedTask, new object());

        service.RegisterPlayer(player, 1, static (_, _) => Task.CompletedTask, new object());
    }

    private sealed class FakeOnlinePlayerRegistry : IOnlinePlayerRegistry
    {
        private readonly Dictionary<int, OnlinePlayer> _players = [];

        public void Register(Player player, int channel, Func<byte[], CancellationToken, Task> sendPacket, object token)
            => _players[player.Character.Id] = new OnlinePlayer(player.Character.Id, player.Character.Name, channel, player, sendPacket);

        public OnlinePlayer? Deregister(int characterId, object token)
            => _players.Remove(characterId, out var player) ? player : null;

        public OnlinePlayer? FindById(int characterId)
            => _players.GetValueOrDefault(characterId);

        public OnlinePlayer? FindByName(string name)
            => _players.Values.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<OnlinePlayer> GetAll() => _players.Values.ToList();
    }
}
