using Maple.Core.Accounts;
using Maple.Core.CashShop;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.CashShop;

public enum CashShopTransactionStatus
{
    Success,
    InvalidCurrency,
    ItemNotFound,
    ItemNotOnSale,
    InvalidPrice,
    NotEnoughCash,
    GenderMismatch,
    InventoryFull,
}

public sealed record CashShopBuyResult(
    CashShopTransactionStatus Status,
    CashCurrencyType Currency,
    int SerialNumber,
    CashItemDefinition? CashItem = null,
    Item? GainedItem = null,
    int CashPoints = 0,
    int MaplePoints = 0,
    int JavaErrorCode = 0);

/// <summary>Cash Shop 核心購買用例。協定欄位留在 Adapters；商品資料由 ICashItemCatalog 注入。</summary>
public sealed class CashShopService
{
    private readonly ICashItemCatalog _catalog;

    public CashShopService(ICashItemCatalog catalog)
    {
        _catalog = catalog;
    }

    public CashShopBuyResult Buy(
        Account account,
        Player player,
        CashCurrencyType currency,
        int serialNumber,
        DateTimeOffset? now = null)
    {
        if (!IsValidCurrency(currency))
        {
            return Fail(CashShopTransactionStatus.InvalidCurrency, currency, serialNumber, null, JavaErrorCode.Generic);
        }

        var cashItem = _catalog.GetBySerialNumber(serialNumber);
        if (cashItem is null)
        {
            return Fail(CashShopTransactionStatus.ItemNotFound, currency, serialNumber, null, JavaErrorCode.Generic);
        }

        if (!cashItem.OnSale)
        {
            return Fail(CashShopTransactionStatus.ItemNotOnSale, currency, serialNumber, cashItem, JavaErrorCode.NotOnSale);
        }

        if (cashItem.Price < 0)
        {
            return Fail(CashShopTransactionStatus.InvalidPrice, currency, serialNumber, cashItem, JavaErrorCode.Generic);
        }

        if (GetBalance(account, currency) < cashItem.Price)
        {
            var error = currency == CashCurrencyType.Cash
                ? JavaErrorCode.NotEnoughCash
                : JavaErrorCode.NotOnSale;
            return Fail(CashShopTransactionStatus.NotEnoughCash, currency, serialNumber, cashItem, error);
        }

        if (!cashItem.GenderMatches(player.Character.Gender))
        {
            return Fail(CashShopTransactionStatus.GenderMismatch, currency, serialNumber, cashItem, JavaErrorCode.GenderMismatch);
        }

        var gained = player.GainCashShopItem(cashItem, now);
        if (gained is null)
        {
            return Fail(CashShopTransactionStatus.InventoryFull, currency, serialNumber, cashItem, JavaErrorCode.InventoryFull);
        }

        Debit(account, currency, cashItem.Price);
        player.FlushInventory();

        return new CashShopBuyResult(
            CashShopTransactionStatus.Success,
            currency,
            serialNumber,
            cashItem,
            gained,
            account.CashPoints,
            account.MaplePoints);
    }

    private static CashShopBuyResult Fail(
        CashShopTransactionStatus status,
        CashCurrencyType currency,
        int serialNumber,
        CashItemDefinition? cashItem,
        int errorCode)
        => new(status, currency, serialNumber, cashItem, JavaErrorCode: errorCode);

    private static bool IsValidCurrency(CashCurrencyType currency)
        => currency is CashCurrencyType.Cash or CashCurrencyType.MaplePoint;

    private static int GetBalance(Account account, CashCurrencyType currency)
        => currency == CashCurrencyType.Cash ? account.CashPoints : account.MaplePoints;

    private static void Debit(Account account, CashCurrencyType currency, int amount)
    {
        if (amount == 0) return;

        if (currency == CashCurrencyType.Cash)
        {
            account.CashPoints -= amount;
        }
        else
        {
            account.MaplePoints -= amount;
        }
    }

    private static class JavaErrorCode
    {
        public const int Generic = 0;
        public const int NotEnoughCash = 168;
        public const int InventoryFull = 175;
        public const int GenderMismatch = 186;
        public const int NotOnSale = 225;
    }
}
