using Maple.Application.PlayerShops;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.PlayerShops;
using Maple.Core.World;

namespace Maple.Application.Tests.PlayerShops;

public sealed class PlayerShopServiceTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(1);

    [Fact]
    public async Task AddListing_DeductsOwnerInventoryAndPersistsMerchant()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var service = new PlayerShopService(repo);
        var owner = NewPlayer(1, "Owner", 10, meso: 0, new ItemRecord
        {
            Type = (byte)InventoryType.Use,
            ItemId = 2000000,
            Slot = 1,
            Quantity = 10,
        });
        var create = await service.CreateHiredMerchantAsync(owner, 5030000, "Potion shop", 910000001, 1, _now);
        await service.OpenMerchantAsync(create.Merchant!.StoreId, owner, _now);

        var result = await service.AddListingAsync(
            create.Merchant.StoreId,
            owner,
            InventoryType.Use,
            slot: 1,
            itemId: 2000000,
            bundles: 2,
            bundleQuantity: 3,
            price: 100);

        var persisted = await repo.FindByStoreIdAsync(create.Merchant.StoreId);
        Assert.Equal(PlayerShopServiceStatus.Success, result.Status);
        Assert.Equal((short)4, owner.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Equal((short)4, owner.Character.Items.Single(i => i.ItemId == 2000000).Quantity);
        Assert.Equal((short)2, persisted!.Items[0].Bundles);
        Assert.Equal((short)3, persisted.Items[0].BundleQuantity);
    }

    [Fact]
    public async Task Buy_DeductsBuyerAddsItemAndPersistsMerchantIncome()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var service = new PlayerShopService(repo);
        var merchant = NewOpenMerchant();
        merchant.TryAddListing(InventoryType.Etc, new Item { ItemId = 4000000, Quantity = 6 }, 3, 2, 100);
        await repo.AddAsync(merchant);
        var buyer = NewPlayer(2, "Buyer", 20, meso: 1_000);

        var result = await service.BuyAsync(merchant.StoreId, buyer, listingIndex: 0, bundleCount: 2, _now);

        var persisted = await repo.FindByStoreIdAsync(merchant.StoreId);
        Assert.Equal(PlayerShopServiceStatus.Success, result.Status);
        Assert.Equal(800, buyer.Character.Meso);
        Assert.Equal(4, buyer.Inventory.By(InventoryType.Etc).CountById(4000000));
        Assert.Equal((short)1, persisted!.Items[0].Bundles);
        Assert.Equal(200, persisted.Mesos);
    }

    [Fact]
    public async Task TakeListing_ReturnsRemainingItemToOwnerAndExposesReturnedItem()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var service = new PlayerShopService(repo);
        var merchant = NewOpenMerchant();
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000000, Quantity = 6 }, 3, 2, 100);
        await repo.AddAsync(merchant);
        var owner = NewPlayer(1, "Owner", 10, meso: 0);

        var result = await service.TakeListingAsync(merchant.StoreId, owner, listingIndex: 0);

        var persisted = await repo.FindByStoreIdAsync(merchant.StoreId);
        Assert.Equal(PlayerShopServiceStatus.Success, result.Status);
        Assert.Equal(InventoryType.Use, result.ReturnedInventoryType);
        Assert.NotNull(result.ReturnedItem);
        Assert.Equal(6, owner.Inventory.By(InventoryType.Use).CountById(2000000));
        Assert.Empty(persisted!.Items);
    }

    [Fact]
    public async Task Claim_ReturnsPendingMerchantItemsAndMesosThenDeletesPackage()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var service = new PlayerShopService(repo);
        var merchant = NewOpenMerchant();
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000001, Quantity = 4 }, 2, 2, 100);
        merchant.State.Mesos = 500;
        merchant.CloseForClaim(_now);
        await repo.AddAsync(merchant);
        var owner = NewPlayer(1, "Owner", 10, meso: 100);

        var result = await service.ClaimAsync(owner);

        Assert.Equal(PlayerShopServiceStatus.Success, result.Status);
        Assert.Equal(600, owner.Character.Meso);
        Assert.Equal(4, owner.Inventory.By(InventoryType.Use).CountById(2000001));
        Assert.Null(await repo.FindByStoreIdAsync(merchant.StoreId));
    }

    [Fact]
    public async Task Claim_ConcurrentRequestsOnlyOneSucceeds()
    {
        var repo = new InMemoryHiredMerchantRepository { YieldOnFindClaimable = true };
        var service = new PlayerShopService(repo);
        var merchant = NewOpenMerchant();
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000002, Quantity = 2 }, 2, 1, 100);
        merchant.State.Mesos = 250;
        merchant.CloseForClaim(_now);
        await repo.AddAsync(merchant);
        var owner = NewPlayer(1, "Owner", 10, meso: 100);

        var results = await Task.WhenAll(
            Task.Run(() => service.ClaimAsync(owner)),
            Task.Run(() => service.ClaimAsync(owner)));

        Assert.Equal(1, results.Count(static r => r.Status == PlayerShopServiceStatus.Success));
        Assert.Equal(1, results.Count(static r => r.Status == PlayerShopServiceStatus.MerchantNotFound));
        Assert.Equal(350, owner.Character.Meso);
        Assert.Equal(2, owner.Inventory.By(InventoryType.Use).CountById(2000002));
        Assert.Null(await repo.FindByStoreIdAsync(merchant.StoreId));
    }

    [Fact]
    public async Task Claim_RejectsMesoOverflowAndKeepsMerchantPackage()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var service = new PlayerShopService(repo);
        var merchant = NewOpenMerchant();
        merchant.State.Mesos = 100;
        merchant.CloseForClaim(_now);
        await repo.AddAsync(merchant);
        var owner = NewPlayer(1, "Owner", 10, meso: int.MaxValue);

        var result = await service.ClaimAsync(owner);

        Assert.Equal(PlayerShopServiceStatus.MesoOverflow, result.Status);
        Assert.NotNull(await repo.FindByStoreIdAsync(merchant.StoreId));
    }

    [Fact]
    public async Task ExpireOpenMerchants_MarksExpiredMerchantClaimable()
    {
        var repo = new InMemoryHiredMerchantRepository();
        var service = new PlayerShopService(repo);
        var merchant = NewOpenMerchant();
        await repo.AddAsync(merchant);

        var count = await service.ExpireOpenMerchantsAsync(_now.AddDays(2));
        var claimable = await repo.FindClaimableByOwnerAsync(merchant.OwnerAccountId, merchant.OwnerId);

        Assert.Equal(1, count);
        Assert.NotNull(claimable);
        Assert.Equal(PlayerShopStatus.Expired, claimable!.Status);
    }

    private HiredMerchant NewOpenMerchant()
    {
        var merchant = HiredMerchant.Create(1, 10, "Owner", 5030000, "Shop", 910000001, 1, _now, TimeSpan.FromDays(1));
        merchant.Open(_now);
        return merchant;
    }

    private static Player NewPlayer(int id, string name, int accountId, int meso, params ItemRecord[] items)
        => new(
            new Character
            {
                Id = id,
                AccountId = accountId,
                Name = name,
                Meso = meso,
                Items = items.ToList(),
            },
            new Position(0, 0, 0, 0));

    private sealed class InMemoryHiredMerchantRepository : IHiredMerchantRepository
    {
        private readonly Dictionary<int, HiredMerchant> _merchants = new();
        private int _nextStoreId = 1;

        public bool YieldOnFindClaimable { get; init; }

        public Task<int> AddAsync(HiredMerchant merchant, CancellationToken cancellationToken = default)
        {
            if (merchant.StoreId <= 0)
            {
                merchant.StoreId = _nextStoreId++;
            }

            _merchants[merchant.StoreId] = merchant;
            return Task.FromResult(merchant.StoreId);
        }

        public Task UpsertAsync(HiredMerchant merchant, CancellationToken cancellationToken = default)
        {
            _merchants[merchant.StoreId] = merchant;
            return Task.CompletedTask;
        }

        public Task<HiredMerchant?> FindByStoreIdAsync(int storeId, CancellationToken cancellationToken = default)
            => Task.FromResult(_merchants.GetValueOrDefault(storeId));

        public Task<HiredMerchant?> FindOpenByOwnerAsync(int ownerAccountId, int ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_merchants.Values.FirstOrDefault(m =>
                m.OwnerAccountId == ownerAccountId &&
                m.OwnerId == ownerId &&
                m.Status is PlayerShopStatus.Draft or PlayerShopStatus.Open or PlayerShopStatus.Maintenance));

        public async Task<HiredMerchant?> FindClaimableByOwnerAsync(
            int ownerAccountId,
            int ownerId,
            CancellationToken cancellationToken = default)
        {
            if (YieldOnFindClaimable)
            {
                await Task.Yield();
            }

            return _merchants.Values.FirstOrDefault(m =>
                m.OwnerAccountId == ownerAccountId &&
                m.OwnerId == ownerId &&
                m.Status is PlayerShopStatus.PendingClaim or PlayerShopStatus.Expired);
        }

        public Task<IReadOnlyList<HiredMerchant>> FindOpenByMapAsync(byte channel, int mapId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HiredMerchant>>(_merchants.Values
                .Where(m => m.Channel == channel && m.MapId == mapId && m.Status == PlayerShopStatus.Open)
                .ToList());

        public Task<IReadOnlyList<HiredMerchant>> FindExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<HiredMerchant>>(_merchants.Values
                .Where(m => m.Status == PlayerShopStatus.Open && m.IsExpired(now))
                .ToList());

        public Task<bool> DeleteAsync(int storeId, CancellationToken cancellationToken = default)
            => Task.FromResult(_merchants.Remove(storeId));
    }
}
