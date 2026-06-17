using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.Tests.World;

public sealed class PlayerMountTests
{
    [Fact]
    public void UseMountFood_ReducesFatigueAddsExpAndConsumesUseItem()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2260000, Quantity = 2 });
        player.SetMount(new PlayerMountState(itemId: 1902000, skillId: 1004, level: 5, exp: 0, fatigue: 40));

        var result = player.UseMountFood(slot: 1, itemId: 2260000, expGain: 15);

        Assert.True(result.Applied);
        Assert.False(result.LevelUp);
        Assert.Equal(40, result.PreviousFatigue);
        Assert.Equal(10, player.Mount!.Fatigue);
        Assert.Equal(15, player.Mount.Exp);
        Assert.Equal(5, player.Mount.Level);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Equal((short)1, result.ConsumedItem!.NewQuantity);
        Assert.Contains(player.Character.Items, i => i.ItemId == 2260000 && i.Quantity == 1);
    }

    [Fact]
    public void UseMountFood_LevelsUpWhenExpReachesJavaThreshold()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2260000, Quantity = 1 });
        player.SetMount(new PlayerMountState(itemId: 1902000, skillId: 1004, level: 1, exp: 5, fatigue: 1));

        var result = player.UseMountFood(slot: 1, itemId: 2260000, expGain: 1);

        Assert.True(result.Applied);
        Assert.True(result.LevelUp);
        Assert.Equal(2, player.Mount!.Level);
        Assert.Equal(6, player.Mount.Exp);
        Assert.Equal(0, player.Mount.Fatigue);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
    }

    [Fact]
    public void UseMountFood_DoesNotAddExpWhenFatigueWasZero()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2260000, Quantity = 1 });
        player.SetMount(new PlayerMountState(itemId: 1902000, skillId: 1004, level: 3, exp: 24, fatigue: 0));

        var result = player.UseMountFood(slot: 1, itemId: 2260000, expGain: 20);

        Assert.True(result.Applied);
        Assert.False(result.LevelUp);
        Assert.Equal(3, player.Mount!.Level);
        Assert.Equal(24, player.Mount.Exp);
        Assert.Equal(0, player.Mount.Fatigue);
    }

    [Fact]
    public void UseMountFood_WithoutMountDoesNotConsumeItem()
    {
        var player = NewPlayer();
        player.Inventory.By(InventoryType.Use).Put(new Item { Slot = 1, ItemId = 2260000, Quantity = 1 });

        var result = player.UseMountFood(slot: 1, itemId: 2260000, expGain: 15);

        Assert.False(result.Applied);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
    }

    private static Player NewPlayer() =>
        new(new Character { Id = 1, Name = "MountUser" }, new Position(0, 0, 0, 0));
}
