using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.Tests.Inventory;

public sealed class PlayerEquipTests
{
    [Fact]
    public void Equip_MovesBagEquipToEquippedSlot()
    {
        var player = CreatePlayer([BagEquip(1002000, 2)]);

        var equipped = player.Equip(2, -1);

        Assert.True(equipped);
        Assert.Null(player.Inventory.By(InventoryType.Equip).Get(2));

        var entry = Assert.Single(player.Character.Equips);
        Assert.Equal(-1, entry.Position);
        Assert.Equal(1002000, entry.ItemId);

        player.FlushInventory();
        Assert.Empty(player.Character.Items);
    }

    [Fact]
    public void Equip_SwapsExistingEquippedItemBackToSourceBagSlot()
    {
        var player = CreatePlayer(
            [BagEquip(1002000, 2)],
            new EquipEntry { Position = -1, ItemId = 1002001 });

        var equipped = player.Equip(2, -1);

        Assert.True(equipped);

        var entry = Assert.Single(player.Character.Equips);
        Assert.Equal(-1, entry.Position);
        Assert.Equal(1002000, entry.ItemId);

        var bagItem = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(2));
        Assert.Equal(1002001, bagItem.ItemId);
        Assert.Equal(2, bagItem.Slot);
    }

    [Fact]
    public void Unequip_MovesEquippedItemToEmptyBagSlot()
    {
        var player = CreatePlayer(
            [],
            new EquipEntry { Position = -1, ItemId = 1002000 });

        var unequipped = player.Unequip(-1, 4);

        Assert.True(unequipped);
        Assert.Empty(player.Character.Equips);

        var bagItem = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(4));
        Assert.Equal(1002000, bagItem.ItemId);
        Assert.Equal(4, bagItem.Slot);

        player.FlushInventory();
        var record = Assert.Single(player.Character.Items);
        Assert.Equal((byte)InventoryType.Equip, record.Type);
        Assert.True(record.IsEquip);
        Assert.Equal(4, record.Slot);
        Assert.Equal(1002000, record.ItemId);
    }

    [Fact]
    public void Unequip_ToOccupiedBagSlotFailsWithoutChangingState()
    {
        var player = CreatePlayer(
            [BagEquip(1002001, 4)],
            new EquipEntry { Position = -1, ItemId = 1002000 });

        var unequipped = player.Unequip(-1, 4);

        Assert.False(unequipped);

        var entry = Assert.Single(player.Character.Equips);
        Assert.Equal(-1, entry.Position);
        Assert.Equal(1002000, entry.ItemId);

        var bagItem = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(4));
        Assert.Equal(1002001, bagItem.ItemId);
    }

    private static Player CreatePlayer(IEnumerable<ItemRecord> items, params EquipEntry[] equips)
    {
        var character = new Character
        {
            Id = 1,
            Name = "EquipTest",
            Items = items.ToList(),
            Equips = equips.ToList(),
        };

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static ItemRecord BagEquip(int itemId, short slot) => new()
    {
        Type = (byte)InventoryType.Equip,
        IsEquip = true,
        ItemId = itemId,
        Slot = slot,
        Quantity = 1,
    };
}
