using Maple.Application.Shops;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Shops;
using Maple.Core.World;

namespace Maple.Application.Tests.Shops;

public sealed class ShopServiceTests
{
    [Fact]
    public void OpenShop_ByShopId_SetsActiveShop()
    {
        var service = new ShopService(new FakeShopCatalog());
        var player = NewPlayer(meso: 1000);

        var shop = service.OpenShop(player, 35);

        Assert.NotNull(shop);
        Assert.Equal(35, player.ActiveShopId);
    }

    [Fact]
    public void Buy_DeductsMesoAndAddsItem()
    {
        var service = new ShopService(new FakeShopCatalog());
        var player = NewPlayer(meso: 1000);
        service.OpenShop(player, 35);

        var result = service.Buy(player, 2000000, 2);

        Assert.Equal(ShopTransactionStatus.Success, result.Status);
        Assert.Equal(900, player.Character.Meso);
        Assert.Equal(2, player.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Equal(2, player.Character.Items.Single(i => i.ItemId == 2000000).Quantity);
    }

    [Fact]
    public void Sell_AddsMesoAndReducesSlotQuantity()
    {
        var service = new ShopService(new FakeShopCatalog());
        var player = NewPlayer(meso: 1000, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 1,
            Quantity = 3,
        });
        service.OpenShop(player, 35);

        var result = service.Sell(player, slot: 1, itemId: 2000000, quantity: 2);

        Assert.Equal(ShopTransactionStatus.Success, result.Status);
        Assert.Equal(1050, player.Character.Meso);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)?.Quantity);
        Assert.Equal((short)1, player.Character.Items.Single(i => i.ItemId == 2000000).Quantity);
    }

    private static Player NewPlayer(int meso, params ItemRecord[] items)
        => new(
            new Character
            {
                Id = 1,
                Name = "ShopTest",
                Meso = meso,
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private sealed class FakeShopCatalog : IShopCatalog
    {
        private readonly ShopDefinition _shop = new(
            35,
            1033002,
            new[]
            {
                new ShopItem(2000000, 50, 1000, 0, 0, 25),
                new ShopItem(2000001, 160, 1000, 0, 0, 80),
            });

        public ShopDefinition? GetShop(int shopId) => shopId == _shop.ShopId ? _shop : null;

        public ShopDefinition? GetShopForNpc(int npcId) => npcId == _shop.NpcId ? _shop : null;

        public int? GetSellPrice(int itemId)
            => _shop.Items.FirstOrDefault(i => i.ItemId == itemId)?.SellPrice;
    }
}
