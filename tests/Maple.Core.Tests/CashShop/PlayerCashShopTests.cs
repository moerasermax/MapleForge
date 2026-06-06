using Maple.Core.CashShop;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.Tests.CashShop;

public sealed class PlayerCashShopTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000L);

    [Fact]
    public void GainCashShopItem_CashCategory_AddsToCashInventoryWithUniqueIdAndDefaultPeriod()
    {
        var player = NewPlayer();
        var cashItem = new CashItemDefinition(10000001, 5350000, 10, 45, 0, 2, -1, true);

        var gained = player.GainCashShopItem(cashItem, FixedNow);

        Assert.NotNull(gained);
        Assert.Equal(InventoryType.Cash, Player.InventoryTypeOf(cashItem.ItemId));
        Assert.Equal((short)1, gained!.Slot);
        Assert.Equal((short)10, gained.Quantity);
        Assert.Equal(1, gained.UniqueId);
        Assert.Equal(FixedNow.AddDays(45).ToUnixTimeMilliseconds(), gained.Expiration);

        player.FlushInventory();
        var record = Assert.Single(player.Character.Items);
        Assert.Equal((byte)InventoryType.Cash, record.Type);
        Assert.Equal(5350000, record.ItemId);
        Assert.Equal(1, record.UniqueId);
    }

    [Fact]
    public void GainCashShopItem_EquipCategory_AddsEquipWithQuantityOne()
    {
        var player = NewPlayer();
        var cashItem = new CashItemDefinition(10100004, 1702388, 10, 199, 30, 2, -1, true);

        var gained = player.GainCashShopItem(cashItem, FixedNow);

        var equip = Assert.IsType<Equip>(gained);
        Assert.Equal(InventoryType.Equip, Player.InventoryTypeOf(cashItem.ItemId));
        Assert.Equal((short)1, equip.Quantity);
        Assert.Equal(FixedNow.AddDays(30).ToUnixTimeMilliseconds(), equip.Expiration);
    }

    private static Player NewPlayer()
        => new(
            new Character
            {
                Id = 1,
                Name = "CashShopCore",
                Gender = 0,
            },
            new Position(0, 0, 0, 0));
}
