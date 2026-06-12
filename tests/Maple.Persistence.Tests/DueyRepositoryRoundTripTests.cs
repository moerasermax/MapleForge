using LiteDB;
using Maple.Core.Duey;
using Maple.Core.Inventory;
using Maple.Persistence;
using Maple.Persistence.Duey;
using Microsoft.Extensions.DependencyInjection;

namespace Maple.Persistence.Tests;

public sealed class DueyRepositoryRoundTripTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"maple_duey_{Guid.NewGuid():N}.db");
    private readonly LiteDatabase _db;

    public DueyRepositoryRoundTripTests()
    {
        _db = new LiteDatabase(_dbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task LiteDbDueyPackageRepository_RoundTripsInboxAndRemovesExpired()
    {
        var repo = new LiteDbDueyPackageRepository(_db);
        await repo.AddAsync(new DueyPackage
        {
            SenderName = "Sender",
            RecipientCharacterId = 2,
            Meso = 12_000,
            Message = "gift",
            CreatedAtUnixMillis = 1_000,
            ExpiresAtUnixMillis = 100_000,
            Item = new ItemRecord
            {
                Type = (byte)InventoryType.Use,
                ItemId = 2_000_000,
                Quantity = 4,
            },
        });
        await repo.AddAsync(new DueyPackage
        {
            SenderName = "Expired",
            RecipientCharacterId = 2,
            CreatedAtUnixMillis = 1,
            ExpiresAtUnixMillis = 10,
        });

        Assert.Equal(1, await repo.DeleteExpiredAsync(2, 11));
        var inbox = await repo.GetInboxAsync(2, 11);
        var package = Assert.Single(inbox);
        Assert.True(package.Id > 0);
        Assert.Equal("Sender", package.SenderName);
        Assert.Equal(12_000, package.Meso);
        Assert.Equal(2_000_000, package.Item!.ItemId);

        Assert.NotNull(await repo.FindForRecipientAsync(package.Id, 2, 11));
        Assert.True(await repo.RemoveAsync(package.Id, 2));
        Assert.Empty(await repo.GetInboxAsync(2, 11));
    }

    [Fact]
    public void AddMapleDueyPersistence_SelectsLiteDbRepositoryWhenProviderIsLiteDb()
    {
        var instanceName = $"duey_di_{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), $"{instanceName}.db");
        using (var provider = new ServiceCollection()
                   .AddMaplePersistence(_ => new MapleDatabaseOptions
                   {
                       Provider = MapleDatabaseProvider.LiteDb,
                       DataDirectory = Path.GetTempPath(),
                       InstanceName = instanceName,
                   })
                   .AddMapleDueyPersistence()
                   .BuildServiceProvider())
        {
            Assert.IsType<LiteDbDueyPackageRepository>(provider.GetRequiredService<IDueyPackageRepository>());
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
