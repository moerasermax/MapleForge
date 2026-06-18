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

    /// <summary>
    /// Full item update for metadata changes such as equip flags. MapleForge uses remove+add so the client
    /// receives the full item-info block instead of Java mode 1's quantity-only shape.
    /// </summary>
    public static byte[] ModifyItemUpdate(InventoryType type, short slot, Item item)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ModifyInventoryItem);
        w.WriteByte(0);              // updateTick=false, matching Java UnlockItem call
        w.WriteByte(2);              // remove + add

        w.WriteByte(3);              // remove
        w.WriteByte((byte)type);
        w.WriteShort(slot);

        w.WriteByte(0);              // add
        w.WriteByte((byte)type);
        w.WriteShort(slot);
        AddItemInfo(w, item);

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

    private static void AddItemInfo(PacketWriter w, Item item)
    {
        w.WriteByte(item.IsEquip ? 1 : 2);
        w.WriteInt(item.ItemId);
        w.WriteByte(0);
        w.WriteLong(GetTime(item.Expiration));

        if (item is Equip equip)
        {
            w.WriteByte(equip.UpgradeSlots);
            w.WriteByte(equip.Level);
            w.WriteShort(equip.Str);
            w.WriteShort(equip.Dex);
            w.WriteShort(equip.Int);
            w.WriteShort(equip.Luk);
            w.WriteShort(equip.Hp);
            w.WriteShort(equip.Mp);
            w.WriteShort(equip.Watk);
            w.WriteShort(equip.Matk);
            w.WriteShort(equip.Wdef);
            w.WriteShort(equip.Mdef);
            w.WriteShort(equip.Acc);
            w.WriteShort(equip.Avoid);
            w.WriteShort(equip.Hands);
            w.WriteShort(equip.Speed);
            w.WriteShort(equip.Jump);
            w.WriteMapleString(equip.Owner);
            w.WriteShort(equip.Flag);
            w.WriteByte(0);
            w.WriteByte(equip.ItemLevel);
            w.WriteInt(equip.ItemExp);
            w.WriteLong(equip.UniqueId);
            w.WriteLong(GetTime(-2));
            w.WriteInt(-1);
            return;
        }

        w.WriteShort(item.Quantity);
        w.WriteMapleString(item.Owner);
        w.WriteShort(item.Flag);
    }

    private static long GetTime(long offset)
    {
        const long KoreanEpochOffset = 116444736000000000L;
        if (offset < 0)
        {
            return KoreanEpochOffset + offset;
        }

        return KoreanEpochOffset + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 10000);
    }
}
