using Maple.Application.Buddies;
using Maple.Application.OnlinePlayers;
using Maple.Core.Characters;

namespace Maple.Application.Tests.Buddies;

public sealed class BuddyServiceTests
{
    [Fact]
    public async Task AddOnlineBuddy_AddsPendingRequestToTarget()
    {
        var owner = Character(1, "Owner");
        var target = Character(2, "Target");
        var repo = new FakeCharacterRepository(owner, target);
        var registry = new InMemoryOnlinePlayerRegistry();
        registry.Register(Online(target, channel: 2), new object());
        var service = new BuddyService(repo, registry);

        var result = await service.ModifyAsync(
            owner,
            new BuddyModifyRequest(BuddyModifyKind.Add, BuddyName: "Target", Group: "Friends"),
            channel: 1);

        var self = Assert.Single(owner.BuddyList.Entries);
        Assert.Equal(2, self.CharacterId);
        Assert.Equal("Friends", self.Group);
        Assert.False(self.Visible);

        var pending = Assert.Single(target.BuddyList.Entries);
        Assert.Equal(1, pending.CharacterId);
        Assert.True(pending.PendingRequest);
        Assert.False(pending.Visible);
        Assert.True(pending.RequestPrompted);

        var remote = Assert.Single(result.RemoteRequests);
        Assert.Equal(2, remote.Target.CharacterId);
        Assert.Equal(1, remote.CharacterIdFrom);
        Assert.NotNull(result.Self.BuddyList);
    }

    [Fact]
    public async Task AcceptPendingBuddy_MarksBothSidesVisibleAndNotifiesRequester()
    {
        var requester = Character(1, "Requester");
        requester.BuddyList.Put(new BuddyEntry { CharacterId = 2, Name = "Target", Group = "Friends" });
        var target = Character(2, "Target");
        target.BuddyList.Put(new BuddyEntry
        {
            CharacterId = 1,
            Name = "Requester",
            PendingRequest = true,
            Visible = false,
        });
        var repo = new FakeCharacterRepository(requester, target);
        var registry = new InMemoryOnlinePlayerRegistry();
        registry.Register(Online(requester, channel: 1), new object());
        var service = new BuddyService(repo, registry);

        var result = await service.ModifyAsync(
            target,
            new BuddyModifyRequest(BuddyModifyKind.Accept, BuddyCharacterId: 1),
            channel: 2);

        Assert.True(target.BuddyList.Get(1)?.Visible);
        Assert.True(requester.BuddyList.Get(2)?.Visible);
        Assert.False(target.BuddyList.Get(1)?.PendingRequest);

        var update = Assert.Single(result.RemoteChannelUpdates);
        Assert.Equal(1, update.Target.CharacterId);
        Assert.Equal(2, update.CharacterId);
        Assert.Equal(1, update.ChannelForClient);
    }

    [Fact]
    public void LogOnAndLogOff_UpdateVisibleBuddyChannels()
    {
        var alice = Character(1, "Alice");
        var bob = Character(2, "Bob");
        alice.BuddyList.Put(new BuddyEntry { CharacterId = 2, Name = "Bob", Visible = true });
        bob.BuddyList.Put(new BuddyEntry { CharacterId = 1, Name = "Alice", Visible = true });
        var registry = new InMemoryOnlinePlayerRegistry();
        var service = new BuddyService(new FakeCharacterRepository(alice, bob), registry);
        var aliceToken = new object();

        registry.Register(Online(bob, channel: 2), new object());
        service.LogOn(bob, channel: 2);
        registry.Register(Online(alice, channel: 1), aliceToken);
        var login = service.LogOn(alice, channel: 1);

        var selfBob = Assert.Single(login.Self.BuddyList!);
        Assert.Equal(2, selfBob.Channel);
        var loginUpdate = Assert.Single(login.RemoteChannelUpdates);
        Assert.Equal(2, loginUpdate.Target.CharacterId);
        Assert.Equal(1, loginUpdate.CharacterId);
        Assert.Equal(0, loginUpdate.ChannelForClient);
        Assert.Equal(1, bob.BuddyList.Get(1)?.Channel);

        var logout = service.LogOff(alice);
        registry.Deregister(alice.Id, aliceToken);

        var logoutUpdate = Assert.Single(logout.RemoteChannelUpdates);
        Assert.Equal(-1, logoutUpdate.ChannelForClient);
        Assert.Equal(-1, bob.BuddyList.Get(1)?.Channel);
    }

    private static Character Character(int id, string name)
        => new() { Id = id, Name = name };

    private static OnlinePlayer Online(Character character, int channel)
        => new(character.Id, character.Name, channel, character, SendNoop);

    private static Task SendNoop(byte[] packet, CancellationToken ct)
        => Task.CompletedTask;

    private sealed class FakeCharacterRepository : ICharacterRepository
    {
        private readonly Dictionary<int, Character> _byId;
        private readonly Dictionary<string, Character> _byName;

        public FakeCharacterRepository(params Character[] characters)
        {
            _byId = characters.ToDictionary(static c => c.Id);
            _byName = characters.ToDictionary(static c => c.Name, StringComparer.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Character>>(Array.Empty<Character>());

        public Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default)
            => Task.FromResult(_byId.GetValueOrDefault(characterId));

        public Task<Character?> FindByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_byName.GetValueOrDefault(name));

        public Task AddAsync(Character character, CancellationToken ct = default)
        {
            _byId[character.Id] = character;
            _byName[character.Name] = character;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Character character, CancellationToken ct = default)
        {
            _byId[character.Id] = character;
            _byName[character.Name] = character;
            return Task.CompletedTask;
        }
    }
}
