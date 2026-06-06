using System.Text;
using Maple.Adapters.V113.Channel;
using Maple.Application.Guilds;
using Maple.Core.Characters;
using Maple.Core.Guilds;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelGuildPacketTests
{
    [Fact]
    public void ShowGuildInfo_WritesJavaGuildInfoLayout()
    {
        var leader = Member(1, "Leader", rank: Guild.LeaderRank);
        var guest = Member(2, "Guest", rank: Guild.DefaultMemberRank);
        var guild = new GuildState(
            25,
            "Forge",
            leader.CharacterId,
            500,
            new GuildEmblem { LogoBackground = 7, LogoBackgroundColor = 2, Logo = 9, LogoColor = 4 },
            10,
            "notice",
            123456,
            0,
            new[] { "Master", "Jr", "Rank3", "Rank4", "Rank5" },
            new[] { leader, guest });

        var packet = V113GuildPackets.ShowGuildInfo(guild);
        var reader = new PacketReader(packet);

        Assert.Equal(V113GuildPackets.SendGuildOperationOpcode, reader.ReadShort());
        Assert.Equal(V113GuildPackets.ShowGuildInfoCode, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(25, reader.ReadInt());
        Assert.Equal("Forge", reader.ReadMapleString());
        Assert.Equal(new[] { "Master", "Jr", "Rank3", "Rank4", "Rank5" }, ReadMapleStrings(reader, 5));
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(new[] { 1, 2 }, ReadInts(reader, 2));

        Assert.Equal("Leader", ReadFixedName(reader));
        Assert.Equal(100, reader.ReadInt());
        Assert.Equal(30, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(123456, reader.ReadInt());
        Assert.Equal(5, reader.ReadInt());

        Assert.Equal("Guest", ReadFixedName(reader));
        Assert.Equal(100, reader.ReadInt());
        Assert.Equal(30, reader.ReadInt());
        Assert.Equal(5, reader.ReadInt());
        Assert.Equal(1, reader.ReadInt());
        Assert.Equal(123456, reader.ReadInt());
        Assert.Equal(5, reader.ReadInt());

        Assert.Equal(10, reader.ReadInt());
        Assert.Equal(7, reader.ReadShort());
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(9, reader.ReadShort());
        Assert.Equal(4, reader.ReadByte());
        Assert.Equal("notice", reader.ReadMapleString());
        Assert.Equal(500, reader.ReadInt());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public async Task AcceptedOperation_SendsFullInfoToSelfAndBroadcastsNewMember()
    {
        var characters = new FakeCharacterRepository();
        var guilds = new FakeGuildRepository();
        var service = new GuildService(new InMemoryGuildRegistry(guilds, firstGuildId: 40), characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var guest = Player(2, "Guest");
        characters.Put(leader.Character);
        characters.Put(guest.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await service.InviteMemberAsync(leader.Character.Id, GuildMember.FromCharacter(guest.Character, channel: 1));
        var hook = new FakeGuildSessionHook();
        var handler = new V113GuildOperationHandler(service, hook);
        var selfPackets = new List<byte[]>();

        var request = new PacketWriter()
            .WriteByte((byte)V113GuildClientOperation.Accepted)
            .WriteInt(created.Guild!.Id)
            .WriteInt(guest.Character.Id)
            .ToArray();

        await handler.HandleGuildOperationAsync(
            new PacketReader(request),
            guest,
            channel: 1,
            (packet, _) =>
            {
                selfPackets.Add(packet);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(2, selfPackets.Count);
        AssertShowGuildInfo(selfPackets[0]);
        AssertNewGuildMember(selfPackets[1], guest.Character.Id, "Guest");

        var leaderPacket = Assert.Single(hook.SentPackets);
        Assert.Equal(leader.Character.Id, leaderPacket.CharacterId);
        AssertNewGuildMember(leaderPacket.Packet, guest.Character.Id, "Guest");
    }

    private static void AssertShowGuildInfo(byte[] packet)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113GuildPackets.SendGuildOperationOpcode, reader.ReadShort());
        Assert.Equal(V113GuildPackets.ShowGuildInfoCode, reader.ReadByte());
        Assert.Equal(1, reader.ReadByte());
    }

    private static void AssertNewGuildMember(byte[] packet, int characterId, string name)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113GuildPackets.SendGuildOperationOpcode, reader.ReadShort());
        Assert.Equal(V113GuildPackets.NewGuildMemberCode, reader.ReadByte());
        reader.ReadInt();
        Assert.Equal(characterId, reader.ReadInt());
        Assert.Equal(name, ReadFixedName(reader));
    }

    private static int[] ReadInts(PacketReader reader, int count)
    {
        var values = new int[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadInt();
        }

        return values;
    }

    private static string[] ReadMapleStrings(PacketReader reader, int count)
    {
        var values = new string[count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = reader.ReadMapleString();
        }

        return values;
    }

    private static string ReadFixedName(PacketReader reader)
    {
        var bytes = new byte[15];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = reader.ReadByte();
        }

        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }

    private static GuildMember Member(int id, string name, byte rank) => new()
    {
        CharacterId = id,
        Name = name,
        Level = 30,
        JobId = 100,
        GuildRank = rank,
        GuildId = 25,
        AllianceRank = Guild.DefaultAllianceRank,
        IsOnline = true,
    };

    private static Player Player(int id, string name, int meso = 0, int mapId = 100000000) =>
        new(new Character
        {
            Id = id,
            Name = name,
            Level = 30,
            Job = 100,
            Meso = meso,
            MapId = mapId,
        }, new Position(0, 0, 0, 0));

    private sealed class FakeGuildSessionHook : IV113GuildSessionHook
    {
        public List<(int CharacterId, byte[] Packet)> SentPackets { get; } = new();

        public ValueTask<V113GuildSessionPlayer?> FindOnlinePlayerByNameAsync(string characterName, CancellationToken ct) =>
            ValueTask.FromResult<V113GuildSessionPlayer?>(null);

        public Task SendToCharacterAsync(int characterId, byte[] packet, CancellationToken ct)
        {
            SentPackets.Add((characterId, packet));
            return Task.CompletedTask;
        }

        public Task UpdateGuildStatusAsync(int characterId, int guildId, byte guildRank, byte allianceRank, CancellationToken ct) =>
            Task.CompletedTask;
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

    private sealed class FakeCharacterRepository : ICharacterRepository
    {
        private readonly Dictionary<int, Character> _characters = new();

        public void Put(Character character) => _characters[character.Id] = character;

        public Task<IReadOnlyList<Character>> GetByAccountAsync(int accountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Character>>(_characters.Values.Where(c => c.AccountId == accountId).ToList());

        public Task<Character?> FindByIdAsync(int characterId, CancellationToken ct = default) =>
            Task.FromResult(_characters.GetValueOrDefault(characterId));

        public Task<Character?> FindByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(_characters.Values.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Character character, CancellationToken ct = default)
        {
            Put(character);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Character character, CancellationToken ct = default)
        {
            Put(character);
            return Task.CompletedTask;
        }
    }
}
