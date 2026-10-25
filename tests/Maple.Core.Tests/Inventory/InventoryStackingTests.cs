using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.Items;
using Maple.Core.World;

namespace Maple.Core.Tests.Inventory;

/// <summary>P181：背包疊加（對照 Java MapleInventoryManipulator.addById／addFromDrop／checkSpace）。</summary>
public sealed class InventoryStackingTests
{
    [Fact]
    public void GainStacking_FillsExistingStacksInSlotOrder_ThenOpensNewSlots()
    {
        var bag = new Maple.Core.Inventory.Inventory(InventoryType.Use, 24);
        bag.Put(new Item { ItemId = 2000000, Slot = 3, Quantity = 90 });
        bag.Put(new Item { ItemId = 2000000, Slot = 1, Quantity = 98 });

        var result = bag.GainStacking(new Item { ItemId = 2000000, Quantity = 215 }, 100, static _ => true);

        Assert.True(result.Complete);
        Assert.Equal(
            [new InventoryQuantityMutation(InventoryType.Use, 1, 2000000, 98, 100), new InventoryQuantityMutation(InventoryType.Use, 3, 2000000, 90, 100)],
            result.Merged);
        Assert.Equal([(short)2, (short)4, (short)5], result.Added.Select(static i => i.Slot));
        Assert.Equal([(short)100, (short)100, (short)3], result.Added.Select(static i => i.Quantity)); // 215 − 2 − 10 = 203
        Assert.Equal(98 + 90 + 215, bag.CountById(2000000));
    }

    [Fact]
    public void GainStacking_CanMergeFalse_SkipsStack()
    {
        var bag = new Maple.Core.Inventory.Inventory(InventoryType.Etc, 24);
        bag.Put(new Item { ItemId = 4000000, Slot = 1, Quantity = 5, Expiration = 123 });

        var result = bag.GainStacking(new Item { ItemId = 4000000, Quantity = 2 }, 100, static e => e.Expiration == -1);

        Assert.Empty(result.Merged);
        Assert.Equal((short)2, Assert.Single(result.Added).Slot);
    }

    [Fact]
    public void GainStacking_Rechargeable_AlwaysOneNewSlot()
    {
        var bag = new Maple.Core.Inventory.Inventory(InventoryType.Use, 24);
        bag.Put(new Item { ItemId = 2070000, Slot = 1, Quantity = 10 });

        var result = bag.GainStacking(new Item { ItemId = 2070000, Quantity = 500 }, 100, static _ => true);

        Assert.Empty(result.Merged);
        Assert.Equal((short)500, Assert.Single(result.Added).Quantity);
    }

    [Fact]
    public void GainStacking_Full_ReportsRemaining_KeepsPartialProgress()
    {
        var bag = new Maple.Core.Inventory.Inventory(InventoryType.Etc, 2);
        bag.Put(new Item { ItemId = 4000000, Slot = 1, Quantity = 95 });
        bag.Put(new Item { ItemId = 4000001, Slot = 2, Quantity = 1 });

        var result = bag.GainStacking(new Item { ItemId = 4000000, Quantity = 20 }, 100, static _ => true);

        Assert.False(result.Complete);
        Assert.Equal((short)15, result.Remaining);
        Assert.Equal((short)100, bag.Get(1)!.Quantity);
        Assert.True(result.AnyChange);
    }

    [Theory]
    [InlineData(95, 5, true)]    // 全部補進既有堆疊，不需新格
    [InlineData(95, 6, false)]   // 還剩 1 個需要新格，但背包已滿
    public void HasSpaceFor_CountsExistingStacks(short existing, int quantity, bool expected)
    {
        var bag = new Maple.Core.Inventory.Inventory(InventoryType.Etc, 2);
        bag.Put(new Item { ItemId = 4000000, Slot = 1, Quantity = existing });
        bag.Put(new Item { ItemId = 4000001, Slot = 2, Quantity = 1 });

        Assert.Equal(expected, bag.HasSpaceFor(4000000, quantity, 100));
    }

    [Fact]
    public void Player_GainItemStacking_UsesProviderSlotMaxAndJavaOverrides()
    {
        var player = new Player(new Character { Id = 1, Name = "S" }, new Position(0, 0, 0, 0), new SlotMaxProvider(200));

        var result = player.GainItemStacking(2000000, 250);

        Assert.Equal([(short)200, (short)50], result.Added.Select(static i => i.Quantity));
        Assert.Equal(200, player.GetSlotMax(2000000));
        Assert.Equal(1000, player.GetSlotMax(5220010)); // GameConstants.getSlotMax 固定值
        Assert.Equal(1, ItemSlotMax.Default(1302000));
        Assert.Equal(100, ItemSlotMax.Default(4000000));
    }

    private sealed class SlotMaxProvider(short slotMax) : IEquipTemplateProvider
    {
        public Equip Create(int itemId) => new() { ItemId = itemId };

        public short GetSlotMax(int itemId) => slotMax;
    }
}
