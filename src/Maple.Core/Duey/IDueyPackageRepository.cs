namespace Maple.Core.Duey;

public interface IDueyPackageRepository
{
    Task AddAsync(DueyPackage package, CancellationToken ct = default);

    Task<IReadOnlyList<DueyPackage>> GetInboxAsync(
        int recipientCharacterId,
        long nowUnixMillis,
        CancellationToken ct = default);

    Task<DueyPackage?> FindForRecipientAsync(
        int packageId,
        int recipientCharacterId,
        long nowUnixMillis,
        CancellationToken ct = default);

    Task<bool> RemoveAsync(int packageId, int recipientCharacterId, CancellationToken ct = default);

    Task<int> DeleteExpiredAsync(int recipientCharacterId, long nowUnixMillis, CancellationToken ct = default);
}
