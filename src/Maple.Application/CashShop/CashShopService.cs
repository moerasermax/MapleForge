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

public enum CashCouponRedeemStatus
{
    Success,
    CouponRepositoryUnavailable,
    InvalidCode,
    UnsupportedRewardType,
    InvalidReward,
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

public sealed record CashCouponRedeemResult(
    CashCouponRedeemStatus Status,
    string Code,
    CashCoupon? Coupon = null,
    Item? GainedItem = null,
    int CashPoints = 0,
    int MaplePoints = 0,
    int Meso = 0,
    int JavaErrorCode = 0)
{
    public bool AccountMutated => Status == CashCouponRedeemStatus.Success &&
        Coupon?.Type is CashCouponRewardType.CashPoints or CashCouponRewardType.MaplePoints;

    public bool CharacterMutated => Status == CashCouponRedeemStatus.Success &&
        Coupon?.Type is CashCouponRewardType.Item or CashCouponRewardType.Meso;
}

/// <summary>Cash Shop 核心購買用例。協定欄位留在 Adapters；商品資料由 ICashItemCatalog 注入。</summary>
public sealed class CashShopService
{
    private readonly ICashItemCatalog _catalog;
    private readonly ICashCouponRepository? _coupons;

    public CashShopService(ICashItemCatalog catalog, ICashCouponRepository? coupons = null)
    {
        _catalog = catalog;
        _coupons = coupons;
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

    public async Task<CashCouponRedeemResult> RedeemCouponAsync(
        Account account,
        Player player,
        string code,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (_coupons is null)
        {
            return new CashCouponRedeemResult(
                CashCouponRedeemStatus.CouponRepositoryUnavailable,
                NormalizeCouponCode(code),
                JavaErrorCode: JavaErrorCode.InvalidCoupon);
        }

        var normalized = NormalizeCouponCode(code);
        if (normalized.Length == 0)
        {
            return new CashCouponRedeemResult(
                CashCouponRedeemStatus.InvalidCode,
                normalized,
                JavaErrorCode: JavaErrorCode.InvalidCoupon);
        }

        var coupon = await _coupons.FindByCodeAsync(normalized, cancellationToken).ConfigureAwait(false);
        if (coupon is null || !coupon.Valid)
        {
            return new CashCouponRedeemResult(
                CashCouponRedeemStatus.InvalidCode,
                normalized,
                coupon,
                JavaErrorCode: JavaErrorCode.InvalidCoupon);
        }

        if (!ValidateCouponReward(coupon))
        {
            return new CashCouponRedeemResult(
                CashCouponRedeemStatus.InvalidReward,
                normalized,
                coupon,
                JavaErrorCode: JavaErrorCode.Generic);
        }

        if (coupon.Type == CashCouponRewardType.Item &&
            player.Inventory.By(Player.InventoryTypeOf(coupon.Item)).FirstFreeSlot() is null)
        {
            return new CashCouponRedeemResult(
                CashCouponRedeemStatus.InventoryFull,
                normalized,
                coupon,
                JavaErrorCode: JavaErrorCode.InventoryFull);
        }

        var marked = await _coupons
            .TryMarkUsedAsync(normalized, player.Character.Name, now, cancellationToken)
            .ConfigureAwait(false);
        if (!marked)
        {
            return new CashCouponRedeemResult(
                CashCouponRedeemStatus.InvalidCode,
                normalized,
                coupon,
                JavaErrorCode: JavaErrorCode.InvalidCoupon);
        }

        switch (coupon.Type)
        {
            case CashCouponRewardType.CashPoints:
                account.CashPoints = SaturatingAdd(account.CashPoints, coupon.Item);
                return new CashCouponRedeemResult(
                    CashCouponRedeemStatus.Success,
                    normalized,
                    coupon,
                    CashPoints: account.CashPoints,
                    MaplePoints: account.MaplePoints);

            case CashCouponRewardType.MaplePoints:
                account.MaplePoints = SaturatingAdd(account.MaplePoints, coupon.Item);
                return new CashCouponRedeemResult(
                    CashCouponRedeemStatus.Success,
                    normalized,
                    coupon,
                    CashPoints: account.CashPoints,
                    MaplePoints: account.MaplePoints);

            case CashCouponRewardType.Item:
                var gained = player.GainItem(Player.InventoryTypeOf(coupon.Item), coupon.Item, coupon.Size);
                if (gained is null)
                {
                    return new CashCouponRedeemResult(
                        CashCouponRedeemStatus.InventoryFull,
                        normalized,
                        coupon,
                        JavaErrorCode: JavaErrorCode.InventoryFull);
                }

                gained.Expiration = ToExpiration(coupon.Time, now);
                player.FlushInventory();
                return new CashCouponRedeemResult(
                    CashCouponRedeemStatus.Success,
                    normalized,
                    coupon,
                    gained,
                    account.CashPoints,
                    account.MaplePoints);

            case CashCouponRewardType.Meso:
                player.GainMeso(coupon.Item);
                return new CashCouponRedeemResult(
                    CashCouponRedeemStatus.Success,
                    normalized,
                    coupon,
                    CashPoints: account.CashPoints,
                    MaplePoints: account.MaplePoints,
                    Meso: player.Character.Meso);

            default:
                return new CashCouponRedeemResult(
                    CashCouponRedeemStatus.UnsupportedRewardType,
                    normalized,
                    coupon,
                    JavaErrorCode: JavaErrorCode.Generic);
        }
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

    private static string NormalizeCouponCode(string code) => code.Trim().ToUpperInvariant();

    private static bool ValidateCouponReward(CashCoupon coupon)
        => coupon.Type switch
        {
            CashCouponRewardType.CashPoints or CashCouponRewardType.MaplePoints or CashCouponRewardType.Meso => coupon.Item > 0,
            CashCouponRewardType.Item => coupon.Item > 0 && coupon.Size > 0,
            _ => false,
        };

    private static int SaturatingAdd(int current, int delta)
        => (int)Math.Clamp((long)current + delta, 0, int.MaxValue);

    private static long ToExpiration(int days, DateTimeOffset now)
        => days <= 0 ? -1 : now.AddDays(days).ToUnixTimeMilliseconds();

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
        public const int InvalidCoupon = 179;
        public const int InventoryFull = 175;
        public const int GenderMismatch = 186;
        public const int NotOnSale = 225;
    }
}
