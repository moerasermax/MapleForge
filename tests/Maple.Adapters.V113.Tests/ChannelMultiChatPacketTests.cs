using Maple.Adapters.V113.Channel;
using Maple.Application.Chats;
using Maple.Application.Guilds;
using Maple.Application.Parties;
using Maple.Core.Characters;
using Maple.Core.Guilds;
using Maple.Core.IO;
using Maple.Core.Parties;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelMultiChatPacketTests
{
    [Fact]
    public void MultiChat_WritesJavaLayout()
    {
        var packet = V113ChatPackets.MultiChat("Alice", "hello", GroupChatKind.Party);
        var reader = new PacketReader(packet);

        Assert.Equal(V113ChatPackets.SendMultiChatOpcode, reader.ReadShort());
        Assert.Equal((byte)GroupChatKind.Party, reader.ReadByte());
        Assert.Equal("Alice", reader.ReadMapleString());
        Assert.Equal("hello", reader.ReadMapleString());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void WhisperAndReply_WriteJavaLayouts()
    {
        var whisper = new PacketReader(V113ChatPackets.Whisper("Alice", channel: 2, "secret"));

        Assert.Equal(V113ChatPackets.SendWhisperOpcode, whisper.ReadShort());
        Assert.Equal(0x12, whisper.ReadByte());
        Assert.Equal("Alice", whisper.ReadMapleString());
        Assert.Equal(1, whisper.ReadShort());
        Assert.Equal("secret", whisper.ReadMapleString());
        Assert.Equal(0, whisper.Remaining);

        var reply = new PacketReader(V113ChatPackets.WhisperReply("Bob", 1));

        Assert.Equal(V113ChatPackets.SendWhisperOpcode, reply.ReadShort());
        Assert.Equal(0x0A, reply.ReadByte());
        Assert.Equal("Bob", reply.ReadMapleString());
        Assert.Equal(1, reply.ReadByte());
        Assert.Equal(0, reply.Remaining);
    }

    [Fact]
    public void FindReplyWithMap_WritesJavaLayout()
    {
        var reader = new PacketReader(V113ChatPackets.FindReplyWithMap("Bob", 100000000, buddy: true));

        Assert.Equal(V113ChatPackets.SendWhisperOpcode, reader.ReadShort());
        Assert.Equal(72, reader.ReadByte());
        Assert.Equal("Bob", reader.ReadMapleString());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(100000000, reader.ReadInt());
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(0, reader.ReadByte());
        }

        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public async Task WhisperHandler_SendsWhisperToTargetAndSuccessReplyToSender()
    {
        var (handler, _) = CreateHandler();
        var sender = Player(1, "Alice");
        var target = Player(2, "Bob");
        var selfPackets = new List<byte[]>();
        var targetPackets = new List<byte[]>();

        handler.OnPlayerLoggedIn(sender, channel: 1, SendTo(selfPackets));
        handler.OnPlayerLoggedIn(target, channel: 2, SendTo(targetPackets));

        var request = new PacketWriter()
            .WriteByte((byte)V113WhisperClientMode.Whisper)
            .WriteMapleString("Bob")
            .WriteMapleString("secret")
            .ToArray();

        await handler.HandleWhisperFindAsync(
            new PacketReader(request),
            sender,
            channel: 1,
            SendTo(selfPackets),
            CancellationToken.None);

        AssertWhisper(Assert.Single(targetPackets), "Alice", channelForClient: 0, "secret");
        AssertWhisperReply(Assert.Single(selfPackets), "Bob", reply: 1);
    }

    [Fact]
    public async Task WhisperHandler_ReturnsNotFoundWhenTargetOffline()
    {
        var (handler, _) = CreateHandler();
        var sender = Player(1, "Alice");
        var selfPackets = new List<byte[]>();

        handler.OnPlayerLoggedIn(sender, channel: 1, SendTo(selfPackets));

        var request = new PacketWriter()
            .WriteByte((byte)V113WhisperClientMode.Whisper)
            .WriteMapleString("Missing")
            .WriteMapleString("secret")
            .ToArray();

        await handler.HandleWhisperFindAsync(
            new PacketReader(request),
            sender,
            channel: 1,
            SendTo(selfPackets),
            CancellationToken.None);

        AssertWhisperReply(Assert.Single(selfPackets), "Missing", reply: 0);
    }

    [Fact]
    public async Task PartyChat_BroadcastsToOnlinePartyMembersExceptSender()
    {
        var (handler, parties) = CreateHandler(firstPartyId: 40);
        var sender = Player(1, "Alice");
        var target = Player(2, "Bob");
        var selfPackets = new List<byte[]>();
        var targetPackets = new List<byte[]>();

        handler.OnPlayerLoggedIn(sender, channel: 1, SendTo(selfPackets));
        handler.OnPlayerLoggedIn(target, channel: 1, SendTo(targetPackets));
        parties.CreateParty(PartyMember.FromCharacter(sender.Character, channelIndex: 0));
        parties.JoinParty(40, PartyMember.FromCharacter(target.Character, channelIndex: 0));

        var request = GroupChatRequest(GroupChatKind.Party, [target.Character.Id], "party hi");

        await handler.HandleGroupChatAsync(new PacketReader(request), sender, CancellationToken.None);

        Assert.Empty(selfPackets);
        AssertMultiChat(Assert.Single(targetPackets), GroupChatKind.Party, "Alice", "party hi");
    }

    [Fact]
    public async Task BuddyChat_SendsOnlyWhenRecipientHasVisibleSenderBuddy()
    {
        var (handler, _) = CreateHandler();
        var sender = Player(1, "Alice");
        var visibleTarget = Player(2, "Bob");
        var hiddenTarget = Player(3, "Carol");
        var visiblePackets = new List<byte[]>();
        var hiddenPackets = new List<byte[]>();

        visibleTarget.Character.BuddyList.Put(new BuddyEntry
        {
            CharacterId = sender.Character.Id,
            Name = sender.Character.Name,
            Visible = true,
        });
        hiddenTarget.Character.BuddyList.Put(new BuddyEntry
        {
            CharacterId = sender.Character.Id,
            Name = sender.Character.Name,
            Visible = false,
        });

        handler.OnPlayerLoggedIn(sender, channel: 1, SendTo(new List<byte[]>()));
        handler.OnPlayerLoggedIn(visibleTarget, channel: 1, SendTo(visiblePackets));
        handler.OnPlayerLoggedIn(hiddenTarget, channel: 1, SendTo(hiddenPackets));

        var request = GroupChatRequest(
            GroupChatKind.Buddy,
            [visibleTarget.Character.Id, hiddenTarget.Character.Id],
            "buddy hi");

        await handler.HandleGroupChatAsync(new PacketReader(request), sender, CancellationToken.None);

        AssertMultiChat(Assert.Single(visiblePackets), GroupChatKind.Buddy, "Alice", "buddy hi");
        Assert.Empty(hiddenPackets);
    }

    [Fact]
    public async Task GuildChat_BroadcastsToOnlineGuildMembersExceptSender()
    {
        var online = new InMemoryChatOnlineRegistry();
        var partyRegistry = new InMemoryPartyRegistry();
        var guildRegistry = new InMemoryGuildRegistry(new FakeGuildRepository(), firstGuildId: 60);
        var chatService = new ChatService(online, partyRegistry, guildRegistry);
        var handler = new V113ChatHandler(chatService, new CentralChatSessionHook(online));
        var sender = Player(1, "Alice");
        var target = Player(2, "Bob");
        var targetPackets = new List<byte[]>();

        var created = await guildRegistry.CreateGuildAsync(
            GuildMember.FromCharacter(sender.Character, channel: 1, rank: Guild.LeaderRank),
            "Forge",
            signature: 123,
            CancellationToken.None);
        var guildId = created.Guild!.Id;
        sender.Character.GuildId = guildId;
        target.Character.GuildId = guildId;
        await guildRegistry.AddMemberAsync(
            guildId,
            GuildMember.FromCharacter(target.Character, channel: 1, rank: Guild.DefaultMemberRank, guildId: guildId),
            CancellationToken.None);

        handler.OnPlayerLoggedIn(sender, channel: 1, SendTo(new List<byte[]>()));
        handler.OnPlayerLoggedIn(target, channel: 1, SendTo(targetPackets));

        var request = GroupChatRequest(GroupChatKind.Guild, [target.Character.Id], "guild hi");

        await handler.HandleGroupChatAsync(new PacketReader(request), sender, CancellationToken.None);

        AssertMultiChat(Assert.Single(targetPackets), GroupChatKind.Guild, "Alice", "guild hi");
    }

    private static (V113ChatHandler Handler, PartyService Parties) CreateHandler(int firstPartyId = 1)
    {
        var online = new InMemoryChatOnlineRegistry();
        var partyRegistry = new InMemoryPartyRegistry(firstPartyId);
        var chatService = new ChatService(online, partyRegistry, new InMemoryGuildRegistry(new FakeGuildRepository()));
        var handler = new V113ChatHandler(chatService, new CentralChatSessionHook(online));
        return (handler, new PartyService(partyRegistry));
    }

    private static byte[] GroupChatRequest(GroupChatKind kind, IReadOnlyList<int> recipientIds, string text)
    {
        var writer = new PacketWriter()
            .WriteByte((byte)kind)
            .WriteByte(recipientIds.Count);
        foreach (var recipientId in recipientIds)
        {
            writer.WriteInt(recipientId);
        }

        return writer.WriteMapleString(text).ToArray();
    }

    private static Func<byte[], CancellationToken, Task> SendTo(List<byte[]> packets) =>
        (packet, _) =>
        {
            packets.Add(packet);
            return Task.CompletedTask;
        };

    private static Player Player(int id, string name) =>
        new(
            new Character
            {
                Id = id,
                Name = name,
                Level = 30,
                Job = 100,
                MapId = 100000000 + id,
            },
            new Position(0, 0, 0, 0));

    private static void AssertMultiChat(byte[] packet, GroupChatKind kind, string senderName, string text)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113ChatPackets.SendMultiChatOpcode, reader.ReadShort());
        Assert.Equal((byte)kind, reader.ReadByte());
        Assert.Equal(senderName, reader.ReadMapleString());
        Assert.Equal(text, reader.ReadMapleString());
        Assert.Equal(0, reader.Remaining);
    }

    private static void AssertWhisper(byte[] packet, string senderName, short channelForClient, string text)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113ChatPackets.SendWhisperOpcode, reader.ReadShort());
        Assert.Equal(0x12, reader.ReadByte());
        Assert.Equal(senderName, reader.ReadMapleString());
        Assert.Equal(channelForClient, reader.ReadShort());
        Assert.Equal(text, reader.ReadMapleString());
        Assert.Equal(0, reader.Remaining);
    }

    private static void AssertWhisperReply(byte[] packet, string targetName, byte reply)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113ChatPackets.SendWhisperOpcode, reader.ReadShort());
        Assert.Equal(0x0A, reader.ReadByte());
        Assert.Equal(targetName, reader.ReadMapleString());
        Assert.Equal(reply, reader.ReadByte());
        Assert.Equal(0, reader.Remaining);
    }

    private sealed class FakeGuildRepository : IGuildRepository
    {
        private readonly Dictionary<int, Guild> _guilds = new();

        public Task<IReadOnlyList<Guild>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guild>>(_guilds.Values.ToList());

        public Task<Guild?> FindByIdAsync(int guildId, CancellationToken ct = default) =>
            Task.FromResult(_guilds.GetValueOrDefault(guildId));

        public Task<Guild?> FindByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(_guilds.Values.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Guild guild, CancellationToken ct = default)
        {
            _guilds[guild.Id] = guild;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Guild guild, CancellationToken ct = default)
        {
            _guilds[guild.Id] = guild;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int guildId, CancellationToken ct = default)
        {
            _guilds.Remove(guildId);
            return Task.CompletedTask;
        }
    }
}
