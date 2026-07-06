using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Core.PlayerShops;

public sealed class PlayerShopState
{
    public const int DefaultMaxListings = 16;
    public const int DefaultMaxVisitors = 3;

    private readonly object _sync = new();

    public int StoreId { get; set; }

    public PlayerShopKind Kind { get; set; }

    public PlayerShopStatus Status { get; set; } = PlayerShopStatus.Draft;

    public int OwnerId { get; set; }

    public int OwnerAccountId { get; set; }

    public string OwnerName { get; set; } = string.Empty;

    public int ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int MapId { get; set; }

    public byte Channel { get; set; }

    public short X { get; set; }

    public short Y { get; set; }

    public byte Stance { get; set; }

    public short Foothold { get; set; }

    public Position Position
    {
        get => new(X, Y, Stance, Foothold);
        set
        {
            X = value.X;
            Y = value.Y;
            Stance = value.Stance;
            Foothold = value.Foothold;
        }
    }

    public int Mesos { get; set; }

    public DateTimeOffset OpenedAt { get; set; }

    public DateTimeOffset ExpireAt { get; set; }

    public int MaxListings { get; set; } = DefaultMaxListings;

    public int MaxVisitors { get; set; } = DefaultMaxVisitors;

    public List<PlayerShopItemListing> Items { get; set; } = new();

    public List<PlayerShopVisitor> Visitors { get; set; } = new();

    public List<string> Blacklist { get; set; } = new();

    public List<PlayerShopPurchaseLog> PurchaseLogs { get; set; } = new();

    public bool IsOwner(int characterId, string name)
        => characterId == OwnerId && string.Equals(name, OwnerName, StringComparison.Ordinal);

    public bool IsExpired(DateTimeOffset now)
        => ExpireAt != default && now >= ExpireAt;

    public void Open(DateTimeOffset now)
    {
        lock (_sync)
        {
            if (OpenedAt == default)
            {
                OpenedAt = now;
            }

            if (Status is PlayerShopStatus.Draft or PlayerShopStatus.Maintenance)
            {
                Status = PlayerShopStatus.Open;
            }
        }
    }

    public void EnterMaintenance()
    {
        lock (_sync)
        {
            if (Status == PlayerShopStatus.Open)
            {
                Status = PlayerShopStatus.Maintenance;
            }
        }
    }

    public void CloseForClaim(DateTimeOffset now)
    {
        lock (_sync)
        {
            Status = IsExpired(now) ? PlayerShopStatus.Expired : PlayerShopStatus.PendingClaim;
            Visitors.Clear();
        }
    }

    public void MarkClosed()
    {
        lock (_sync)
        {
            Status = PlayerShopStatus.Closed;
            Visitors.Clear();
        }
    }

    public void AddToBlacklist(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        lock (_sync)
        {
            if (!Blacklist.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Blacklist.Add(name);
            }

            Visitors.RemoveAll(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveFromBlacklist(string name)
    {
        lock (_sync)
        {
            Blacklist.RemoveAll(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsBlacklisted(string name)
        => Blacklist.Contains(name, StringComparer.OrdinalIgnoreCase);

    public PlayerShopVisitResult TryEnter(int characterId, string name, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (Status != PlayerShopStatus.Open)
            {
                return new PlayerShopVisitResult(PlayerShopVisitStatus.NotOpen);
            }

            if (IsBlacklisted(name))
            {
                return new PlayerShopVisitResult(PlayerShopVisitStatus.Blacklisted);
            }

            if (Visitors.Any(v => v.CharacterId == characterId))
            {
                return new PlayerShopVisitResult(PlayerShopVisitStatus.AlreadyVisiting);
            }

            var slot = FirstFreeVisitorSlot();
            if (slot <= 0)
            {
                return new PlayerShopVisitResult(PlayerShopVisitStatus.Full);
            }

            Visitors.Add(new PlayerShopVisitor(characterId, name, slot, now));
            return new PlayerShopVisitResult(PlayerShopVisitStatus.Success, slot);
        }
    }

    public void Leave(int characterId)
    {
        lock (_sync)
        {
            Visitors.RemoveAll(v => v.CharacterId == characterId);
        }
    }

    public PlayerShopAddListingResult TryAddListing(
        InventoryType type,
        Item item,
        short bundles,
        short bundleQuantity,
        int price)
    {
        lock (_sync)
        {
            if (Status is PlayerShopStatus.PendingClaim or PlayerShopStatus.Closed or PlayerShopStatus.Expired)
            {
                return new PlayerShopAddListingResult(PlayerShopAddListingStatus.Closed);
            }

            if (Items.Count >= MaxListings)
            {
                return new PlayerShopAddListingResult(PlayerShopAddListingStatus.Full);
            }

            if (bundles <= 0 || bundleQuantity <= 0)
            {
                return new PlayerShopAddListingResult(PlayerShopAddListingStatus.InvalidQuantity);
            }

            var totalQuantity = (long)bundles * bundleQuantity;
            if (totalQuantity <= 0 || totalQuantity > short.MaxValue)
            {
                return new PlayerShopAddListingResult(PlayerShopAddListingStatus.InvalidQuantity);
            }

            if (price <= 0)
            {
                return new PlayerShopAddListingResult(PlayerShopAddListingStatus.InvalidPrice);
            }

            if (ItemFlags.Has(item.Flag, ItemFlags.Lock) || ItemFlags.Has(item.Flag, ItemFlags.Untradeable))
            {
                return new PlayerShopAddListingResult(PlayerShopAddListingStatus.RestrictedItem);
            }

            if (item.IsEquip && bundleQuantity != 1)
            {
                return new PlayerShopAddListingResult(PlayerShopAddListingStatus.InvalidQuantity);
            }

            var listingItem = item.Copy();
            listingItem.Slot = 0;
            listingItem.Quantity = item.IsEquip ? (short)1 : bundleQuantity;

            var listing = new PlayerShopItemListing
            {
                ListingId = NextListingId(),
                InventoryType = (byte)type,
                Item = ItemRecord.From(type, listingItem),
                Bundles = bundles,
                BundleQuantity = listingItem.Quantity,
                Price = price,
            };

            Items.Add(listing);
            return new PlayerShopAddListingResult(PlayerShopAddListingStatus.Success, listing);
        }
    }

    public PlayerShopPurchaseResult TryBuy(
        int listingIndex,
        short bundleCount,
        int buyerMeso,
        bool buyerCanHold,
        string buyerName,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            if (IsExpired(now))
            {
                Status = PlayerShopStatus.Expired;
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.Expired);
            }

            if (Status != PlayerShopStatus.Open)
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.Closed);
            }

            if (IsBlacklisted(buyerName))
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.Blacklisted);
            }

            if (listingIndex < 0 || listingIndex >= Items.Count)
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.InvalidListing);
            }

            if (bundleCount <= 0)
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.InvalidQuantity);
            }

            var listing = Items[listingIndex];
            if (listing.Bundles < bundleCount)
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.SoldOut);
            }

            var totalPrice = (long)listing.Price * bundleCount;
            var totalQuantity = (long)listing.BundleQuantity * bundleCount;
            if (totalPrice <= 0 || totalPrice > int.MaxValue || totalQuantity <= 0 || totalQuantity > short.MaxValue)
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.TotalPriceOverflow);
            }

            if (buyerMeso < totalPrice)
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.NotEnoughMeso);
            }

            if (!buyerCanHold)
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.BuyerInventoryFull);
            }

            var grossMesos = (long)Mesos + totalPrice;
            if (grossMesos > int.MaxValue)
            {
                return new PlayerShopPurchaseResult(PlayerShopPurchaseStatus.StoreMesoOverflow);
            }

            listing.Bundles -= bundleCount;
            var tax = EntrustedStoreTax((int)grossMesos);
            Mesos = (int)grossMesos - tax;
            var bought = listing.CreateItemForBundles(bundleCount);
            PurchaseLogs.Add(new PlayerShopPurchaseLog(
                bought.ItemId,
                bought.Quantity,
                (int)totalPrice,
                buyerName,
                now));

            return new PlayerShopPurchaseResult(
                PlayerShopPurchaseStatus.Success,
                (InventoryType)listing.InventoryType,
                bought,
                (int)totalPrice,
                tax,
                Mesos,
                listing.Bundles);
        }
    }

    public PlayerShopTakeItemResult TryTakeListing(int listingIndex, bool ownerCanHold)
    {
        lock (_sync)
        {
            if (Status is PlayerShopStatus.PendingClaim or PlayerShopStatus.Closed or PlayerShopStatus.Expired)
            {
                return new PlayerShopTakeItemResult(PlayerShopTakeItemStatus.Closed);
            }

            if (listingIndex < 0 || listingIndex >= Items.Count)
            {
                return new PlayerShopTakeItemResult(PlayerShopTakeItemStatus.InvalidListing);
            }

            if (!ownerCanHold)
            {
                return new PlayerShopTakeItemResult(PlayerShopTakeItemStatus.OwnerInventoryFull);
            }

            var listing = Items[listingIndex];
            if (listing.Bundles <= 0)
            {
                Items.RemoveAt(listingIndex);
                return new PlayerShopTakeItemResult(PlayerShopTakeItemStatus.InvalidQuantity);
            }

            var totalQuantity = (long)listing.BundleQuantity * listing.Bundles;
            if (totalQuantity <= 0 || totalQuantity > short.MaxValue)
            {
                return new PlayerShopTakeItemResult(PlayerShopTakeItemStatus.InvalidQuantity);
            }

            var item = listing.CreateRemainingItem();
            var type = (InventoryType)listing.InventoryType;
            Items.RemoveAt(listingIndex);
            return new PlayerShopTakeItemResult(PlayerShopTakeItemStatus.Success, type, item);
        }
    }

    public PlayerShopSettlement CreateSettlement()
    {
        lock (_sync)
        {
            var remaining = new List<(InventoryType Type, Item Item)>();
            foreach (var listing in Items)
            {
                if (listing.Bundles <= 0)
                {
                    continue;
                }

                remaining.Add(((InventoryType)listing.InventoryType, listing.CreateRemainingItem()));
            }

            return new PlayerShopSettlement(remaining, Mesos);
        }
    }

    public void RemoveSoldOutListings()
    {
        lock (_sync)
        {
            Items.RemoveAll(static item => item.Bundles <= 0);
        }
    }

    public void ClearMesos() => Mesos = 0;

    public static int EntrustedStoreTax(int meso)
    {
        if (meso >= 100_000_000) return (int)Math.Round(0.03 * meso);
        if (meso >= 25_000_000) return (int)Math.Round(0.025 * meso);
        if (meso >= 10_000_000) return (int)Math.Round(0.02 * meso);
        if (meso >= 5_000_000) return (int)Math.Round(0.015 * meso);
        if (meso >= 1_000_000) return (int)Math.Round(0.009 * meso);
        if (meso >= 100_000) return (int)Math.Round(0.004 * meso);
        return 0;
    }

    private byte FirstFreeVisitorSlot()
    {
        for (byte slot = 1; slot <= MaxVisitors; slot++)
        {
            if (Visitors.All(v => v.Slot != slot))
            {
                return slot;
            }
        }

        return 0;
    }

    private int NextListingId()
        => Items.Count == 0 ? 1 : Items.Max(static item => item.ListingId) + 1;
}
