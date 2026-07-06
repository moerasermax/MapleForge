namespace Maple.Core.CashShop;

public interface ICashCouponRepository
{
    Task<CashCoupon?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> TryMarkUsedAsync(
        string code,
        string usedBy,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(CashCoupon coupon, CancellationToken cancellationToken = default);
}
