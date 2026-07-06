using Maple.Core.Inventory;

namespace Maple.Core.PlayerShops;

public sealed class PlayerShopItemListing
{
    public int ListingId { get; set; }

    public byte InventoryType { get; set; }

    public ItemRecord Item { get; set; } = new();

    public short Bundles { get; set; }

    public short BundleQuantity { get; set; }

    public int Price { get; set; }

    public bool SoldOut => Bundles <= 0;

    public Item CreateItemForBundles(short bundleCount)
    {
        var item = Item.ToItem();
        item.Slot = 0;
        item.Quantity = item.IsEquip ? (short)1 : checked((short)(BundleQuantity * bundleCount));
        item.Flag = ItemFlags.Clear(ItemFlags.Clear(item.Flag, ItemFlags.KarmaEquip), ItemFlags.KarmaUse);
        return item;
    }

    public Item CreateRemainingItem()
    {
        var item = Item.ToItem();
        item.Slot = 0;
        item.Quantity = item.IsEquip ? (short)Bundles : checked((short)(BundleQuantity * Bundles));
        return item;
    }
}

public sealed record PlayerShopVisitor(int CharacterId, string Name, byte Slot, DateTimeOffset EnteredAt);

public sealed record PlayerShopPurchaseLog(int ItemId, int Quantity, int TotalPrice, string Buyer, DateTimeOffset BoughtAt);

public sealed record PlayerShopVisitResult(PlayerShopVisitStatus Status, byte Slot = 0);

public sealed record PlayerShopAddListingResult(PlayerShopAddListingStatus Status, PlayerShopItemListing? Listing = null);

public sealed record PlayerShopPurchaseResult(
    PlayerShopPurchaseStatus Status,
    InventoryType? InventoryType = null,
    Item? Item = null,
    int TotalPrice = 0,
    int Tax = 0,
    int MerchantMesos = 0,
    int RemainingBundles = 0);

public sealed record PlayerShopTakeItemResult(
    PlayerShopTakeItemStatus Status,
    InventoryType? InventoryType = null,
    Item? Item = null);

public sealed record PlayerShopSettlement(IReadOnlyList<(InventoryType Type, Item Item)> Items, int Mesos);
