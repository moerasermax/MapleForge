using LiteDB;
using Maple.Core.PlayerShops;

namespace Maple.Persistence.PlayerShops;

public sealed class LiteDbHiredMerchantRepository : IHiredMerchantRepository
{
    private readonly ILiteCollection<HiredMerchantDocument> _merchants;
    private readonly ILiteCollection<HiredMerchantItemDocument> _items;

    public LiteDbHiredMerchantRepository(LiteDatabase db)
    {
        _merchants = db.GetCollection<HiredMerchantDocument>("hired_merchants");
        _items = db.GetCollection<HiredMerchantItemDocument>("hired_merchant_items");
        _merchants.EnsureIndex(m => m.OwnerAccountId);
        _merchants.EnsureIndex(m => m.OwnerId);
        _merchants.EnsureIndex(m => m.Status);
        _merchants.EnsureIndex(m => m.MapId);
        _merchants.EnsureIndex(m => m.Channel);
        _merchants.EnsureIndex(m => m.ExpireAtUnixMillis);
        _items.EnsureIndex(i => i.StoreId);
    }

    public Task<int> AddAsync(HiredMerchant merchant, CancellationToken cancellationToken = default)
    {
        var document = HiredMerchantDocumentMapper.ToDocument(merchant);
        var id = _merchants.Insert(document).AsInt32;
        merchant.StoreId = id;
        SaveItems(merchant);
        return Task.FromResult(id);
    }

    public Task UpsertAsync(HiredMerchant merchant, CancellationToken cancellationToken = default)
    {
        if (merchant.StoreId <= 0)
        {
            return AddAsync(merchant, cancellationToken);
        }

        _merchants.Upsert(HiredMerchantDocumentMapper.ToDocument(merchant));
        SaveItems(merchant);
        return Task.CompletedTask;
    }

    public Task<HiredMerchant?> FindByStoreIdAsync(int storeId, CancellationToken cancellationToken = default)
    {
        var document = _merchants.FindById(storeId);
        return Task.FromResult(document is null ? null : Hydrate(document));
    }

    public Task<HiredMerchant?> FindOpenByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default)
    {
        var document = _merchants.FindOne(m =>
            m.OwnerAccountId == ownerAccountId &&
            m.OwnerId == ownerId &&
            (m.Status == PlayerShopStatus.Draft ||
             m.Status == PlayerShopStatus.Open ||
             m.Status == PlayerShopStatus.Maintenance));
        return Task.FromResult(document is null ? null : Hydrate(document));
    }

    public Task<HiredMerchant?> FindClaimableByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default)
    {
        var document = _merchants.FindOne(m =>
            m.OwnerAccountId == ownerAccountId &&
            m.OwnerId == ownerId &&
            (m.Status == PlayerShopStatus.PendingClaim ||
             m.Status == PlayerShopStatus.Expired));
        return Task.FromResult(document is null ? null : Hydrate(document));
    }

    public Task<IReadOnlyList<HiredMerchant>> FindOpenByMapAsync(byte channel, int mapId, CancellationToken cancellationToken = default)
    {
        var list = _merchants
            .Find(m => m.Channel == channel && m.MapId == mapId && m.Status == PlayerShopStatus.Open)
            .Select(Hydrate)
            .ToList();
        return Task.FromResult<IReadOnlyList<HiredMerchant>>(list);
    }

    public Task<IReadOnlyList<HiredMerchant>> FindExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var nowUnixMillis = now.ToUnixTimeMilliseconds();
        var list = _merchants
            .Find(m =>
                (m.Status == PlayerShopStatus.Open || m.Status == PlayerShopStatus.Maintenance) &&
                m.ExpireAtUnixMillis > 0 &&
                m.ExpireAtUnixMillis <= nowUnixMillis)
            .Select(Hydrate)
            .ToList();
        return Task.FromResult<IReadOnlyList<HiredMerchant>>(list);
    }

    public Task<bool> DeleteAsync(int storeId, CancellationToken cancellationToken = default)
    {
        _items.DeleteMany(i => i.StoreId == storeId);
        return Task.FromResult(_merchants.Delete(storeId));
    }

    private HiredMerchant Hydrate(HiredMerchantDocument document)
    {
        var items = _items.Find(i => i.StoreId == document.StoreId).ToList();
        return HiredMerchantDocumentMapper.ToDomain(document, items);
    }

    private void SaveItems(HiredMerchant merchant)
    {
        _items.DeleteMany(i => i.StoreId == merchant.StoreId);
        var items = HiredMerchantDocumentMapper.ToItemDocuments(merchant);
        if (items.Count > 0)
        {
            _items.InsertBulk(items);
        }
    }
}
