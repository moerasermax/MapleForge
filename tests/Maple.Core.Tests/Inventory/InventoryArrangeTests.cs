using Maple.Core.Inventory;

namespace Maple.Core.Tests.Inventory;

public sealed class InventoryArrangeTests
{
    [Fact]
    public void SortByItemId_ReordersItemsToFrontByItemId()
    {
        var inventory = new Maple.Core.Inventory.Inventory(InventoryType.Etc, 24);
        inventory.Put(new Item { Slot = 5, ItemId = 4000002, Quantity = 1 });
        inventory.Put(new Item { Slot = 2, ItemId = 4000001, Quantity = 3 });
        inventory.Put(new Item { Slot = 9, ItemId = 4000001, Quantity = 1 });

        var changed = inventory.SortByItemId();

        Assert.True(changed);
        Assert.Equal(4000001, inventory.Get(1)!.ItemId);
        Assert.Equal((short)3, inventory.Get(1)!.Quantity);
        Assert.Equal(4000001, inventory.Get(2)!.ItemId);
        Assert.Equal((short)1, inventory.Get(2)!.Quantity);
        Assert.Equal(4000002, inventory.Get(3)!.ItemId);
        Assert.Null(inventory.Get(5));
        Assert.Null(inventory.Get(9));
    }

    [Fact]
    public void GatherByItemId_UsesSameJavaOrderingAsSort()
    {
        var inventory = new Maple.Core.Inventory.Inventory(InventoryType.Use, 24);
        inventory.Put(new Item { Slot = 4, ItemId = 2000002 });
        inventory.Put(new Item { Slot = 8, ItemId = 2000001 });

        var changed = inventory.GatherByItemId();

        Assert.True(changed);
        Assert.Equal(2000001, inventory.Get(1)!.ItemId);
        Assert.Equal(2000002, inventory.Get(2)!.ItemId);
    }
}
