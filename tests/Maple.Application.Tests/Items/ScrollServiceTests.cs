using Maple.Application.Items;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.Items;

public sealed class ScrollServiceTests
{
    // P121：伺服器端亂數序列（第一個＝成功判定 nextInt(100)，第二個＝詛咒判定 nextInt(99)）。
    private static readonly int[] Rolls = [99, 0];

    [Fact]
    public void UseScroll_HundredPercentScroll_IncreasesStatsAndConsumesSlot()
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040200, slot: 1, quantity: 2)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var service = new ScrollService(new HardcodedScrollCatalog(), new SequenceRandom(Rolls));

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false);

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
        var service = new ScrollService(new HardcodedScrollCatalog(), new SequenceRandom(Rolls));

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false);

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
        var service = new ScrollService(new HardcodedScrollCatalog(), new SequenceRandom(Rolls));

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: true);

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
        var service = new ScrollService(new HardcodedScrollCatalog(), new SequenceRandom(Rolls));

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false);

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
        var service = new ScrollService(new HardcodedScrollCatalog(), new SequenceRandom([seed, 0]));

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false);

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
        var service = new ScrollService(new HardcodedScrollCatalog(), new SequenceRandom(Rolls));

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false);

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
        var service = new ScrollService(new HardcodedScrollCatalog(), new SequenceRandom(Rolls));

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: -11, whiteScroll: false);

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
    [Theory]
    [InlineData(60, 60, ScrollResult.Success)]  // Java nextInt(100) <= success（含等號）
    [InlineData(60, 61, ScrollResult.Fail)]
    public void UseScroll_SuccessRollIsInclusive(int successRate, int roll, ScrollResult expected)
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040201, slot: 1)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var catalog = new FixedScrollCatalog(new Maple.Core.Items.ScrollEffect(2040201, successRate, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var service = new ScrollService(catalog, new SequenceRandom([roll]));

        Assert.Equal(expected, service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false).Result);
    }

    [Theory]
    [InlineData(50, 49, ScrollResult.Curse)]   // Java nextInt(99) < cursed
    [InlineData(50, 50, ScrollResult.Fail)]
    public void UseScroll_CurseIsPercentAfterFailure(int curseRate, int curseRoll, ScrollResult expected)
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040202, slot: 1)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var catalog = new FixedScrollCatalog(new Maple.Core.Items.ScrollEffect(2040202, 10, curseRate, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var service = new ScrollService(catalog, new SequenceRandom([99, curseRoll]));

        var result = service.UseScroll(player, scrollSlot: 1, equipSlot: 1, whiteScroll: false);

        Assert.Equal(expected, result.Result);
        Assert.Equal(expected == ScrollResult.Curse, result.EquipDestroyed);
    }

    private sealed class FixedScrollCatalog(Maple.Core.Items.ScrollEffect effect) : Maple.Core.Items.IScrollCatalog
    {
        public Maple.Core.Items.ScrollEffect? GetScroll(int scrollId) => scrollId == effect.ScrollId ? effect : null;
    }

    private sealed class SequenceRandom(int[] rolls) : Random
    {
        private int _index;

        public override int Next(int maxValue) => rolls[Math.Min(_index++, rolls.Length - 1)];
    }
}
