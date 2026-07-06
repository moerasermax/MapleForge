using Maple.Core.Inventory;
using Maple.Core.PlayerShops;
using Maple.Core.World;

namespace Maple.Core.Tests.PlayerShops;

public sealed class HiredMerchantTests
{
    private readonly DateTimeOffset _now = DateTimeOffset.UnixEpoch.AddDays(1);

    [Fact]
    public void Buy_ReducesBundlesClearsKarmaAndAppliesEntrustedStoreTax()
    {
        var merchant = NewMerchant();
        merchant.Open(_now);
        var added = merchant.TryAddListing(
            InventoryType.Use,
            new Item { ItemId = 2000000, Quantity = 10, Flag = ItemFlags.KarmaUse },
            bundles: 5,
            bundleQuantity: 2,
            price: 25_000);

        var result = merchant.TryBuy(
            listingIndex: 0,
            bundleCount: 4,
            buyerMeso: 200_000,
            buyerCanHold: true,
            buyerName: "Buyer",
            now: _now);

        Assert.Equal(PlayerShopAddListingStatus.Success, added.Status);
        Assert.Equal(PlayerShopPurchaseStatus.Success, result.Status);
        Assert.Equal(100_000, result.TotalPrice);
        Assert.Equal(400, result.Tax);
        Assert.Equal(99_600, merchant.Mesos);
        Assert.Equal((short)1, merchant.Items[0].Bundles);
        Assert.Equal((short)8, result.Item!.Quantity);
        Assert.False(ItemFlags.Has(result.Item.Flag, ItemFlags.KarmaUse));
    }

    [Fact]
    public void Buy_LastBundleLeavesListingSoldOut()
    {
        var merchant = NewMerchant();
        merchant.Open(_now);
        merchant.TryAddListing(InventoryType.Etc, new Item { ItemId = 4000000, Quantity = 2 }, 2, 1, 1_000);

        var first = merchant.TryBuy(0, 2, 5_000, true, "Buyer", _now);
        var second = merchant.TryBuy(0, 1, 5_000, true, "Buyer2", _now);

        Assert.Equal(PlayerShopPurchaseStatus.Success, first.Status);
        Assert.Equal(0, first.RemainingBundles);
        Assert.Equal(PlayerShopPurchaseStatus.SoldOut, second.Status);
    }

    [Fact]
    public void BlacklistedBuyerCannotEnterOrBuy()
    {
        var merchant = NewMerchant();
        merchant.Open(_now);
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000000, Quantity = 1 }, 1, 1, 100);
        merchant.AddToBlacklist("Buyer");

        var visit = merchant.TryEnter(2, "Buyer", _now);
        var buy = merchant.TryBuy(0, 1, 1_000, true, "Buyer", _now);

        Assert.Equal(PlayerShopVisitStatus.Blacklisted, visit.Status);
        Assert.Equal(PlayerShopPurchaseStatus.Blacklisted, buy.Status);
    }

    [Fact]
    public void ExpiredMerchantRejectsPurchaseAndMarksExpired()
    {
        var merchant = NewMerchant();
        merchant.Open(_now);
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000000, Quantity = 1 }, 1, 1, 100);

        var result = merchant.TryBuy(0, 1, 1_000, true, "Buyer", _now.AddDays(2));

        Assert.Equal(PlayerShopPurchaseStatus.Expired, result.Status);
        Assert.Equal(PlayerShopStatus.Expired, merchant.Status);
    }

    [Fact]
    public void Buy_RejectsStoreMesoOverflow()
    {
        var merchant = NewMerchant();
        merchant.State.Mesos = int.MaxValue - 10;
        merchant.Open(_now);
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000000, Quantity = 1 }, 1, 1, 20);

        var result = merchant.TryBuy(0, 1, 100, true, "Buyer", _now);

        Assert.Equal(PlayerShopPurchaseStatus.StoreMesoOverflow, result.Status);
        Assert.Equal(int.MaxValue - 10, merchant.Mesos);
        Assert.Equal((short)1, merchant.Items[0].Bundles);
    }

    [Fact]
    public async Task Buy_IsSerializedWhenTwoBuyersRaceForLastBundle()
    {
        var merchant = NewMerchant();
        merchant.Open(_now);
        merchant.TryAddListing(InventoryType.Use, new Item { ItemId = 2000000, Quantity = 1 }, 1, 1, 100);

        var attempts = await Task.WhenAll(
            Task.Run(() => merchant.TryBuy(0, 1, 1_000, true, "Buyer1", _now)),
            Task.Run(() => merchant.TryBuy(0, 1, 1_000, true, "Buyer2", _now)));

        Assert.Single(attempts, result => result.Status == PlayerShopPurchaseStatus.Success);
        Assert.Single(attempts, result => result.Status == PlayerShopPurchaseStatus.SoldOut);
        Assert.Equal((short)0, merchant.Items[0].Bundles);
    }

    [Fact]
    public void Create_StoresMerchantPosition()
    {
        var merchant = HiredMerchant.Create(
            ownerId: 1,
            ownerAccountId: 10,
            ownerName: "Owner",
            itemId: 5030000,
            title: "Position merchant",
            mapId: 910000001,
            channel: 1,
            now: _now,
            duration: TimeSpan.FromDays(1),
            position: new Position(12, 34, 1, 56));

        Assert.Equal(new Position(12, 34, 1, 56), merchant.Position);
    }

    private HiredMerchant NewMerchant()
        => HiredMerchant.Create(
            ownerId: 1,
            ownerAccountId: 10,
            ownerName: "Owner",
            itemId: 5030000,
            title: "Test merchant",
            mapId: 910000001,
            channel: 1,
            now: _now,
            duration: TimeSpan.FromDays(1));
}
