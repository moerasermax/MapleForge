using Maple.Core.Inventory;
using Maple.Core.PlayerShops;

namespace Maple.Persistence.PlayerShops;

internal sealed class HiredMerchantDocument
{
    [LiteDB.BsonId]
    [MongoDB.Bson.Serialization.Attributes.BsonId]
    public int StoreId { get; set; }

    public int OwnerId { get; set; }

    public int OwnerAccountId { get; set; }

    public string OwnerName { get; set; } = string.Empty;

    public int ItemId { get; set; }

    public string Title { get; set; } = string.Empty;

    public int MapId { get; set; }

    public byte Channel { get; set; }

    public int Mesos { get; set; }

    public PlayerShopStatus Status { get; set; }

    public long OpenedAtUnixMillis { get; set; }

    public long ExpireAtUnixMillis { get; set; }

    public int MaxListings { get; set; }

    public int MaxVisitors { get; set; }

    public List<string> Blacklist { get; set; } = new();
}

internal sealed class HiredMerchantItemDocument
{
    [LiteDB.BsonId]
    [MongoDB.Bson.Serialization.Attributes.BsonId]
    public string Id { get; set; } = string.Empty;

    public int StoreId { get; set; }

    public int ListingId { get; set; }

    public byte InventoryType { get; set; }

    public ItemRecord Item { get; set; } = new();

    public short Bundles { get; set; }

    public short BundleQuantity { get; set; }

    public int Price { get; set; }
}

internal static class HiredMerchantDocumentMapper
{
    public static HiredMerchantDocument ToDocument(HiredMerchant merchant)
        => new()
        {
            StoreId = merchant.StoreId,
            OwnerId = merchant.OwnerId,
            OwnerAccountId = merchant.OwnerAccountId,
            OwnerName = merchant.OwnerName,
            ItemId = merchant.ItemId,
            Title = merchant.Title,
            MapId = merchant.MapId,
            Channel = merchant.Channel,
            Mesos = merchant.Mesos,
            Status = merchant.Status,
            OpenedAtUnixMillis = ToUnixMillis(merchant.State.OpenedAt),
            ExpireAtUnixMillis = ToUnixMillis(merchant.State.ExpireAt),
            MaxListings = merchant.State.MaxListings,
            MaxVisitors = merchant.State.MaxVisitors,
            Blacklist = merchant.State.Blacklist.ToList(),
        };

    public static IReadOnlyList<HiredMerchantItemDocument> ToItemDocuments(HiredMerchant merchant)
        => merchant.State.Items
            .Select(item => new HiredMerchantItemDocument
            {
                Id = ItemDocumentId(merchant.StoreId, item.ListingId),
                StoreId = merchant.StoreId,
                ListingId = item.ListingId,
                InventoryType = item.InventoryType,
                Item = item.Item,
                Bundles = item.Bundles,
                BundleQuantity = item.BundleQuantity,
                Price = item.Price,
            })
            .ToList();

    public static HiredMerchant ToDomain(HiredMerchantDocument document, IEnumerable<HiredMerchantItemDocument> itemDocuments)
    {
        var state = new PlayerShopState
        {
            StoreId = document.StoreId,
            Kind = PlayerShopKind.HiredMerchant,
            OwnerId = document.OwnerId,
            OwnerAccountId = document.OwnerAccountId,
            OwnerName = document.OwnerName,
            ItemId = document.ItemId,
            Title = document.Title,
            MapId = document.MapId,
            Channel = document.Channel,
            Mesos = document.Mesos,
            Status = document.Status,
            OpenedAt = FromUnixMillis(document.OpenedAtUnixMillis),
            ExpireAt = FromUnixMillis(document.ExpireAtUnixMillis),
            MaxListings = document.MaxListings <= 0 ? PlayerShopState.DefaultMaxListings : document.MaxListings,
            MaxVisitors = document.MaxVisitors <= 0 ? PlayerShopState.DefaultMaxVisitors : document.MaxVisitors,
            Blacklist = document.Blacklist.ToList(),
            Items = itemDocuments
                .OrderBy(static item => item.ListingId)
                .Select(static item => new PlayerShopItemListing
                {
                    ListingId = item.ListingId,
                    InventoryType = item.InventoryType,
                    Item = item.Item,
                    Bundles = item.Bundles,
                    BundleQuantity = item.BundleQuantity,
                    Price = item.Price,
                })
                .ToList(),
        };

        return HiredMerchant.FromState(state);
    }

    public static string ItemDocumentId(int storeId, int listingId) => $"{storeId}:{listingId}";

    private static long ToUnixMillis(DateTimeOffset value)
        => value == default ? 0 : value.ToUnixTimeMilliseconds();

    private static DateTimeOffset FromUnixMillis(long value)
        => value <= 0 ? default : DateTimeOffset.FromUnixTimeMilliseconds(value);
}
