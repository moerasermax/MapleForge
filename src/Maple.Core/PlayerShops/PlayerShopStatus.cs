namespace Maple.Core.PlayerShops;

public enum PlayerShopKind
{
    HiredMerchant,
    PlayerShop,
}

public enum PlayerShopStatus
{
    Draft,
    Open,
    Maintenance,
    PendingClaim,
    Closed,
    Expired,
}

public enum PlayerShopVisitStatus
{
    Success,
    NotOpen,
    Full,
    Blacklisted,
    AlreadyVisiting,
}

public enum PlayerShopAddListingStatus
{
    Success,
    Closed,
    Full,
    InvalidQuantity,
    InvalidPrice,
    RestrictedItem,
}

public enum PlayerShopPurchaseStatus
{
    Success,
    Closed,
    Expired,
    Blacklisted,
    InvalidListing,
    InvalidQuantity,
    SoldOut,
    NotEnoughMeso,
    BuyerInventoryFull,
    TotalPriceOverflow,
    StoreMesoOverflow,
}

public enum PlayerShopTakeItemStatus
{
    Success,
    Closed,
    InvalidListing,
    InvalidQuantity,
    OwnerInventoryFull,
}
