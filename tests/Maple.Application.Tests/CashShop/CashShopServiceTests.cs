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
}
