using Maple.Core.Inventory;

namespace Maple.Core.PlayerShops;

public sealed class PlayerShop : IPlayerShop
{
    public PlayerShopState State { get; set; } = new() { Kind = PlayerShopKind.PlayerShop };

    public int StoreId
    {
        get => State.StoreId;
        set => State.StoreId = value;
    }

    public PlayerShopKind Kind => State.Kind;
    public PlayerShopStatus Status => State.Status;
    public int OwnerId => State.OwnerId;
    public int OwnerAccountId => State.OwnerAccountId;
    public string OwnerName => State.OwnerName;
    public string Title => State.Title;
    public int ItemId => State.ItemId;
    public int MapId => State.MapId;
    public byte Channel => State.Channel;
    public int Mesos => State.Mesos;
    public DateTimeOffset ExpireAt => State.ExpireAt;
    public IReadOnlyList<PlayerShopItemListing> Items => State.Items;
    public IReadOnlyList<string> Blacklist => State.Blacklist;

    public static PlayerShop Create(
        int ownerId,
        int ownerAccountId,
        string ownerName,
        int itemId,
        string title,
        int mapId,
        byte channel)
        => new()
        {
            State = new PlayerShopState
            {
                Kind = PlayerShopKind.PlayerShop,
                OwnerId = ownerId,
                OwnerAccountId = ownerAccountId,
                OwnerName = ownerName,
                ItemId = itemId,
                Title = title,
                MapId = mapId,
                Channel = channel,
            },
        };

    public bool IsOwner(int characterId, string name) => State.IsOwner(characterId, name);
    public bool IsExpired(DateTimeOffset now) => false;
    public void Open(DateTimeOffset now) => State.Open(now);
    public void EnterMaintenance() => State.EnterMaintenance();
    public void CloseForClaim(DateTimeOffset now) => State.CloseForClaim(now);
    public void MarkClosed() => State.MarkClosed();
    public void AddToBlacklist(string name) => State.AddToBlacklist(name);
    public void RemoveFromBlacklist(string name) => State.RemoveFromBlacklist(name);
    public PlayerShopVisitResult TryEnter(int characterId, string name, DateTimeOffset now) => State.TryEnter(characterId, name, now);
    public void Leave(int characterId) => State.Leave(characterId);
    public PlayerShopAddListingResult TryAddListing(InventoryType type, Item item, short bundles, short bundleQuantity, int price)
        => State.TryAddListing(type, item, bundles, bundleQuantity, price);
    public PlayerShopPurchaseResult TryBuy(int listingIndex, short bundleCount, int buyerMeso, bool buyerCanHold, string buyerName, DateTimeOffset now)
        => State.TryBuy(listingIndex, bundleCount, buyerMeso, buyerCanHold, buyerName, now);
    public PlayerShopTakeItemResult TryTakeListing(int listingIndex, bool ownerCanHold) => State.TryTakeListing(listingIndex, ownerCanHold);
    public PlayerShopSettlement CreateSettlement() => State.CreateSettlement();
    public void RemoveSoldOutListings() => State.RemoveSoldOutListings();
    public void ClearMesos() => State.ClearMesos();
}
