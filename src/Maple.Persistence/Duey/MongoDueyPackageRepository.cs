using Maple.Core.Duey;
using MongoDB.Driver;

namespace Maple.Persistence.Duey;

public sealed class MongoDueyPackageRepository : IDueyPackageRepository
{
    private const string CollectionName = "dueyPackages";
    private const string SequenceName = "dueyPackages";

    private readonly IMongoCollection<DueyPackage> _collection;
    private readonly MongoSequenceGenerator _sequences;

    public MongoDueyPackageRepository(IMongoDatabase database, MongoSequenceGenerator sequences)
    {
        _collection = database.GetCollection<DueyPackage>(CollectionName);
        _sequences = sequences;

        var recipientIndex = new CreateIndexModel<DueyPackage>(
            Builders<DueyPackage>.IndexKeys.Ascending(p => p.RecipientCharacterId).Ascending(p => p.Id),
            new CreateIndexOptions { Name = "ix_dueyPackages_recipient_id" });
        var expiryIndex = new CreateIndexModel<DueyPackage>(
            Builders<DueyPackage>.IndexKeys.Ascending(p => p.ExpiresAtUnixMillis),
            new CreateIndexOptions { Name = "ix_dueyPackages_expiresAt" });

        _collection.Indexes.CreateMany(new[] { recipientIndex, expiryIndex });
    }

    public async Task AddAsync(DueyPackage package, CancellationToken ct = default)
    {
        await AssignIdIfNeededAsync(package, ct).ConfigureAwait(false);
        await _collection.InsertOneAsync(package, cancellationToken: ct).ConfigureAwait(false);
        await _sequences.EnsureAtLeastAsync(SequenceName, package.Id, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DueyPackage>> GetInboxAsync(
        int recipientCharacterId,
        long nowUnixMillis,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(p => p.RecipientCharacterId == recipientCharacterId && p.ExpiresAtUnixMillis > nowUnixMillis)
            .SortBy(p => p.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<DueyPackage?> FindForRecipientAsync(
        int packageId,
        int recipientCharacterId,
        long nowUnixMillis,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(p => p.Id == packageId &&
                       p.RecipientCharacterId == recipientCharacterId &&
                       p.ExpiresAtUnixMillis > nowUnixMillis)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(int packageId, int recipientCharacterId, CancellationToken ct = default)
    {
        var result = await _collection
            .DeleteOneAsync(p => p.Id == packageId && p.RecipientCharacterId == recipientCharacterId, ct)
            .ConfigureAwait(false);

        return result.DeletedCount > 0;
    }

    public async Task<int> DeleteExpiredAsync(int recipientCharacterId, long nowUnixMillis, CancellationToken ct = default)
    {
        var result = await _collection
            .DeleteManyAsync(
                p => p.RecipientCharacterId == recipientCharacterId && p.ExpiresAtUnixMillis <= nowUnixMillis,
                ct)
            .ConfigureAwait(false);

        return (int)result.DeletedCount;
    }

    private async Task AssignIdIfNeededAsync(DueyPackage package, CancellationToken ct)
    {
        if (package.Id > 0)
        {
            return;
        }

        var currentMax = await _collection
            .Find(Builders<DueyPackage>.Filter.Empty)
            .SortByDescending(p => p.Id)
            .Limit(1)
            .Project(p => p.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        package.Id = await _sequences.NextAsync(SequenceName, currentMax, ct).ConfigureAwait(false);
    }
}
