using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.Tests.Inventory;

public sealed class PlayerRangedCombatTests
{
    [Fact]
    public void TryResolveRangedProjectile_UsesCashProjectileAsVisual()
    {
        var player = CreatePlayer([
            UseItem(2070000, slot: 1, quantity: 800),
            CashItem(5021000, slot: 2),
        ]);

        var resolved = player.TryResolveRangedProjectile(1, 2, out var projectile);

        Assert.True(resolved);
        Assert.Equal(2070000, projectile.ItemId);
        Assert.Equal(5021000, projectile.VisualItemId);
    }

    [Fact]
    public void TryConsumeUseItemById_ConsumesAcrossStacksAndFlushes()
    {
        var player = CreatePlayer([
            UseItem(2060000, slot: 1, quantity: 2),
            UseItem(2060000, slot: 2, quantity: 3),
        ]);

        var consumed = player.TryConsumeUseItemById(2060000, 4, out var mutations);

        Assert.True(consumed);
        Assert.Equal(2, mutations.Count);
        Assert.Equal(0, mutations[0].NewQuantity);
        Assert.Equal(1, mutations[1].NewQuantity);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
        var remainingStack = player.Inventory.By(InventoryType.Use).Get(2);
        Assert.NotNull(remainingStack);
        Assert.Equal(1, remainingStack.Quantity);

        var record = Assert.Single(player.Character.Items);
        Assert.Equal(2, record.Slot);
        Assert.Equal(1, record.Quantity);
    }

    [Fact]
    public void TryConsumeUseItemById_FailsWithoutChangingInventory_WhenQuantityIsInsufficient()
    {
        var player = CreatePlayer([UseItem(2060000, slot: 1, quantity: 2)]);

        var consumed = player.TryConsumeUseItemById(2060000, 3, out var mutations);

        Assert.False(consumed);
        Assert.Empty(mutations);
        var stack = player.Inventory.By(InventoryType.Use).Get(1);
        Assert.NotNull(stack);
        Assert.Equal(2, stack.Quantity);
    }

    private static Player CreatePlayer(IEnumerable<ItemRecord> items)
    {
        var character = new Character
        {
            Id = 1,
            Name = "RangedTest",
            Items = items.ToList(),
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static ItemRecord UseItem(int itemId, short slot, short quantity) => new()
    {
        Type = (byte)InventoryType.Use,
        ItemId = itemId,
        Slot = slot,
        Quantity = quantity,
    };

    private static ItemRecord CashItem(int itemId, short slot) => new()
    {
        Type = (byte)InventoryType.Cash,
        ItemId = itemId,
        Slot = slot,
        Quantity = 1,
    };
}
