using Maple.Application.OnlinePlayers;
using Maple.Application.Trades;
using Maple.Core.Characters;
using Maple.Core.Inventory;
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
        registry.Register(new OnlinePlayer(
            player.Character.Id,
            player.Character.Name,
            1,
            player.Character,
            static (_, _) => Task.CompletedTask),
            new object());

        service.RegisterPlayer(player, 1, static (_, _) => Task.CompletedTask, new object());
    }

    private sealed class FakeOnlinePlayerRegistry : IOnlinePlayerRegistry
    {
        private readonly Dictionary<int, OnlinePlayer> _players = [];

        public void Register(OnlinePlayer player, object token) => _players[player.CharacterId] = player;

        public OnlinePlayer? Deregister(int characterId, object token)
            => _players.Remove(characterId, out var player) ? player : null;

        public OnlinePlayer? FindById(int characterId)
            => _players.GetValueOrDefault(characterId);

        public OnlinePlayer? FindByName(string name)
            => _players.Values.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
