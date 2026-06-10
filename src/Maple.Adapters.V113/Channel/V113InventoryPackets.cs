using Maple.Core.Inventory;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Channel;

/// <summary>c2s ITEM_MOVE 解析結果（對照 Java InventoryHandler.ItemMove）。</summary>
internal readonly record struct ItemMoveRequest(byte RawType, short Src, short Dst, short Quantity)
{
    public bool IsValidBagType => InventoryTypes.IsValid(RawType);
    public InventoryType Type => (InventoryType)RawType;

    /// <summary>格內移動（兩端皆背包正格；非穿脫/丟棄）。MVP-0 僅處理此類。</summary>
    public bool IsWithinBagMove => Src > 0 && Dst > 0;

    public bool IsEquipMove => RawType == (byte)InventoryType.Equip && Src > 0 && Dst < 0;

    public bool IsUnequipMove => RawType == (byte)InventoryType.Equip && Src < 0 && Dst > 0;

    public bool IsDropMove => Dst == 0;
}

internal readonly record struct V113InventoryArrangeRequest(int Tick, byte RawType)
{
    public bool IsValidBagType => InventoryTypes.IsValid(RawType);
    public InventoryType Type => (InventoryType)RawType;
}

/// <summary>
/// v113 背包封包：解 ITEM_MOVE(c2s 0x41)、編 MODIFY_INVENTORY_ITEM(s2c 0x1B)。
/// 對照 Java InventoryHandler.ItemMove / MaplePacketCreator.MODIFY_INVENTORY_ITEM。
/// </summary>
internal static class V113InventoryPackets
{
    // c2s：[int tick][byte invType][short src][short dst][short quantity]
    public static ItemMoveRequest ParseItemMove(PacketReader reader)
    {
        reader.ReadInt();                       // tick（server 計時驗證，MVP 忽略）
        var type = reader.ReadByte();
        var src = reader.ReadShort();
        var dst = reader.ReadShort();
        var qty = reader.Remaining >= 2 ? reader.ReadShort() : (short)0;
        return new ItemMoveRequest(type, src, dst, qty);
    }

    public static V113InventoryArrangeRequest ParseArrange(PacketReader reader)
    {
        var tick = reader.ReadInt();
        var type = reader.ReadByte();
        return new V113InventoryArrangeRequest(tick, type);
    }

    public static byte[] FinishedSort(byte type) => InventoryArrangeResult(V113ChannelSendOp.GatherItemResult, type);

    public static byte[] FinishedGather(byte type) => InventoryArrangeResult(V113ChannelSendOp.SortItemResult, type);

    /// <summary>
    /// MODIFY_INVENTORY_ITEM：單筆「移動(mode 2)」。
    /// [opcode][byte updateTick=1][byte modCount=1][byte mode=2][byte type][short oldPos][short newPos][movement?]。
    /// Java 對負 slot 會在所有 mods 後補 movement byte：oldPos&lt;0 為 1（脫裝），newPos&lt;0 為 2（穿裝）。
    /// </summary>
    public static byte[] ModifyMove(InventoryType type, short src, short dst)
    {
        var w = new PacketWriter(13);
        w.WriteShort(V113ChannelSendOp.ModifyInventoryItem);
        w.WriteByte(1);              // updateTick
        w.WriteByte(1);              // mod count
        w.WriteByte(2);              // mode = move
        w.WriteByte((byte)type);
        w.WriteShort(src);           // old position
        w.WriteShort(dst);           // new position
        if (src < 0 || dst < 0)
            w.WriteByte(src < 0 ? 1 : 2);
        return w.ToArray();
    }

    private static byte[] InventoryArrangeResult(short opcode, byte type)
    {
        var w = new PacketWriter(4);
        w.WriteShort(opcode);
        w.WriteByte(1);
        w.WriteByte(type);
        return w.ToArray();
    }
}
