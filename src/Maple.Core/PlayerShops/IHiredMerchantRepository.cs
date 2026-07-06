namespace Maple.Core.PlayerShops;

public interface IHiredMerchantRepository
{
    Task<int> AddAsync(HiredMerchant merchant, CancellationToken cancellationToken = default);

    Task UpsertAsync(HiredMerchant merchant, CancellationToken cancellationToken = default);

    Task<HiredMerchant?> FindByStoreIdAsync(int storeId, CancellationToken cancellationToken = default);

    Task<HiredMerchant?> FindOpenByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default);

    Task<HiredMerchant?> FindClaimableByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HiredMerchant>> FindOpenByMapAsync(byte channel, int mapId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HiredMerchant>> FindExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int storeId, CancellationToken cancellationToken = default);
}
