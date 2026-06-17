using Maple.Application.Items;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.World;
using Maple.Adapters.V113.Channel;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelScrollPacketTests
{
    [Fact]
    public void OpcodeConstants_MatchJava()
    {
        Assert.Equal(0x50, V113ChannelRecvOp.UseUpgradeScroll);
        Assert.Equal(unchecked((short)0x9F), V113ChannelSendOp.ShowScrollEffect);
        Assert.Equal(0x50, V113ScrollPackets.RecvUseUpgradeScroll);
        Assert.Equal(unchecked((short)0x9F), V113ScrollPackets.SendShowScrollEffect);
    }

    [Fact]
    public void ParseUseUpgradeScroll_ReadsTickSlotsAndFlags()
    {
        var w = new PacketWriter();
        w.WriteInt(1234);
        w.WriteShort(2);
        w.WriteShort(-11);
        w.WriteShort(2);

        var req = V113ScrollPackets.ParseUseUpgradeScroll(new PacketReader(w.ToArray()));

        Assert.Equal(1234, req.Tick);
        Assert.Equal(2, req.ScrollSlot);
        Assert.Equal(-11, req.EquipSlot);
        Assert.Equal(2, req.Flags);
        Assert.True(req.WhiteScroll);
    }

    [Fact]
    public void ShowScrollEffect_WritesJavaShape()
    {
        var packet = V113ScrollPackets.ShowScrollEffect(123, ScrollResult.Success, legendarySpirit: false, whiteScroll: false);

        Assert.Equal(
            new byte[]
            {
                0x9F, 0x00,
                0x7B, 0x00, 0x00, 0x00,
                0x01, 0x00,
                0x00, 0x00,
                0x00,
            },
            packet);
    }

    [Fact]
    public void ShowScrollEffect_CurseSetsCurseByte()
    {
        var packet = V113ScrollPackets.ShowScrollEffect(123, ScrollResult.Curse, legendarySpirit: false, whiteScroll: true);

        Assert.Equal(0, packet[6]);
        Assert.Equal(1, packet[7]);
        Assert.Equal(1, packet[10]);
    }

    [Fact]
    public void HandleUseUpgradeScroll_SuccessReturnsScrollConsumeEquipUpdateAndEffect()
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040200, slot: 1)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var handler = new V113ScrollHandler(new ScrollService(new HardcodedScrollCatalog()));

        var result = handler.HandleUseUpgradeScroll(Request(scrollSlot: 1, equipSlot: 1, flags: 0, tick: 99), player);

        Assert.True(result.Handled);
        Assert.True(result.CharacterMutated);
        Assert.Equal(ScrollResult.Success, result.Use.Result);
        Assert.Equal(2, result.SelfPackets.Count);
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.SelfPackets[0], 0));
        Assert.Equal(V113ChannelSendOp.ModifyInventoryItem, BitConverter.ToInt16(result.SelfPackets[1], 0));
        Assert.NotNull(result.BroadcastPacket);
        Assert.Equal(V113ChannelSendOp.ShowScrollEffect, BitConverter.ToInt16(result.BroadcastPacket!, 0));

        var equip = Assert.IsType<Equip>(player.Inventory.By(InventoryType.Equip).Get(1));
        Assert.Equal((short)1, equip.Str);
    }

    [Fact]
    public void HandleUseUpgradeScroll_CurseReturnsEquipRemove()
    {
        var player = CreatePlayer(
            useItems: [UseItem(2040202, slot: 1)],
            equipItems: [BagEquip(1102000, slot: 1, slots: 7)]);
        var handler = new V113ScrollHandler(new ScrollService(new HardcodedScrollCatalog()));

        var result = handler.HandleUseUpgradeScroll(Request(scrollSlot: 1, equipSlot: 1, flags: 0, tick: 99), player);

        Assert.Equal(ScrollResult.Curse, result.Use.Result);
        Assert.True(result.Use.EquipDestroyed);
        var removePacket = result.SelfPackets[1];
        Assert.Equal(1, removePacket[3]);   // mod count
        Assert.Equal(3, removePacket[4]);   // remove mode
        Assert.Equal((byte)InventoryType.Equip, removePacket[5]);
        Assert.Equal(1, BitConverter.ToInt16(removePacket, 6));
    }

    private static PacketReader Request(short scrollSlot, short equipSlot, short flags, int tick)
    {
        var w = new PacketWriter();
        w.WriteInt(tick);
        w.WriteShort(scrollSlot);
        w.WriteShort(equipSlot);
        w.WriteShort(flags);
        return new PacketReader(w.ToArray());
    }

    private static Player CreatePlayer(
        IReadOnlyList<ItemRecord> useItems,
        IReadOnlyList<ItemRecord> equipItems,
        EquipEntry? equipped = null)
    {
        var character = new Character
        {
            Id = 123,
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
