using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.Shops;

namespace Maple.Adapters.V113.Channel;

internal enum V113NpcShopAction : byte
{
    Buy = 0,
    Sell = 1,
    Recharge = 2,
}

internal readonly record struct V113NpcShopRequest(
    V113NpcShopAction Action,
    short Slot,
    int ItemId,
    short Quantity);

/// <summary>
/// v113 NPC 商店封包。對照 Java NPCHandler.handleNPCShop / MaplePacketCreator.getNPCShop。
/// </summary>
internal static class V113ShopPackets
{
    public const short RecvNpcShop = 0x36;
    public const short SendOpenNpcShop = 0x13D;
    public const short SendConfirmShopTransaction = 0x13E;
    public const short SendUpdateStats = 0x1D;

    public const byte ConfirmBuy = 0x00;
    public const byte ConfirmSell = 0x08;
    public const byte ConfirmError = 0x20;

    private const int MesoStat = 0x40000;

    public static V113NpcShopRequest ParseNpcShop(PacketReader reader)
    {
        var action = (V113NpcShopAction)reader.ReadByte();
        return action switch
        {
            V113NpcShopAction.Buy => ParseBuy(reader),
            V113NpcShopAction.Sell => ParseSell(reader),
            V113NpcShopAction.Recharge => new V113NpcShopRequest(action, reader.ReadShort(), 0, 0),
            _ => new V113NpcShopRequest(action, 0, 0, 0),
        };
    }

    public static byte[] OpenNpcShop(ShopDefinition shop)
    {
        var w = new PacketWriter();
        w.WriteShort(SendOpenNpcShop);
        w.WriteInt(shop.NpcId);
        w.WriteShort(shop.Items.Count);

        foreach (var item in shop.Items)
        {
            w.WriteInt(item.ItemId);
            w.WriteInt(item.Price);
            w.WriteShort(1);
            w.WriteShort(item.Buyable);
        }

        return w.ToArray();
    }

    public static byte[] ConfirmShopTransaction(byte code)
    {
        var w = new PacketWriter(3);
        w.WriteShort(SendConfirmShopTransaction);
        w.WriteByte(code);
        return w.ToArray();
    }

    public static byte[] UpdateMeso(int meso, bool itemReaction = false)
    {
        var w = new PacketWriter(11);
        w.WriteShort(SendUpdateStats);
        w.WriteByte(itemReaction ? 1 : 0);
        w.WriteInt(MesoStat);
        w.WriteInt(meso);
        return w.ToArray();
    }

    public static byte[] ModifyInventoryAdd(InventoryType type, Item item)
    {
        var w = BeginSingleInventoryModify(mode: 0, type, item.Slot);
        AddItemInfo(w, item);
        return w.ToArray();
    }

    public static byte[] ModifyInventoryQuantity(ShopInventoryMutation mutation)
    {
        var mode = mutation.Removed ? 3 : 1;
        var w = BeginSingleInventoryModify(mode, mutation.Type, mutation.Slot);
        if (!mutation.Removed)
        {
            w.WriteShort(mutation.NewQuantity);
        }

        return w.ToArray();
    }

    private static V113NpcShopRequest ParseBuy(PacketReader reader)
    {
        reader.Skip(2);
        var itemId = reader.ReadInt();
        var quantity = reader.ReadShort();
        return new V113NpcShopRequest(V113NpcShopAction.Buy, 0, itemId, quantity);
    }

    private static V113NpcShopRequest ParseSell(PacketReader reader)
    {
        var slot = reader.ReadShort();
        var itemId = reader.ReadInt();
        var quantity = reader.ReadShort();
        return new V113NpcShopRequest(V113NpcShopAction.Sell, slot, itemId, quantity);
    }

    private static PacketWriter BeginSingleInventoryModify(int mode, InventoryType type, short slot)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.ModifyInventoryItem);
        w.WriteByte(0);
        w.WriteByte(1);
        w.WriteByte(mode);
        w.WriteByte((byte)type);
        w.WriteShort(slot);
        return w;
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
        }
        else
        {
            w.WriteShort(item.Quantity);
            w.WriteMapleString(item.Owner);
            w.WriteShort(item.Flag);
        }
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
