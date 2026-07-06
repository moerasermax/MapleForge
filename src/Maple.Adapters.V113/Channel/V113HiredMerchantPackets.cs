using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.IO;
using Maple.Core.PlayerShops;
using Maple.Core.World;

namespace Maple.Adapters.V113.Channel;

/// <summary>
/// Hired merchant s2c packets. Java-source candidates from PlayerShopPacket.java; all layouts here are unverified
/// until captured against the v113 client.
/// </summary>
internal static class V113HiredMerchantPackets
{
    public const byte HiredMerchantShopType = 1;
    public const byte MerchItemStoreOpenPackage = 0x23;
    public const byte MerchItemStoreConfirmTakeOut = 0x24;
    public const byte MerchItemStoreNoPackage = 0x25;
    public const byte MerchItemClaimSuccess = 0x1D;
    public const byte MerchItemClaimInventoryFull = 0x21;

    /// <summary>Unverified: PlayerShopPacket.sendTitleBox().</summary>
    public static byte[] TitleBox()
    {
        var w = new PacketWriter(3);
        w.WriteShort(V113ChannelSendOp.EntrustedShopCheckResult);
        w.WriteByte(7);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.getHiredMerch(chr, merch, firstTime), owner/empty-visitor subset.</summary>
    public static byte[] OpenHiredMerchant(Player viewer, HiredMerchant merchant, bool firstTime, DateTimeOffset now)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.PlayerInteraction);
        w.WriteByte(5);
        w.WriteByte(5);
        w.WriteByte(4);
        w.WriteByte(merchant.IsOwner(viewer.Character.Id, viewer.Character.Name) ? 0 : 1);
        w.WriteByte(0);
        w.WriteInt(merchant.ItemId);
        w.WriteMapleString("精靈商人");

        w.WriteByte(0xFF);
        w.WriteShort(0);
        w.WriteMapleString(merchant.OwnerName);

        if (merchant.IsOwner(viewer.Character.Id, viewer.Character.Name))
        {
            w.WriteInt(ElapsedSeconds(merchant, now));
            w.WriteByte(firstTime ? 1 : 0);
            w.WriteByte(merchant.State.PurchaseLogs.Count);
            foreach (var sold in merchant.State.PurchaseLogs)
            {
                w.WriteInt(sold.ItemId);
                w.WriteShort(sold.Quantity);
                w.WriteInt(sold.TotalPrice);
                w.WriteMapleString(sold.Buyer);
            }

            w.WriteInt(merchant.Mesos);
        }

        w.WriteMapleString(merchant.Title);
        w.WriteByte(10);
        w.WriteInt(merchant.Mesos);
        WriteListings(w, merchant.Items);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.spawnHiredMerchant(hm).</summary>
    public static byte[] SpawnHiredMerchant(HiredMerchant merchant, Position position)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.SpawnHiredMerchant);
        w.WriteInt(merchant.OwnerId);
        w.WriteInt(merchant.ItemId);
        WritePosition(w, position);
        w.WriteShort(0);
        w.WriteMapleString(merchant.OwnerName);
        AddInteraction(w, merchant);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.destroyHiredMerchant(id).</summary>
    public static byte[] DestroyHiredMerchant(int ownerId)
    {
        var w = new PacketWriter(6);
        w.WriteShort(V113ChannelSendOp.DestroyHiredMerchant);
        w.WriteInt(ownerId);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.updateHiredMerchant(shop).</summary>
    public static byte[] UpdateHiredMerchant(HiredMerchant merchant)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.UpdateHiredMerchant);
        w.WriteInt(merchant.OwnerId);
        AddInteraction(w, merchant);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.shopItemUpdate(shop).</summary>
    public static byte[] ShopItemUpdate(HiredMerchant merchant)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.PlayerInteraction);
        w.WriteByte(0x16);
        w.WriteInt(0);
        WriteListings(w, merchant.Items);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.shopVisitorAdd(chr, slot).</summary>
    public static byte[] ShopVisitorAdd(Character visitor, int slot)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.PlayerInteraction);
        w.WriteByte(4);
        w.WriteByte(slot);
        AddCharLook(w, visitor);
        w.WriteMapleString(visitor.Name);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.shopVisitorLeave(slot).</summary>
    public static byte[] ShopVisitorLeave(byte slot)
    {
        var w = new PacketWriter(4);
        w.WriteShort(V113ChannelSendOp.PlayerInteraction);
        w.WriteByte(0x0A);
        w.WriteByte(slot);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.Merchant_Buy_Error(message).</summary>
    public static byte[] MerchantBuyError(byte message)
    {
        var w = new PacketWriter(4);
        w.WriteShort(V113ChannelSendOp.PlayerInteraction);
        w.WriteByte(0x17);
        w.WriteByte(message);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.ShowMerchItemStore(npc, mapid, ch).</summary>
    public static byte[] ShowMerchItemStore(int npcId, int mapId, int channel)
    {
        var w = new PacketWriter(12);
        w.WriteShort(V113ChannelSendOp.MerchItemStore);
        w.WriteByte(MerchItemStoreNoPackage);
        w.WriteInt(npcId);
        w.WriteInt(mapId);
        w.WriteByte(Math.Max(0, channel - 1));
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.merchItemStore(op).</summary>
    public static byte[] MerchItemStore(byte op)
    {
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.MerchItemStore);
        w.WriteByte(op);

        switch (op)
        {
            case MerchItemStoreConfirmTakeOut:
                w.WriteZeroBytes(8);
                break;
            case MerchItemStoreNoPackage:
                w.WriteInt(9030000);
                w.WriteInt(Character.EmptyRockMapId);
                w.WriteByte(0);
                break;
            default:
                w.WriteByte(0);
                break;
        }

        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.merchItemStore_ItemData(pack), mapped from a pending HiredMerchant package.</summary>
    public static byte[] MerchItemStoreItemData(HiredMerchant merchant)
    {
        var settlement = merchant.CreateSettlement();
        var w = new PacketWriter();
        w.WriteShort(V113ChannelSendOp.MerchItemStore);
        w.WriteByte(MerchItemStoreOpenPackage);
        w.WriteInt(9030000);
        w.WriteInt(merchant.StoreId);
        w.WriteZeroBytes(5);
        w.WriteInt(settlement.Mesos);
        w.WriteByte(0);
        w.WriteByte(settlement.Items.Count);

        foreach (var (_, item) in settlement.Items)
        {
            AddItemInfo(w, item);
        }

        w.WriteZeroBytes(3);
        return w.ToArray();
    }

    /// <summary>Unverified: PlayerShopPacket.merchItem_Message(op).</summary>
    public static byte[] MerchItemMessage(byte op)
    {
        var w = new PacketWriter(3);
        w.WriteShort(V113ChannelSendOp.MerchItemMessage);
        w.WriteByte(op);
        return w.ToArray();
    }

    private static void WriteListings(PacketWriter w, IReadOnlyList<PlayerShopItemListing> items)
    {
        w.WriteByte(items.Count);
        foreach (var listing in items)
        {
            w.WriteShort(listing.Bundles);
            w.WriteShort(listing.BundleQuantity);
            w.WriteInt(listing.Price);
            AddItemInfo(w, listing.Item.ToItem());
        }
    }

    private static void AddInteraction(PacketWriter w, HiredMerchant merchant)
    {
        w.WriteByte(HiredMerchantShopType);
        w.WriteMapleString(merchant.Title);
        w.WriteByte(merchant.Items.Count);
        w.WriteByte(merchant.State.Visitors.Count);
    }

    private static void WritePosition(PacketWriter w, Position position)
    {
        w.WriteShort(position.X);
        w.WriteShort(position.Y);
    }

    private static int ElapsedSeconds(HiredMerchant merchant, DateTimeOffset now)
    {
        if (merchant.State.OpenedAt == default || now <= merchant.State.OpenedAt)
        {
            return 0;
        }

        return (int)Math.Min(int.MaxValue, (now - merchant.State.OpenedAt).TotalSeconds);
    }

    private static void AddCharLook(PacketWriter w, Character chr)
    {
        w.WriteByte(chr.Gender);
        w.WriteByte(chr.SkinColor);
        w.WriteInt(chr.Face);
        w.WriteByte(0);
        w.WriteInt(chr.Hair);

        foreach (var equip in chr.Equips.Where(static e => e.Position < 0 && e.Position > -100))
        {
            w.WriteByte((byte)(-equip.Position));
            w.WriteInt(equip.ItemId);
        }

        w.WriteByte(0xFF);
        w.WriteByte(0xFF);
        w.WriteInt(0);
        w.WriteInt(0);
        w.WriteLong(0);
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
