using LiteDB;
using Maple.Core.Inventory;
using Maple.Core.PlayerShops;
using Maple.Core.World;
using Maple.Persistence.PlayerShops;

namespace Maple.Persistence.Tests;

public sealed class HiredMerchantRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"maple-hired-merchant-{Guid.NewGuid():N}.db");
    private readonly LiteDatabase _db;
    private readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(1);

    public HiredMerchantRepositoryTests()
    {
        _db = new LiteDatabase(_dbPath);
    }

    [Fact]
    public async Task LiteDbRepository_RoundTripsMerchantHeaderAndItems()
    {
        var repo = new LiteDbHiredMerchantRepository(_db);
        var merchant = NewMerchant();
        merchant.AddToBlacklist("Blocked");
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000000, Quantity = 8 }, 4, 2, 500);
        merchant.TryAddListing(InventoryType.Equip, new Equip { ItemId = 1302000, Quantity = 1, Str = 3 }, 1, 1, 10_000);
        merchant.State.Mesos = 12_345;

        var storeId = await repo.AddAsync(merchant);
        var loaded = await repo.FindByStoreIdAsync(storeId);

        Assert.NotEqual(0, storeId);
        Assert.NotNull(loaded);
        Assert.Equal("Potion shop", loaded!.Title);
        Assert.Equal(910000001, loaded.MapId);
        Assert.Equal((byte)1, loaded.Channel);
        Assert.Equal(new Position(123, 456, 2, 7), loaded.Position);
        Assert.Equal(12_345, loaded.Mesos);
        Assert.Contains("Blocked", loaded.Blacklist);
        Assert.Equal(2, loaded.Items.Count);
        Assert.Equal((short)4, loaded.Items[0].Bundles);
        Assert.Equal((short)2, loaded.Items[0].BundleQuantity);
        Assert.Equal(2000000, loaded.Items[0].Item.ItemId);
        Assert.True(loaded.Items[1].Item.IsEquip);
        Assert.Equal((short)3, loaded.Items[1].Item.Str);
    }

    [Fact]
    public async Task LiteDbRepository_QueriesOpenClaimableMapAndExpiredMerchants()
    {
        var repo = new LiteDbHiredMerchantRepository(_db);
        var merchant = NewMerchant();
        await repo.AddAsync(merchant);

        var openByOwner = await repo.FindOpenByOwnerAsync(merchant.OwnerAccountId, merchant.OwnerId);
        var openByMap = await repo.FindOpenByMapAsync(merchant.Channel, merchant.MapId);
        var expiredBefore = await repo.FindExpiredAsync(_now.AddHours(12));
        var expiredAfter = await repo.FindExpiredAsync(_now.AddDays(2));

        merchant.CloseForClaim(_now);
        await repo.UpsertAsync(merchant);
        var claimable = await repo.FindClaimableByOwnerAsync(merchant.OwnerAccountId, merchant.OwnerId);

        Assert.NotNull(openByOwner);
        Assert.Single(openByMap);
        Assert.Empty(expiredBefore);
        Assert.Single(expiredAfter);
        Assert.NotNull(claimable);
        Assert.Equal(PlayerShopStatus.PendingClaim, claimable!.Status);
    }

    [Fact]
    public async Task LiteDbRepository_RoundTripsMerchantPosition()
    {
        var repo = new LiteDbHiredMerchantRepository(_db);
        var merchant = NewMerchant();

        var storeId = await repo.AddAsync(merchant);
        var loaded = await repo.FindByStoreIdAsync(storeId);

        Assert.NotNull(loaded);
        Assert.Equal(new Position(123, 456, 2, 7), loaded!.Position);
    }

    [Fact]
    public async Task LiteDbRepository_DeleteRemovesMerchantAndItemRows()
    {
        var repo = new LiteDbHiredMerchantRepository(_db);
        var merchant = NewMerchant();
        merchant.TryAddListing(InventoryType.Etc, new Item { ItemId = 4000000, Quantity = 1 }, 1, 1, 100);
        var storeId = await repo.AddAsync(merchant);

        var deleted = await repo.DeleteAsync(storeId);

        Assert.True(deleted);
        Assert.Null(await repo.FindByStoreIdAsync(storeId));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private HiredMerchant NewMerchant()
    {
        var merchant = HiredMerchant.Create(
            ownerId: 1,
            ownerAccountId: 10,
            ownerName: "Owner",
            itemId: 5030000,
            title: "Potion shop",
            mapId: 910000001,
            channel: 1,
            now: _now,
            duration: TimeSpan.FromDays(1),
            position: new Position(123, 456, 2, 7));
        merchant.Open(_now);
        return merchant;
    }
}
