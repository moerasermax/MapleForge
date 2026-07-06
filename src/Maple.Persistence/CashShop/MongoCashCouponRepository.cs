using Maple.Core.CashShop;
using MongoDB.Driver;

namespace Maple.Persistence.CashShop;

public sealed class MongoCashCouponRepository : ICashCouponRepository
{
    private readonly IMongoCollection<CashCoupon> _collection;

    public MongoCashCouponRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<CashCoupon>("cash_coupons");
        var codeIndex = new CreateIndexModel<CashCoupon>(
            Builders<CashCoupon>.IndexKeys.Ascending(c => c.Code),
            new CreateIndexOptions { Unique = true, Name = "ux_cash_coupons_code" });
        _collection.Indexes.CreateOne(codeIndex);
    }

    public async Task<CashCoupon?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await _collection
            .Find(c => c.Code == code)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> TryMarkUsedAsync(
        string code,
        string usedBy,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default)
    {
        var update = Builders<CashCoupon>.Update
            .Set(c => c.Valid, false)
            .Set(c => c.UsedBy, usedBy)
            .Set(c => c.UsedAt, usedAt);

        var updated = await _collection
            .FindOneAndUpdateAsync(
                c => c.Code == code && c.Valid,
                update,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return updated is not null;
    }

    public Task UpsertAsync(CashCoupon coupon, CancellationToken cancellationToken = default)
    {
        coupon.Code = coupon.Code.Trim().ToUpperInvariant();
        return _collection.ReplaceOneAsync(
            c => c.Code == coupon.Code,
            coupon,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }
}
