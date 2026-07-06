using Maple.Core.Inventory;

namespace Maple.Core.PlayerShops;

public interface IPlayerShop
{
    int StoreId { get; set; }
    PlayerShopKind Kind { get; }
    PlayerShopStatus Status { get; }
    int OwnerId { get; }
    int OwnerAccountId { get; }
    string OwnerName { get; }
    string Title { get; }
    int ItemId { get; }
    int MapId { get; }
    byte Channel { get; }
    int Mesos { get; }
    DateTimeOffset ExpireAt { get; }
    IReadOnlyList<PlayerShopItemListing> Items { get; }
    IReadOnlyList<string> Blacklist { get; }

    bool IsOwner(int characterId, string name);
    bool IsExpired(DateTimeOffset now);
    void Open(DateTimeOffset now);
    void EnterMaintenance();
    void CloseForClaim(DateTimeOffset now);
    void MarkClosed();
    void AddToBlacklist(string name);
    void RemoveFromBlacklist(string name);
    PlayerShopVisitResult TryEnter(int characterId, string name, DateTimeOffset now);
    void Leave(int characterId);
    PlayerShopAddListingResult TryAddListing(InventoryType type, Item item, short bundles, short bundleQuantity, int price);
    PlayerShopPurchaseResult TryBuy(int listingIndex, short bundleCount, int buyerMeso, bool buyerCanHold, string buyerName, DateTimeOffset now);
    PlayerShopTakeItemResult TryTakeListing(int listingIndex, bool ownerCanHold);
    PlayerShopSettlement CreateSettlement();
    void RemoveSoldOutListings();
    void ClearMesos();
}
