using Maple.Application.CashShop;
using Maple.Core.Accounts;
using Maple.Core.CashShop;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.CashShop;

public sealed class CashShopServiceTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000L);

    [Fact]
    public void Buy_WithCashPoints_DeductsBalanceAndAddsItem()
    {
        var service = new CashShopService(new FakeCashItemCatalog(DefaultItem));
        var account = new Account { Id = 7, CashPoints = 100, MaplePoints = 30 };
        var player = NewPlayer(gender: 0);

        var result = service.Buy(account, player, CashCurrencyType.Cash, DefaultItem.SerialNumber, FixedNow);

        Assert.Equal(CashShopTransactionStatus.Success, result.Status);
        Assert.Equal(55, account.CashPoints);
        Assert.Equal(30, account.MaplePoints);
        Assert.Equal(55, result.CashPoints);
        Assert.Equal(30, result.MaplePoints);
        Assert.Equal(10, player.Inventory.By(InventoryType.Cash).CountById(DefaultItem.ItemId));

        var record = Assert.Single(player.Character.Items);
        Assert.Equal((byte)InventoryType.Cash, record.Type);
        Assert.Equal(DefaultItem.ItemId, record.ItemId);
        Assert.Equal((short)10, record.Quantity);
    }

    [Fact]
    public void Buy_WithMaplePoints_DeductsMaplePointBalance()
    {
        var service = new CashShopService(new FakeCashItemCatalog(DefaultItem));
        var account = new Account { Id = 7, CashPoints = 0, MaplePoints = 50 };
        var player = NewPlayer(gender: 0);

        var result = service.Buy(account, player, CashCurrencyType.MaplePoint, DefaultItem.SerialNumber, FixedNow);

        Assert.Equal(CashShopTransactionStatus.Success, result.Status);
        Assert.Equal(0, account.CashPoints);
        Assert.Equal(5, account.MaplePoints);
    }

    [Fact]
    public void Buy_NotEnoughCash_DoesNotMutateAndUsesJavaCashError()
    {
        var service = new CashShopService(new FakeCashItemCatalog(DefaultItem));
        var account = new Account { Id = 7, CashPoints = 44 };
        var player = NewPlayer(gender: 0);

        var result = service.Buy(account, player, CashCurrencyType.Cash, DefaultItem.SerialNumber, FixedNow);

        Assert.Equal(CashShopTransactionStatus.NotEnoughCash, result.Status);
        Assert.Equal(168, result.JavaErrorCode);
        Assert.Equal(44, account.CashPoints);
        Assert.Empty(player.Character.Items);
        Assert.Empty(player.Inventory.By(InventoryType.Cash).Items);
    }

    [Fact]
    public void Buy_GenderMismatch_DoesNotMutateAndUsesJavaGenderError()
    {
        var femaleOnly = DefaultItem with { Gender = 1 };
        var service = new CashShopService(new FakeCashItemCatalog(femaleOnly));
        var account = new Account { Id = 7, CashPoints = 100 };
        var player = NewPlayer(gender: 0);

        var result = service.Buy(account, player, CashCurrencyType.Cash, femaleOnly.SerialNumber, FixedNow);

        Assert.Equal(CashShopTransactionStatus.GenderMismatch, result.Status);
        Assert.Equal(186, result.JavaErrorCode);
        Assert.Equal(100, account.CashPoints);
        Assert.Empty(player.Character.Items);
    }

    [Fact]
    public async Task RedeemCoupon_CashPointsMarksCodeUsedAndUpdatesBalance()
    {
        var coupons = new InMemoryCouponRepository(new CashCoupon
        {
            Code = "D3NX",
            Type = CashCouponRewardType.CashPoints,
            Item = 500,
        });
        var service = new CashShopService(new FakeCashItemCatalog(DefaultItem), coupons);
        var account = new Account { CashPoints = 10 };
        var player = NewPlayer(gender: 0);

        var result = await service.RedeemCouponAsync(account, player, "d3nx", FixedNow);

        Assert.Equal(CashCouponRedeemStatus.Success, result.Status);
        Assert.True(result.AccountMutated);
        Assert.False(result.CharacterMutated);
        Assert.Equal(510, account.CashPoints);
        Assert.False((await coupons.FindByCodeAsync("D3NX"))!.Valid);
    }

    [Fact]
    public async Task RedeemCoupon_ItemRewardAddsInventoryAndFlushesCharacter()
    {
        var coupons = new InMemoryCouponRepository(new CashCoupon
        {
            Code = "D3ITEM",
            Type = CashCouponRewardType.Item,
            Item = 2000000,
            Size = 3,
            Time = 7,
        });
        var service = new CashShopService(new FakeCashItemCatalog(DefaultItem), coupons);
        var account = new Account();
        var player = NewPlayer(gender: 0);

        var result = await service.RedeemCouponAsync(account, player, "D3ITEM", FixedNow);

        Assert.Equal(CashCouponRedeemStatus.Success, result.Status);
        Assert.False(result.AccountMutated);
        Assert.True(result.CharacterMutated);
        Assert.NotNull(result.GainedItem);
        Assert.Equal(2000000, result.GainedItem!.ItemId);
        Assert.Equal(3, result.GainedItem.Quantity);
        Assert.Equal(FixedNow.AddDays(7).ToUnixTimeMilliseconds(), result.GainedItem.Expiration);
        Assert.Contains(player.Character.Items, i => i.ItemId == 2000000 && i.Quantity == 3);
    }

    [Fact]
    public async Task RedeemCoupon_AlreadyUsedFailsWithoutReward()
    {
        var coupons = new InMemoryCouponRepository(new CashCoupon
        {
            Code = "USED",
            Valid = false,
            Type = CashCouponRewardType.MaplePoints,
            Item = 100,
        });
        var service = new CashShopService(new FakeCashItemCatalog(DefaultItem), coupons);
        var account = new Account();
        var player = NewPlayer(gender: 0);

        var result = await service.RedeemCouponAsync(account, player, "USED", FixedNow);

        Assert.Equal(CashCouponRedeemStatus.InvalidCode, result.Status);
        Assert.Equal(0, account.MaplePoints);
    }

    private static readonly CashItemDefinition DefaultItem = new(
        10000001,
        5350000,
        10,
        45,
        0,
        2,
        -1,
        true);

    private static Player NewPlayer(byte gender)
        => new(
            new Character
            {
                Id = 1,
                Name = "CashShopApp",
                Gender = gender,
            },
            new Position(0, 0, 0, 0));

    private sealed class FakeCashItemCatalog : ICashItemCatalog
    {
        private readonly Dictionary<int, CashItemDefinition> _items;

        public FakeCashItemCatalog(params CashItemDefinition[] items)
        {
            _items = items.ToDictionary(static i => i.SerialNumber);
        }

        public CashItemDefinition? GetBySerialNumber(int serialNumber)
            => _items.GetValueOrDefault(serialNumber);
    }

    private sealed class InMemoryCouponRepository : ICashCouponRepository
    {
        private readonly Dictionary<string, CashCoupon> _coupons;

        public InMemoryCouponRepository(params CashCoupon[] coupons)
        {
            _coupons = coupons.ToDictionary(static c => c.Code, StringComparer.OrdinalIgnoreCase);
        }

        public Task<CashCoupon?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(_coupons.GetValueOrDefault(code));

        public Task<bool> TryMarkUsedAsync(
            string code,
            string usedBy,
            DateTimeOffset usedAt,
            CancellationToken cancellationToken = default)
        {
            if (!_coupons.TryGetValue(code, out var coupon) || !coupon.Valid)
            {
                return Task.FromResult(false);
            }

            coupon.Valid = false;
            coupon.UsedBy = usedBy;
            coupon.UsedAt = usedAt;
            return Task.FromResult(true);
        }

        public Task UpsertAsync(CashCoupon coupon, CancellationToken cancellationToken = default)
        {
            _coupons[coupon.Code] = coupon;
            return Task.CompletedTask;
        }
    }
}
