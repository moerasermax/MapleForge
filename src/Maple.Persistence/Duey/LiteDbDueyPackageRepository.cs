using LiteDB;
using Maple.Core.Duey;

namespace Maple.Persistence.Duey;

public sealed class LiteDbDueyPackageRepository : IDueyPackageRepository
{
    private readonly ILiteCollection<DueyPackage> _collection;

    public LiteDbDueyPackageRepository(LiteDatabase db)
    {
        _collection = db.GetCollection<DueyPackage>("dueyPackages");
        _collection.EnsureIndex(p => p.RecipientCharacterId);
        _collection.EnsureIndex(p => p.ExpiresAtUnixMillis);
    }

    public Task AddAsync(DueyPackage package, CancellationToken ct = default)
    {
        _collection.Insert(package);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DueyPackage>> GetInboxAsync(
        int recipientCharacterId,
        long nowUnixMillis,
        CancellationToken ct = default)
    {
        var list = _collection
            .Find(p => p.RecipientCharacterId == recipientCharacterId && p.ExpiresAtUnixMillis > nowUnixMillis)
            .OrderBy(static p => p.Id)
            .ToList();

        return Task.FromResult<IReadOnlyList<DueyPackage>>(list);
    }

    public Task<DueyPackage?> FindForRecipientAsync(
        int packageId,
        int recipientCharacterId,
        long nowUnixMillis,
        CancellationToken ct = default)
    {
        var package = _collection.FindOne(
            p => p.Id == packageId &&
                 p.RecipientCharacterId == recipientCharacterId &&
                 p.ExpiresAtUnixMillis > nowUnixMillis);

        return Task.FromResult<DueyPackage?>(package);
    }

    public Task<bool> RemoveAsync(int packageId, int recipientCharacterId, CancellationToken ct = default)
    {
        var removed = _collection.DeleteMany(p => p.Id == packageId && p.RecipientCharacterId == recipientCharacterId);
        return Task.FromResult(removed > 0);
    }

    public Task<int> DeleteExpiredAsync(int recipientCharacterId, long nowUnixMillis, CancellationToken ct = default)
    {
        var removed = _collection.DeleteMany(
            p => p.RecipientCharacterId == recipientCharacterId && p.ExpiresAtUnixMillis <= nowUnixMillis);

        return Task.FromResult(removed);
    }
}
