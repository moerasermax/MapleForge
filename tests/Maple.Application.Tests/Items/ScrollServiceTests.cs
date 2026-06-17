using Maple.Application.Items;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.Items;

public sealed class ScrollServiceTests
{
    [Fact]
    public void UseScroll_HundredPercentScroll_IncreasesStatsAndConsumesSlot()
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040200, slot: 1, quantity: 2)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var service = new ScrollService(new HardcodedScrollCatalog());

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false, randomSeed: 99);

        Assert.True(result.Applied);
        Assert.Equal(ScrollResult.Success, result.Result);
        var equip = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(1));
        Assert.Equal((short)1, equip.Str);
        Assert.Equal((byte)6, equip.UpgradeSlots);
        Assert.Equal((byte)1, equip.Level);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
    }

    [Fact]
    public void UseScroll_Fail_DecrementsUpgradeSlotsWithoutChangingStats()
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040201, slot: 1)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var service = new ScrollService(new HardcodedScrollCatalog());

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false, randomSeed: 99);

        Assert.Equal(ScrollResult.Fail, result.Result);
        var equip = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(1));
        Assert.Equal((short)0, equip.Str);
        Assert.Equal((byte)6, equip.UpgradeSlots);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
    }

    [Fact]
    public void UseScroll_WhiteScrollOnFail_LeavesUpgradeSlotsUnchangedAndConsumesWhiteScroll()
    {
        var player = CreatePlayer(
            useItems:
            [
                UseItem(2040201, slot: 1),
                UseItem(ScrollService.WhiteScrollItemId, slot: 2),
            ],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var service = new ScrollService(new HardcodedScrollCatalog());

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: true, randomSeed: 99);

        Assert.Equal(ScrollResult.Fail, result.Result);
        Assert.True(result.WhiteScrollUsed);
        var equip = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(1));
        Assert.Equal((byte)7, equip.UpgradeSlots);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(2));
    }

    [Fact]
    public void UseScroll_CursedFailedScroll_DestroysEquip()
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040202, slot: 1)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var service = new ScrollService(new HardcodedScrollCatalog());

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false, randomSeed: 99);

        Assert.Equal(ScrollResult.Curse, result.Result);
        Assert.True(result.EquipDestroyed);
        Assert.Null(player.Inventory.By(InventoryType.Equip).Get(1));
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
    }

    [Theory]
    [InlineData(2040200, 0, ScrollResult.Success)]
    [InlineData(2040201, 99, ScrollResult.Fail)]
    [InlineData(2040202, 99, ScrollResult.Curse)]
    public void UseScroll_ConsumesScrollOnAllOutcomes(int scrollId, int seed, ScrollResult expected)
    {
        var player = CreatePlayer(
            useItems: [UseItem(scrollId, slot: 1)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var service = new ScrollService(new HardcodedScrollCatalog());

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false, randomSeed: seed);

        Assert.Equal(expected, result.Result);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
        Assert.Contains(result.InventoryMutations, m => m.ItemId == scrollId && m.Removed);
    }

    [Fact]
    public void UseScroll_WithNoUpgradeSlots_FailsAndConsumesScrollWithoutChangingEquip()
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040200, slot: 1)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 0)]);
        var service = new ScrollService(new HardcodedScrollCatalog());

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false, randomSeed: 0);

        Assert.Equal(ScrollResult.Fail, result.Result);
        var equip = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(1));
        Assert.Equal((byte)0, equip.UpgradeSlots);
        Assert.Equal((byte)0, equip.Level);
        Assert.Equal((short)0, equip.Str);
        Assert.Null(player.Inventory.By(InventoryType.Use).Get(1));
    }

    [Fact]
    public void UseScroll_EquippedSlot_MutatesEquippedEntry()
    {
        var player = CreatePlayer(
            useItems: [UseItem(2044000, slot: 1)],
            equipItems: [],
            equipped: new EquipEntry { Position = -11, ItemId = 1302000, UpgradeSlots = 7 });
        var service = new ScrollService(new HardcodedScrollCatalog());

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: -11, whiteScroll: false, randomSeed: 0);

        Assert.True(result.EquippedSlot);
        Assert.Equal(ScrollResult.Success, result.Result);
        var entry = Assert.Single(player.Character.Equips);
        Assert.Equal((short)1, entry.Watk);
        Assert.Equal((byte)6, entry.UpgradeSlots);
        Assert.Equal((byte)1, entry.Level);
    }

    private static Player CreatePlayer(
        IReadOnlyList<ItemRecord> useItems,
        IReadOnlyList<ItemRecord> equipItems,
        EquipEntry? equipped = null)
    {
        var character = new Character
        {
            Id = 1,
            Name = "ScrollUser",
            Items = useItems.Concat(equipItems).ToList(),
        };

        if (equipped is not null)
        {
            character.Equips.Add(equipped);
        }

        return new Player(character, new Position(0, 0, 0, 0));
    }

    private static ItemRecord UseItem(int itemId, short slot, short quantity = 1) => new()
    {
        Type = (byte)InventoryType.Use,
        IsEquip = false,
        ItemId = itemId,
        Slot = slot,
        Quantity = quantity,
    };

    private static ItemRecord BagEquip(int itemId, short slot, byte slots) => new()
    {
        Type = (byte)InventoryType.Equip,
        IsEquip = true,
        ItemId = itemId,
        Slot = slot,
        Quantity = 1,
        UpgradeSlots = slots,
    };
}
