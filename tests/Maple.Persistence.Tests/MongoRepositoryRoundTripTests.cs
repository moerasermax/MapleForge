using System.Text.Json;
using EphemeralMongo;
using Maple.Core.Accounts;
using Maple.Core.Characters;
using Maple.Core.Inventory;
using Maple.Persistence;
using Maple.Persistence.Accounts;
using Maple.Persistence.Characters;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace Maple.Persistence.Tests;

public sealed class MongoRepositoryRoundTripTests : IAsyncLifetime
{
    private IMongoRunner? _runner;
    private ServiceProvider? _provider;

    private IAccountRepository Accounts => _provider!.GetRequiredService<IAccountRepository>();

    private ICharacterRepository Characters => _provider!.GetRequiredService<ICharacterRepository>();

    public async Task InitializeAsync()
    {
        _runner = await MongoRunner.RunAsync(new MongoRunnerOptions
        {
            AdditionalArguments = ["--quiet"],
            ConnectionTimeout = TimeSpan.FromSeconds(60),
        });

        _provider = new ServiceCollection()
            .AddMaplePersistence(_ => new MapleDatabaseOptions
            {
                MongoConnectionString = _runner.ConnectionString,
                MongoDatabaseName = $"maple_persistence_tests_{Guid.NewGuid():N}",
            })
            .BuildServiceProvider();
    }

    public Task DisposeAsync()
    {
        _provider?.Dispose();
        _runner?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AccountRoundTrip_PreservesAllFieldsAndUniqueName()
    {
        var account = new Account
        {
            AccountName = "codex",
            PasswordHash = "$2a$10$abcdef",
            CreatedAt = new DateTime(2026, 6, 6, 1, 2, 3, DateTimeKind.Utc),
            LastLoginAt = new DateTime(2026, 6, 6, 4, 5, 6, DateTimeKind.Utc),
            IsBanned = true,
            BanReason = "integration-test",
            Gender = 1,
            SecondPassword = "1234",
        };

        await Accounts.AddAsync(account);

        Assert.True(account.Id > 0);
        var loaded = await Accounts.FindByNameAsync(account.AccountName);
        AssertSameDocument(account, loaded);

        account.IsBanned = false;
        account.BanReason = string.Empty;
        account.LastLoginAt = new DateTime(2026, 6, 6, 7, 8, 9, DateTimeKind.Utc);
        account.SecondPassword = "5678";
        await Accounts.UpdateAsync(account);

        loaded = await Accounts.FindByNameAsync(account.AccountName);
        AssertSameDocument(account, loaded);

        var duplicate = new Account
        {
            AccountName = account.AccountName,
            PasswordHash = "other",
            CreatedAt = new DateTime(2026, 6, 6, 10, 11, 12, DateTimeKind.Utc),
        };

        Assert.False(await Accounts.TryAddAsync(duplicate));
    }

    [Fact]
    public async Task CharacterRoundTrip_PreservesWholePocoDocument()
    {
        var account = new Account
        {
            AccountName = "owner",
            PasswordHash = "$2a$10$owner",
            CreatedAt = new DateTime(2026, 6, 6, 0, 0, 0, DateTimeKind.Utc),
        };
        await Accounts.AddAsync(account);

        var character = new Character
        {
            AccountId = account.Id,
            Name = "RoundTrip",
            Gender = 1,
            SkinColor = 2,
            Face = 20000,
            Hair = 30000,
            Level = 42,
            Job = 111,
            Stats = new CharacterStats
            {
                Str = 31,
                Dex = 32,
                Int = 33,
                Luk = 34,
                Hp = 1234,
                MaxHp = 2345,
                Mp = 345,
                MaxMp = 456,
            },
            RemainingAp = 5,
            RemainingSp = 6,
            Exp = 987654,
            Fame = 77,
            GachExp = 88,
            MapId = 100000000,
            SpawnPoint = 3,
            Meso = 123456789,
            Equips =
            [
                new EquipEntry { Position = -1, ItemId = 1002140 },
                new EquipEntry { Position = -11, ItemId = 1302000 },
            ],
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Equip,
                    IsEquip = true,
                    ItemId = 1302000,
                    Slot = 1,
                    Quantity = 1,
                    Owner = "owner",
                    Expiration = -1,
                    Flag = 1,
                    UniqueId = 1001,
                    UpgradeSlots = 7,
                    Level = 2,
                    ItemLevel = 3,
                    ItemExp = 400,
                    Str = 1,
                    Dex = 2,
                    Int = 3,
                    Luk = 4,
                    Hp = 5,
                    Mp = 6,
                    Watk = 7,
                    Matk = 8,
                    Wdef = 9,
                    Mdef = 10,
                    Acc = 11,
                    Avoid = 12,
                    Hands = 13,
                    Speed = 14,
                    Jump = 15,
                },
                new ItemRecord
                {
                    Type = (byte)InventoryType.Use,
                    IsEquip = false,
                    ItemId = 2000000,
                    Slot = 2,
                    Quantity = 25,
                    Owner = string.Empty,
                    Expiration = -1,
                    Flag = 0,
                    UniqueId = 1002,
                },
            ],
        };

        await Characters.AddAsync(character);

        Assert.True(character.Id > 0);
        AssertSameDocument(character, await Characters.FindByIdAsync(character.Id));
        AssertSameDocument(character, await Characters.FindByNameAsync(character.Name));

        var byAccount = await Characters.GetByAccountAsync(account.Id);
        var onlyCharacter = Assert.Single(byAccount);
        AssertSameDocument(character, onlyCharacter);

        character.Level = 43;
        character.Meso = 222222;
        character.Items[1].Quantity = 10;
        character.Equips.Add(new EquipEntry { Position = -5, ItemId = 1040002 });
        await Characters.UpdateAsync(character);

        AssertSameDocument(character, await Characters.FindByIdAsync(character.Id));

        await Assert.ThrowsAsync<MongoWriteException>(() => Characters.AddAsync(new Character
        {
            AccountId = account.Id,
            Name = character.Name,
        }));
    }

    [Fact]
    public void AddMaplePersistence_DefaultsToMongoProvider()
    {
        var options = new MapleDatabaseOptions
        {
            MongoConnectionString = _runner!.ConnectionString,
            MongoDatabaseName = "provider_check",
        };

        using var provider = new ServiceCollection()
            .AddMaplePersistence(_ => options)
            .BuildServiceProvider();

        Assert.IsType<MongoAccountRepository>(provider.GetRequiredService<IAccountRepository>());
        Assert.IsType<MongoCharacterRepository>(provider.GetRequiredService<ICharacterRepository>());
    }

    private static void AssertSameDocument<T>(T expected, T? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(Json(expected), Json(actual));
    }

    private static string Json<T>(T value)
    {
        return JsonSerializer.Serialize(value);
    }
}
