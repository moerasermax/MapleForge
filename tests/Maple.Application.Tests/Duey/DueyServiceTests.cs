using Maple.Application.Duey;
using Maple.Core.Characters;
using Maple.Core.Duey;
using Maple.Core.Inventory;
using Maple.Core.World;

namespace Maple.Application.Tests.Duey;

public sealed class DueyServiceTests
{
    [Fact]
    public async Task SendThenReceivePackage_MovesItemAndMesoThroughRepository()
    {
        var characters = new InMemoryCharacterRepository();
        var packages = new InMemoryDueyPackageRepository();
        var service = new DueyService(packages, characters, new FixedTimeProvider(1_000_000));

        var senderCharacter = new Character
        {
            Id = 1,
            AccountId = 10,
            Name = "Sender",
            Meso = 100_000,
            Items =
            [
                new ItemRecord
                {
                    Type = (byte)InventoryType.Use,
                    ItemId = 2_000_000,
                    Slot = 1,
                    Quantity = 10,
                },
            ],
        };
        var recipientCharacter = new Character
        {
            Id = 2,
            AccountId = 20,
            Name = "Receiver",
            Meso = 0,
        };
        characters.AddSeed(senderCharacter);
        characters.AddSeed(recipientCharacter);

        var sender = new Player(senderCharacter, new Position(0, 0, 0, 0));
        var send = await service.SendAsync(
            sender,
            new DueySendRequest(
                InventoryType.Use,
                ItemSlot: 1,
                Quantity: 4,
                Meso: 12_000,
                RecipientName: "Receiver",
                QuickDelivery: false,
                Message: "gift"));

        Assert.Equal(DueyResultStatus.Success, send.Status);
        Assert.Equal(83_000, sender.Character.Meso);
        Assert.Equal(6, sender.Inventory.By(InventoryType.Use).Get(1)!.Quantity);

        var inbox = await packages.GetInboxAsync(recipientCharacter.Id, 1_000_000, CancellationToken.None);
        var package = Assert.Single(inbox);
        Assert.Equal("Sender", package.SenderName);
        Assert.Equal(12_000, package.Meso);
        Assert.Equal(4, package.Item!.Quantity);
        Assert.Equal(2_000_000, package.Item.ItemId);

        var recipient = new Player(recipientCharacter, new Position(0, 0, 0, 0));
        var receive = await service.ReceiveAsync(recipient, package.Id);

        Assert.Equal(DueyResultStatus.Success, receive.Status);
        Assert.Equal(12_000, recipient.Character.Meso);
        Assert.Equal(2_000_000, recipient.Inventory.By(InventoryType.Use).Get(1)!.ItemId);
        Assert.Equal(4, recipient.Inventory.By(InventoryType.Use).Get(1)!.Quantity);
        Assert.Empty(await packages.GetInboxAsync(recipientCharacter.Id, 1_000_000, CancellationToken.None));
    }

    [Fact]
    public async Task SendPackage_RejectsMissingRecipientAndSameAccount()
    {
        var characters = new InMemoryCharacterRepository();
        var packages = new InMemoryDueyPackageRepository();
        var service = new DueyService(packages, characters, new FixedTimeProvider(1_000_000));
        var senderCharacter = new Character { Id = 1, AccountId = 10, Name = "Sender", Meso = 100_000 };
        var altCharacter = new Character { Id = 2, AccountId = 10, Name = "Alt" };
        characters.AddSeed(senderCharacter);
        characters.AddSeed(altCharacter);
        var sender = new Player(senderCharacter, new Position(0, 0, 0, 0));

        var missing = await service.SendAsync(sender, MesoOnly("Nobody"));
        var sameAccount = await service.SendAsync(sender, MesoOnly("Alt"));

        Assert.Equal(DueyResultStatus.RecipientNotFound, missing.Status);
        Assert.Equal(DueyResultStatus.SameAccount, sameAccount.Status);
        Assert.Empty(await packages.GetInboxAsync(altCharacter.Id, 1_000_000, CancellationToken.None));
    }

    [Theory]
    [InlineData(99_999, 0)]
    [InlineData(100_000, 800)]
    [InlineData(1_000_000, 18_000)]
    [InlineData(5_000_000, 150_000)]
    [InlineData(10_000_000, 400_000)]
    [InlineData(25_000_000, 1_250_000)]
    [InlineData(100_000_000, 6_000_000)]
    public void GetTaxAmount_MatchesJavaTaxLadder(int meso, int expectedTax)
    {
        Assert.Equal(expectedTax, DueyService.GetTaxAmount(meso));
    }

    private static DueySendRequest MesoOnly(string recipient) =>
        new(null, 0, 0, 10_000, recipient, QuickDelivery: false, Message: string.Empty);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(long unixMillis)
        {
            _now = DateTimeOffset.FromUnixTimeMilliseconds(unixMillis);
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class InMemoryCharacterRepository : ICharacterRepository
    {
        private readonly Dictionary<int, Character> _byId = new();
        private readonly Dictionary<string, Character> _byName = new(StringComparer.Ordinal);

        public void AddSeed(Character character)
        {
            _byId[character.Id] = character;
            _byName[character.Name] = character;
        }

        public Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Character>>(_byId.Values.Where(c => c.AccountId == accountId).ToList());

        public Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default)
            => Task.FromResult(_byId.GetValueOrDefault(characterId));

        public Task<Character?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_byName.GetValueOrDefault(name));

        public Task AddAsync(Character character, CancellationToken ct = default)
        {
            AddSeed(character);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Character character, CancellationToken ct = default)
        {
            AddSeed(character);
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(int characterId, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class InMemoryDueyPackageRepository : IDueyPackageRepository
    {
        private readonly List<DueyPackage> _packages = new();
        private int _nextId = 1;

        public Task AddAsync(DueyPackage package, CancellationToken ct = default)
        {
            package.Id = package.Id > 0 ? package.Id : _nextId++;
            _packages.Add(package);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DueyPackage>> GetInboxAsync(
            int recipientCharacterId,
            long nowUnixMillis,
            CancellationToken ct = default)
        {
            var list = _packages
                .Where(p => p.RecipientCharacterId == recipientCharacterId && p.ExpiresAtUnixMillis > nowUnixMillis)
                .OrderBy(p => p.Id)
                .ToList();

            return Task.FromResult<IReadOnlyList<DueyPackage>>(list);
        }

        public Task<DueyPackage?> FindForRecipientAsync(
            int packageId,
            int recipientCharacterId,
            long nowUnixMillis,
            CancellationToken ct = default)
        {
            return Task.FromResult(_packages.FirstOrDefault(
                p => p.Id == packageId &&
                     p.RecipientCharacterId == recipientCharacterId &&
                     p.ExpiresAtUnixMillis > nowUnixMillis));
        }

        public Task<bool> RemoveAsync(int packageId, int recipientCharacterId, CancellationToken ct = default)
        {
            var removed = _packages.RemoveAll(p => p.Id == packageId && p.RecipientCharacterId == recipientCharacterId);
            return Task.FromResult(removed > 0);
        }

        public Task<int> DeleteExpiredAsync(int recipientCharacterId, long nowUnixMillis, CancellationToken ct = default)
        {
            var removed = _packages.RemoveAll(
                p => p.RecipientCharacterId == recipientCharacterId && p.ExpiresAtUnixMillis <= nowUnixMillis);

            return Task.FromResult(removed);
        }
    }
}
