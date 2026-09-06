using Maple.Adapters.V113.Channel;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelPhaseAOpcodeHandlerTests
{
    [Fact]
    public void ItemUnlock_LockedEquipClearsLockAndSendsInventoryUpdate()
    {
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Equip,
            IsEquip = true,
            ItemId = 1002000,
            Slot = 2,
            Quantity = 1,
            Flag = ItemFlags.Lock,
        });

        var result = V113ItemUnlockHandler.Handle(Reader(w => w.WriteShort(2)), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(2, result.Packets.Count);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(V113StatsPackets.EnableActions(), result.Packets[1]);

        var equip = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(2));
        Assert.Equal(0, equip.Flag);
        Assert.Contains(player.Character.Items, item => item is { Slot: 2, Flag: 0 });
    }

    [Fact]
    public void ItemUnlock_MissingEquipOnlyEnablesActions()
    {
        var player = PlayerWithItems();

        var result = V113ItemUnlockHandler.Handle(Reader(w => w.WriteShort(2)), player);

        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(V113StatsPackets.EnableActions(), result.Packets[0]);
    }

    [Fact]
    public void ItemUnlock_UnlockedEquipOnlyEnablesActions()
    {
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Equip,
            IsEquip = true,
            ItemId = 1002000,
            Slot = 2,
            Quantity = 1,
            Flag = 0,
        });

        var result = V113ItemUnlockHandler.Handle(Reader(w => w.WriteShort(2)), player);

        Assert.False(result.CharacterMutated);
        Assert.Single(result.Packets);
        Assert.Equal(V113StatsPackets.EnableActions(), result.Packets[0]);
        Assert.Equal(0, player.Inventory.By(InventoryType.Equip).Get(2)!.Flag);
    }

    [Fact]
    public void ItemUnlock_LockedItem_ConsumesUnlockKeyWhenPresent()
    {
        var player = PlayerWithItems(
            new ItemRecord
            {
                Type = (byte)InventoryType.Equip,
                IsEquip = true,
                ItemId = 1002000,
                Slot = 2,
                Quantity = 1,
                Flag = ItemFlags.Lock,
            },
            new ItemRecord
            {
                Type = (byte)InventoryType.Use,
                ItemId = V113ItemUnlockHandler.UnlockKeyItemId,
                Slot = 5,
                Quantity = 3,
            });

        var result = V113ItemUnlockHandler.Handle(Reader(w => w.WriteShort(2)), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(3, result.Packets.Count); // ModifyItemUpdate + 鑰匙 ModifyInventoryQuantity + EnableActions
        Assert.Equal(0, player.Inventory.By(InventoryType.Equip).Get(2)!.Flag);
        Assert.Equal((short)2, player.Inventory.By(InventoryType.Use).Get(5)!.Quantity);
    }

    [Fact]
    public void ItemUnlock_LockedItem_NoUnlockKey_StillClearsFlagWithoutConsuming()
    {
        // 對照 Java removeById：沒有鑰匙時靜默無效果，不擋清除本身（照抄，不新增額外驗證阻擋玩家）。
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Equip,
            IsEquip = true,
            ItemId = 1002000,
            Slot = 2,
            Quantity = 1,
            Flag = ItemFlags.Lock,
        });

        var result = V113ItemUnlockHandler.Handle(Reader(w => w.WriteShort(2)), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(2, result.Packets.Count); // 沒有鑰匙可扣，只有 ModifyItemUpdate + EnableActions
        Assert.Equal(0, player.Inventory.By(InventoryType.Equip).Get(2)!.Flag);
    }

    [Fact]
    public void ItemUnlock_UntradeableItem_ClearsUntradeableFlag_WhenNotLocked()
    {
        // 對照 Java：LOCK 優先、UNTRADEABLE 其次（if/else if），這裡驗證沒鎖但不可交易的情境。
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Equip,
            IsEquip = true,
            ItemId = 1002000,
            Slot = 2,
            Quantity = 1,
            Flag = ItemFlags.Untradeable,
        });

        var result = V113ItemUnlockHandler.Handle(Reader(w => w.WriteShort(2)), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(0, player.Inventory.By(InventoryType.Equip).Get(2)!.Flag);
    }

    [Fact]
    public void ItemUnlock_LockedAndUntradeable_OnlyClearsLock_NotBoth()
    {
        // 對照 Java if/else if：兩個旗標都設時，只清 LOCK，UNTRADEABLE 不動。
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Equip,
            IsEquip = true,
            ItemId = 1002000,
            Slot = 2,
            Quantity = 1,
            Flag = (short)(ItemFlags.Lock | ItemFlags.Untradeable),
        });

        var result = V113ItemUnlockHandler.Handle(Reader(w => w.WriteShort(2)), player);

        var flag = player.Inventory.By(InventoryType.Equip).Get(2)!.Flag;
        Assert.False(ItemFlags.Has(flag, ItemFlags.Lock));
        Assert.True(ItemFlags.Has(flag, ItemFlags.Untradeable));
    }

    [Fact]
    public void ItemUnlock_NonEquipInventoryType_UsesTypeFromPacket()
    {
        // 對照 Java：ITEM_UNLOCK 適用任一背包類型，非僅裝備欄。
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000003,
            Slot = 4,
            Quantity = 1,
            Flag = ItemFlags.Lock,
        });
        var request = Reader(w => w
            .WriteShort(1)
            .WriteShort((short)InventoryType.Use)
            .WriteShort(4));

        var result = V113ItemUnlockHandler.Handle(request, player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(0, player.Inventory.By(InventoryType.Use).Get(4)!.Flag);
    }

    [Fact]
    public void ScriptedNpcItem_ParseReadsCompactSlotAndItemId()
    {
        var request = V113ScriptedNpcItemHandler.Parse(Reader(w => w.WriteShort(3).WriteInt(2430007)));

        Assert.Equal(0, request.Tick);
        Assert.Equal(3, request.Slot);
        Assert.Equal(2430007, request.ItemId);
    }

    [Fact]
    public void ScriptedNpcItem_MatchingUseItemConsumesOne()
    {
        var player = PlayerWithItems(new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2430007,
            Slot = 3,
            Quantity = 2,
        });

        var result = V113ScriptedNpcItemHandler.Handle(Reader(w => w.WriteShort(3).WriteInt(2430007)), player);

        Assert.True(result.CharacterMutated);
        Assert.Equal(2, result.Packets.Count);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.Packets[0], 0));
        Assert.Equal(V113StatsPackets.EnableActions(), result.Packets[1]);
        Assert.Equal((short)1, player.Inventory.By(InventoryType.Use).Get(3)!.Quantity);
        Assert.Contains(player.Character.Items, item => item is { Slot: 3, Quantity: 1 });
    }

    [Fact]
    public void MobNode_ReportsExistingMobInCurrentField()
    {
        var field = new FieldInstance(100000000);
        field.Add(new Mob(
            new MapMonster { MonsterId = 100100, X = 10, Y = 20, Fh = 1 },
            new MobStats(100100, MaxHp: 100, MaxMp: 10, Level: 1, Exp: 1),
            objectId: 77));

        var result = V113MobNodeHandler.Handle(Reader(w => w.WriteInt(77).WriteInt(2)), field);

        Assert.True(result.MobFound);
        Assert.Equal(77, result.Request.MobObjectId);
        Assert.Equal(2, result.Request.NodeIndex);
    }

    private static PacketReader Reader(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return new PacketReader(writer.ToArray());
    }

    private static Player PlayerWithItems(params ItemRecord[] items)
        => new(
            new Character
            {
                Id = 1,
                Name = "PhaseA",
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));
}
