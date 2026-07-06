using LiteDB;
using Maple.Core.CashShop;
using Maple.Persistence.CashShop;

namespace Maple.Persistence.Tests;

public sealed class CashCouponRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"maple-coupon-{Guid.NewGuid():N}.db");
    private readonly LiteDatabase _db;

    public CashCouponRepositoryTests()
    {
        _db = new LiteDatabase(_dbPath);
    }

    [Fact]
    public async Task LiteDbRepository_UpsertsFindsAndMarksCouponUsed()
    {
        var repo = new LiteDbCashCouponRepository(_db);
        await repo.UpsertAsync(new CashCoupon
        {
            Code = " d3code ",
            Type = CashCouponRewardType.Item,
            Item = 2000000,
            Size = 2,
            Time = -1,
        });

        var found = await repo.FindByCodeAsync("D3CODE");
        var marked = await repo.TryMarkUsedAsync("D3CODE", "Tester", DateTimeOffset.UnixEpoch);
        var secondMark = await repo.TryMarkUsedAsync("D3CODE", "Tester2", DateTimeOffset.UnixEpoch);
        var after = await repo.FindByCodeAsync("D3CODE");

        Assert.NotNull(found);
        Assert.True(marked);
        Assert.False(secondMark);
        Assert.False(after!.Valid);
        Assert.Equal("Tester", after.UsedBy);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
