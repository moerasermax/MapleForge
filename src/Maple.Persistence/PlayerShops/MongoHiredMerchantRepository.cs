using Maple.Core.PlayerShops;
using MongoDB.Driver;

namespace Maple.Persistence.PlayerShops;

public sealed class MongoHiredMerchantRepository : IHiredMerchantRepository
{
    private const string MerchantCollectionName = "hired_merchants";
    private const string ItemCollectionName = "hired_merchant_items";
    private const string SequenceName = "hired_merchants";

    private readonly IMongoCollection<HiredMerchantDocument> _merchants;
    private readonly IMongoCollection<HiredMerchantItemDocument> _items;
    private readonly MongoSequenceGenerator _sequences;

    public MongoHiredMerchantRepository(IMongoDatabase database, MongoSequenceGenerator sequences)
    {
        _merchants = database.GetCollection<HiredMerchantDocument>(MerchantCollectionName);
        _items = database.GetCollection<HiredMerchantItemDocument>(ItemCollectionName);
        _sequences = sequences;

        _merchants.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<HiredMerchantDocument>(
                Builders<HiredMerchantDocument>.IndexKeys
                    .Ascending(m => m.OwnerAccountId)
                    .Ascending(m => m.OwnerId)
                    .Ascending(m => m.Status),
                new CreateIndexOptions { Name = "ix_hired_merchants_owner_status" }),
            new CreateIndexModel<HiredMerchantDocument>(
                Builders<HiredMerchantDocument>.IndexKeys
                    .Ascending(m => m.Channel)
                    .Ascending(m => m.MapId)
                    .Ascending(m => m.Status),
                new CreateIndexOptions { Name = "ix_hired_merchants_map_status" }),
            new CreateIndexModel<HiredMerchantDocument>(
                Builders<HiredMerchantDocument>.IndexKeys
                    .Ascending(m => m.Status)
                    .Ascending(m => m.ExpireAtUnixMillis),
                new CreateIndexOptions { Name = "ix_hired_merchants_expire" }),
        });

        _items.Indexes.CreateOne(new CreateIndexModel<HiredMerchantItemDocument>(
            Builders<HiredMerchantItemDocument>.IndexKeys.Ascending(i => i.StoreId),
            new CreateIndexOptions { Name = "ix_hired_merchant_items_storeId" }));
    }

    public async Task<int> AddAsync(HiredMerchant merchant, CancellationToken cancellationToken = default)
    {
        await AssignStoreIdIfNeededAsync(merchant, cancellationToken).ConfigureAwait(false);
        await _merchants.InsertOneAsync(
            HiredMerchantDocumentMapper.ToDocument(merchant),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await SaveItemsAsync(merchant, cancellationToken).ConfigureAwait(false);
        return merchant.StoreId;
    }

    public async Task UpsertAsync(HiredMerchant merchant, CancellationToken cancellationToken = default)
    {
        if (merchant.StoreId <= 0)
        {
            await AddAsync(merchant, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _merchants.ReplaceOneAsync(
            m => m.StoreId == merchant.StoreId,
            HiredMerchantDocumentMapper.ToDocument(merchant),
            new ReplaceOptions { IsUpsert = true },
            cancellationToken).ConfigureAwait(false);
        await SaveItemsAsync(merchant, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HiredMerchant?> FindByStoreIdAsync(int storeId, CancellationToken cancellationToken = default)
    {
        var document = await _merchants
            .Find(m => m.StoreId == storeId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : await HydrateAsync(document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HiredMerchant?> FindOpenByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default)
    {
        var document = await _merchants
            .Find(m =>
                m.OwnerAccountId == ownerAccountId &&
                m.OwnerId == ownerId &&
                (m.Status == PlayerShopStatus.Draft ||
                 m.Status == PlayerShopStatus.Open ||
                 m.Status == PlayerShopStatus.Maintenance))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : await HydrateAsync(document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HiredMerchant?> FindClaimableByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default)
    {
        var document = await _merchants
            .Find(m =>
                m.OwnerAccountId == ownerAccountId &&
                m.OwnerId == ownerId &&
                (m.Status == PlayerShopStatus.PendingClaim ||
                 m.Status == PlayerShopStatus.Expired))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return document is null ? null : await HydrateAsync(document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HiredMerchant>> FindOpenByMapAsync(byte channel, int mapId, CancellationToken cancellationToken = default)
    {
        var documents = await _merchants
            .Find(m => m.Channel == channel && m.MapId == mapId && m.Status == PlayerShopStatus.Open)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await HydrateManyAsync(documents, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HiredMerchant>> FindExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var nowUnixMillis = now.ToUnixTimeMilliseconds();
        var documents = await _merchants
            .Find(m =>
                (m.Status == PlayerShopStatus.Open || m.Status == PlayerShopStatus.Maintenance) &&
                m.ExpireAtUnixMillis > 0 &&
                m.ExpireAtUnixMillis <= nowUnixMillis)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await HydrateManyAsync(documents, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(int storeId, CancellationToken cancellationToken = default)
    {
        await _items.DeleteManyAsync(i => i.StoreId == storeId, cancellationToken).ConfigureAwait(false);
        var result = await _merchants.DeleteOneAsync(m => m.StoreId == storeId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount > 0;
    }

    private async Task AssignStoreIdIfNeededAsync(HiredMerchant merchant, CancellationToken cancellationToken)
    {
        if (merchant.StoreId > 0)
        {
            await _sequences.EnsureAtLeastAsync(SequenceName, merchant.StoreId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var currentMax = await _merchants
            .Find(Builders<HiredMerchantDocument>.Filter.Empty)
            .SortByDescending(m => m.StoreId)
            .Limit(1)
            .Project(m => m.StoreId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        merchant.StoreId = await _sequences.NextAsync(SequenceName, currentMax, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveItemsAsync(HiredMerchant merchant, CancellationToken cancellationToken)
    {
        await _items.DeleteManyAsync(i => i.StoreId == merchant.StoreId, cancellationToken).ConfigureAwait(false);
        var items = HiredMerchantDocumentMapper.ToItemDocuments(merchant);
        if (items.Count > 0)
        {
            await _items.InsertManyAsync(items, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HiredMerchant> HydrateAsync(HiredMerchantDocument document, CancellationToken cancellationToken)
    {
        var items = await _items
            .Find(i => i.StoreId == document.StoreId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return HiredMerchantDocumentMapper.ToDomain(document, items);
    }

    private async Task<IReadOnlyList<HiredMerchant>> HydrateManyAsync(
        IReadOnlyList<HiredMerchantDocument> documents,
        CancellationToken cancellationToken)
    {
        var result = new List<HiredMerchant>(documents.Count);
        foreach (var document in documents)
        {
            result.Add(await HydrateAsync(document, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }
}
