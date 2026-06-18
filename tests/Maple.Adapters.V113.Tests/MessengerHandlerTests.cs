using Maple.Adapters.V113.Channel;
using Maple.Application.Social;
using Maple.Core.Characters;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class MessengerHandlerTests
{
    [Fact]
    public async Task Create_OpenZero_CreatesMessengerWithSelfInSlotZero()
    {
        var service = new MessengerService(firstMessengerId: 20);
        var hook = new FakeMessengerSessionHook();
        var handler = new V113MessengerHandler(service, hook);
        var player = Player(1, "Alice");
        var selfPackets = new List<byte[]>();

        var request = new PacketWriter()
            .WriteByte((byte)V113MessengerClientMode.Open)
            .WriteInt(0)
            .ToArray();

        await handler.HandleMessengerAsync(
            new PacketReader(request),
            player,
            channelIndex: 0,
            SendTo(selfPackets),
            CancellationToken.None);

        var messenger = service.GetMessengerForCharacter(player.Character.Id);
        Assert.NotNull(messenger);
        Assert.Equal(20, messenger.Id);
        Assert.Equal(player.Character.Id, messenger.Members[0]?.CharacterId);
        Assert.Equal(0, messenger.Members[0]?.Position);
        Assert.Empty(selfPackets);
    }

    [Fact]
    public async Task Join_OpenExisting_BroadcastsExistingAndNewMembers()
    {
        var service = new MessengerService(firstMessengerId: 30);
        var leader = Player(1, "Alice");
        var guest = Player(2, "Bob");
        var hook = new FakeMessengerSessionHook();
        hook.Register(leader, channelIndex: 0);
        var handler = new V113MessengerHandler(service, hook);
        var selfPackets = new List<byte[]>();

        var created = service.CreateMessenger(new(leader.Character.Id, leader.Character.Name, ChannelIndex: 0, Position: 0));
        var request = new PacketWriter()
            .WriteByte((byte)V113MessengerClientMode.Open)
            .WriteInt(created.Id)
            .ToArray();

        await handler.HandleMessengerAsync(
            new PacketReader(request),
            guest,
            channelIndex: 1,
            SendTo(selfPackets),
            CancellationToken.None);

        var messenger = service.GetMessenger(created.Id);
        Assert.Equal(guest.Character.Id, messenger?.Members[1]?.CharacterId);

        var leaderPacket = Assert.Single(hook.SentPackets);
        Assert.Equal(leader.Character.Id, leaderPacket.CharacterId);
        AssertAddPlayer(leaderPacket.Packet, expectedName: "Bob", expectedPosition: 1, expectedChannel: 1);

        Assert.Equal(2, selfPackets.Count);
        AssertAddPlayer(selfPackets[0], expectedName: "Alice", expectedPosition: 0, expectedChannel: 0);
        AssertJoin(selfPackets[1], expectedPosition: 1);
    }

    [Fact]
    public async Task Message_BroadcastsMessengerChatToOtherMembersOnly()
    {
        var service = new MessengerService(firstMessengerId: 40);
        var leader = Player(1, "Alice");
        var guest = Player(2, "Bob");
        var hook = new FakeMessengerSessionHook();
        var handler = new V113MessengerHandler(service, hook);
        var selfPackets = new List<byte[]>();

        var created = service.CreateMessenger(new(leader.Character.Id, leader.Character.Name, ChannelIndex: 0, Position: 0));
        Assert.True(service.JoinMessenger(created.Id, new(guest.Character.Id, guest.Character.Name, ChannelIndex: 0, Position: 0)));

        var request = new PacketWriter()
            .WriteByte((byte)V113MessengerClientMode.Message)
            .WriteMapleString("hello messenger")
            .ToArray();

        await handler.HandleMessengerAsync(
            new PacketReader(request),
            leader,
            channelIndex: 0,
            SendTo(selfPackets),
            CancellationToken.None);

        Assert.Empty(selfPackets);
        var sent = Assert.Single(hook.SentPackets);
        Assert.Equal(guest.Character.Id, sent.CharacterId);
        AssertChat(sent.Packet, "hello messenger");
    }

    private static Player Player(int id, string name) =>
        new(
            new Character
            {
                Id = id,
                Name = name,
                Level = 30,
                Job = 100,
                MapId = 100000000 + id,
                Gender = 0,
                SkinColor = 0,
                Face = 20000,
                Hair = 30000,
            },
            new Position(0, 0, 0, 0));

    private static Func<byte[], CancellationToken, Task> SendTo(List<byte[]> packets) =>
        (packet, _) =>
        {
            packets.Add(packet);
            return Task.CompletedTask;
        };

    private static void AssertAddPlayer(byte[] packet, string expectedName, int expectedPosition, short expectedChannel)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113MessengerPackets.SendMessengerOpcode, reader.ReadShort());
        Assert.Equal(0x00, reader.ReadByte());
        Assert.Equal(expectedPosition, reader.ReadByte());
        reader.Skip(29);
        Assert.Equal(expectedName, reader.ReadMapleString());
        Assert.Equal(expectedChannel, reader.ReadShort());
        Assert.Equal(0, reader.Remaining);
    }

    private static void AssertJoin(byte[] packet, int expectedPosition)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113MessengerPackets.SendMessengerOpcode, reader.ReadShort());
        Assert.Equal(0x01, reader.ReadByte());
        Assert.Equal(expectedPosition, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    private static void AssertChat(byte[] packet, string expectedText)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113MessengerPackets.SendMessengerOpcode, reader.ReadShort());
        Assert.Equal(0x06, reader.ReadByte());
        Assert.Equal(expectedText, reader.ReadMapleString());
        Assert.Equal(0, reader.Remaining);
    }

    private sealed class FakeMessengerSessionHook : IV113MessengerSessionHook
    {
        private readonly Dictionary<string, V113MessengerSessionPlayer> _players = new(StringComparer.OrdinalIgnoreCase);

        public List<(int CharacterId, byte[] Packet)> SentPackets { get; } = new();

        public void Register(Player player, int channelIndex)
        {
            _players[player.Character.Name] = new(
                player.Character.Id,
                player.Character.Name,
                channelIndex,
                player.Character);
        }

        public ValueTask<V113MessengerSessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct) =>
            ValueTask.FromResult(_players.GetValueOrDefault(characterName));

        public Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
        {
            SentPackets.Add((characterId, packet));
            return Task.CompletedTask;
        }
    }
}
