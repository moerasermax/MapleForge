using LiteDB;
using Maple.Core.CashShop;

namespace Maple.Persistence.CashShop;

public sealed class LiteDbCashCouponRepository : ICashCouponRepository
{
    private readonly ILiteCollection<CashCoupon> _collection;

    public LiteDbCashCouponRepository(LiteDatabase db)
    {
        _collection = db.GetCollection<CashCoupon>("cash_coupons");
        _collection.EnsureIndex(c => c.Code, unique: true);
    }

    public Task<CashCoupon?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        => Task.FromResult<CashCoupon?>(_collection.FindOne(c => c.Code == code));

    public Task<bool> TryMarkUsedAsync(
        string code,
        string usedBy,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        var coupon = _collection.FindOne(c => c.Code == code && c.Valid);
        if (coupon is null)
        {
            return Task.FromResult(false);
        }

        coupon.Valid = false;
        coupon.UsedBy = usedBy;
        coupon.UsedAt = usedAt;
        return Task.FromResult(_collection.Update(coupon));
    }

    public Task UpsertAsync(CashCoupon coupon, CancellationToken cancellationToken = default)
    {
        coupon.Code = coupon.Code.Trim().ToUpperInvariant();
        _collection.Upsert(coupon);
        return Task.CompletedTask;
    }
}
