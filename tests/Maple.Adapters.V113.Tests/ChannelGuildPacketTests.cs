using System.Text;
using Maple.Adapters.V113.Channel;
using Maple.Application.Alliances;
using Maple.Application.Guilds;
using Maple.Application.OnlinePlayers;
using Maple.Core.Alliances;
using Maple.Core.Characters;
using Maple.Core.Guilds;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class ChannelGuildPacketTests
{
    [Fact]
    public void GuildCapacityChange_WritesJavaLayout()
    {
        // 對照 Java MaplePacketCreator.guildCapacityChange：GUILD_OPERATION + 0x3A + int guildId + byte capacity。
        var packet = V113GuildPackets.GuildCapacityChange(25, 15);
        var r = new PacketReader(packet);

        Assert.Equal(V113GuildPackets.SendGuildOperationOpcode, r.ReadShort());
        Assert.Equal(V113GuildPackets.GuildCapacityChangedCode, r.ReadByte());
        Assert.Equal(25, r.ReadInt());
        Assert.Equal(15, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void GuildDisband_WritesJavaLayout()
    {
        // 對照 Java MaplePacketCreator.guildDisband：GUILD_OPERATION + 0x32 + int guildId + byte 1。
        var packet = V113GuildPackets.GuildDisband(25);
        var r = new PacketReader(packet);

        Assert.Equal(V113GuildPackets.SendGuildOperationOpcode, r.ReadShort());
        Assert.Equal(V113GuildPackets.GuildDisbandCode, r.ReadByte());
        Assert.Equal(25, r.ReadInt());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(0, r.Remaining);
    }

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
    public void SpawnPlayer_WritesGuildDisplayBeforeBuffMasks()
    {
        var character = new Character
        {
            Id = 9,
            Name = "Hero",
            Level = 12,
        };
        var guild = new V113SpawnGuildInfo(
            "Forge",
            LogoBackground: 7,
            LogoBackgroundColor: 2,
            Logo: 9,
            LogoColor: 4);

        var player = new Player(character, new Position(0, 0, 0, 0));
        var reader = new PacketReader(V113MapPackets.SpawnPlayer(player, 10, 20, 0, 30, guild));

        Assert.Equal(unchecked((short)0x99), reader.ReadShort());
        Assert.Equal(9, reader.ReadInt());
        Assert.Equal(12, reader.ReadByte());
        Assert.Equal("Hero", reader.ReadMapleString());
        Assert.Equal("Forge", reader.ReadMapleString());
        Assert.Equal(7, reader.ReadShort());
        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(9, reader.ReadShort());
        Assert.Equal(4, reader.ReadByte());
        Assert.Equal(0, reader.ReadInt());
        Assert.Equal(0x00FFFC00, reader.ReadInt());
    }

    [Fact]
    public async Task AcceptedOperation_SendsFullInfoToSelfAndBroadcastsNewMember()
    {
        var characters = new FakeCharacterRepository();
        var registry = new InMemoryGuildRegistry(new FakeGuildRepository(), firstGuildId: 40);
        var service = new GuildService(registry, characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var guest = Player(2, "Guest");
        characters.Put(leader.Character);
        characters.Put(guest.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await service.InviteMemberAsync(leader.Character.Id, GuildMember.FromCharacter(guest.Character, channel: 1));
        var hook = new FakeGuildSessionHook();
        var handler = new V113GuildOperationHandler(service, hook, new AllianceService(new FakeAllianceRepository(), registry));
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

    [Fact]
    public async Task CentralGuildSessionHook_UsesOnlineRegistryForLookupSendAndStatusUpdate()
    {
        var online = new InMemoryOnlinePlayerRegistry();
        var player = Player(2, "Guest");
        player.JoinGuild(25, Guild.DefaultMemberRank);
        var sentPackets = new List<byte[]>();
        online.Register(player, 3, (packet, _) =>
            {
                sentPackets.Add(packet);
                return Task.CompletedTask;
            },
            new object());
        var hook = new CentralGuildSessionHook(online);

        var found = await hook.FindOnlinePlayerByNameAsync("guest", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(player.Character.Id, found.CharacterId);
        Assert.Equal(25, found.GuildId);
        Assert.Equal(3, found.Channel);

        await hook.UpdateGuildStatusAsync(
            player.Character.Id,
            guildId: 40,
            guildRank: Guild.JuniorMasterRank,
            allianceRank: 0,
            CancellationToken.None);
        Assert.Equal(40, player.Character.GuildId);
        Assert.Equal(Guild.JuniorMasterRank, player.Character.GuildRank);
        Assert.Equal(Guild.DefaultAllianceRank, player.Character.AllianceRank);

        var packet = new byte[] { 0x03, 0x04 };
        await hook.SendToCharacterAsync(player.Character.Id, packet, CancellationToken.None);
        Assert.Same(packet, Assert.Single(sentPackets));
    }

    [Fact]
    public async Task OnPlayerLoggedInAsync_MemberOfAlliedGuild_BroadcastsAllianceMemberOnlineToOtherGuild()
    {
        var (service, alliances, guildA, guildB, allianceId) = await NewAlliedGuildsAsync();
        // GuildB 的 leader 先前已在線（模擬對照組情境：另一個公會的成員已經上線，等著收到通知）。
        var playerB = Player(2, "LeaderB");
        playerB.JoinGuild(guildB.Id, Guild.LeaderRank);
        await service.SetMemberOnlineAsync(playerB, online: true, channel: 5, CancellationToken.None);

        var hook = new FakeGuildSessionHook();
        var handler = new V113GuildOperationHandler(service, hook, alliances);
        var playerA = Player(1, "LeaderA");
        playerA.JoinGuild(guildA.Id, Guild.LeaderRank);

        // 對照 Java MapleGuild.setOnline：狀態翻轉且公會屬於同盟時，要通知同盟裡「其他公會」的所有
        // 成員（整個來源公會被排除，因為公會內部已經用 GuildMemberOnline 通知過）。
        await handler.OnPlayerLoggedInAsync(playerA, channel: 3, (_, _) => Task.CompletedTask, CancellationToken.None);

        var notice = Assert.Single(hook.SentPackets, p => p.CharacterId == playerB.Character.Id);
        var reader = new PacketReader(notice.Packet);
        Assert.Equal(V113AlliancePackets.SendAllianceOperationOpcode, reader.ReadShort());
        Assert.Equal(V113AlliancePackets.AllianceMemberOnlineCode, reader.ReadByte());
        Assert.Equal(allianceId, reader.ReadInt());
        Assert.Equal(guildA.Id, reader.ReadInt());
        Assert.Equal(playerA.Character.Id, reader.ReadInt());
        Assert.Equal(1, reader.ReadByte());
    }

    [Fact]
    public async Task OnPlayerLoggedOutAsync_MemberOfAlliedGuild_BroadcastsAllianceMemberOfflineToOtherGuild()
    {
        var (service, alliances, guildA, guildB, allianceId) = await NewAlliedGuildsAsync();
        var playerB = Player(2, "LeaderB");
        playerB.JoinGuild(guildB.Id, Guild.LeaderRank);
        await service.SetMemberOnlineAsync(playerB, online: true, channel: 5, CancellationToken.None);

        var hook = new FakeGuildSessionHook();
        var handler = new V113GuildOperationHandler(service, hook, alliances);
        var playerA = Player(1, "LeaderA");
        playerA.JoinGuild(guildA.Id, Guild.LeaderRank);
        await service.SetMemberOnlineAsync(playerA, online: true, channel: 3, CancellationToken.None);

        await handler.OnPlayerLoggedOutAsync(playerA, CancellationToken.None);

        var notice = Assert.Single(hook.SentPackets, p => p.CharacterId == playerB.Character.Id);
        var reader = new PacketReader(notice.Packet);
        Assert.Equal(V113AlliancePackets.SendAllianceOperationOpcode, reader.ReadShort());
        Assert.Equal(V113AlliancePackets.AllianceMemberOnlineCode, reader.ReadByte());
        Assert.Equal(allianceId, reader.ReadInt());
        Assert.Equal(guildA.Id, reader.ReadInt());
        Assert.Equal(playerA.Character.Id, reader.ReadInt());
        Assert.Equal(0, reader.ReadByte());
    }

    [Fact]
    public async Task OnPlayerLoggedInAsync_GuildNotInAlliance_DoesNotBroadcastAllianceMemberOnline()
    {
        var registry = new InMemoryGuildRegistry(new FakeGuildRepository());
        var characters = new FakeCharacterRepository();
        var service = new GuildService(registry, characters);
        var alliances = new AllianceService(new FakeAllianceRepository(), registry);
        var createdA = await registry.CreateGuildAsync(
            new GuildMember { CharacterId = 1, Name = "LeaderA", GuildRank = Guild.LeaderRank },
            "GuildA",
            signature: 1);
        var hook = new FakeGuildSessionHook();
        var handler = new V113GuildOperationHandler(service, hook, alliances);
        var playerA = Player(1, "LeaderA");
        playerA.JoinGuild(createdA.Guild!.Id, Guild.LeaderRank);

        await handler.OnPlayerLoggedInAsync(playerA, channel: 3, (_, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Empty(hook.SentPackets);
    }

    [Fact]
    public async Task SyncMemberLevelJobAsync_BroadcastsGuildMemberLevelJobUpdateToSelfAndGuildmates()
    {
        var characters = new FakeCharacterRepository();
        var registry = new InMemoryGuildRegistry(new FakeGuildRepository(), firstGuildId: 50);
        var service = new GuildService(registry, characters);
        var leader = Player(1, "Leader", meso: GuildService.CreationCost, mapId: GuildService.CreationMapId);
        var junior = Player(2, "Junior");
        characters.Put(leader.Character);
        characters.Put(junior.Character);
        var created = await service.CreateGuildAsync(leader, "Forge", channel: 1);
        await service.InviteMemberAsync(leader.Character.Id, GuildMember.FromCharacter(junior.Character, channel: 1));
        await service.AcceptInviteAsync(junior, created.Guild!.Id, channel: 1);

        var hook = new FakeGuildSessionHook();
        var handler = new V113GuildOperationHandler(service, hook, new AllianceService(new FakeAllianceRepository(), registry));
        var selfPackets = new List<byte[]>();
        junior.Character.Level = 35;

        await handler.SyncMemberLevelJobAsync(
            junior,
            (packet, _) =>
            {
                selfPackets.Add(packet);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var selfPacket = Assert.Single(selfPackets);
        AssertGuildMemberLevelJobUpdate(selfPacket, created.Guild.Id, junior.Character.Id, 35);

        var leaderPacket = Assert.Single(hook.SentPackets, p => p.CharacterId == leader.Character.Id);
        AssertGuildMemberLevelJobUpdate(leaderPacket.Packet, created.Guild.Id, junior.Character.Id, 35);
    }

    [Fact]
    public async Task SyncMemberLevelJobAsync_MemberOfAlliedGuild_BroadcastsUpdateAllianceMemberToOtherGuild()
    {
        var (service, alliances, guildA, guildB, allianceId) = await NewAlliedGuildsAsync();
        var playerB = Player(2, "LeaderB");
        playerB.JoinGuild(guildB.Id, Guild.LeaderRank);
        await service.SetMemberOnlineAsync(playerB, online: true, channel: 5, CancellationToken.None);

        var hook = new FakeGuildSessionHook();
        var handler = new V113GuildOperationHandler(service, hook, alliances);
        var playerA = Player(1, "LeaderA");
        playerA.JoinGuild(guildA.Id, Guild.LeaderRank);
        await service.SetMemberOnlineAsync(playerA, online: true, channel: 3, CancellationToken.None);
        playerA.Character.Level = 35;

        await handler.SyncMemberLevelJobAsync(playerA, (_, _) => Task.CompletedTask, CancellationToken.None);

        var notice = Assert.Single(hook.SentPackets, p => p.CharacterId == playerB.Character.Id);
        var reader = new PacketReader(notice.Packet);
        Assert.Equal(V113AlliancePackets.SendAllianceOperationOpcode, reader.ReadShort());
        Assert.Equal(V113AlliancePackets.UpdateAllianceMemberCode, reader.ReadByte());
        Assert.Equal(allianceId, reader.ReadInt());
        Assert.Equal(guildA.Id, reader.ReadInt());
        Assert.Equal(playerA.Character.Id, reader.ReadInt());
        Assert.Equal(35, reader.ReadInt());
        Assert.Equal(playerA.Character.Job, reader.ReadInt());
    }

    private static void AssertGuildMemberLevelJobUpdate(byte[] packet, int guildId, int characterId, int level)
    {
        var reader = new PacketReader(packet);
        Assert.Equal(V113GuildPackets.SendGuildOperationOpcode, reader.ReadShort());
        Assert.Equal(V113GuildPackets.GuildMemberLevelJobChangedCode, reader.ReadByte());
        Assert.Equal(guildId, reader.ReadInt());
        Assert.Equal(characterId, reader.ReadInt());
        Assert.Equal(level, reader.ReadInt());
    }

    private static async Task<(GuildService Service, AllianceService Alliances, GuildState GuildA, GuildState GuildB, int AllianceId)> NewAlliedGuildsAsync()
    {
        var registry = new InMemoryGuildRegistry(new FakeGuildRepository());
        var characters = new FakeCharacterRepository();
        var service = new GuildService(registry, characters);
        var alliances = new AllianceService(new FakeAllianceRepository(), registry);

        var createdA = await registry.CreateGuildAsync(
            new GuildMember { CharacterId = 1, Name = "LeaderA", GuildRank = Guild.LeaderRank },
            "GuildA",
            signature: 1);
        var createdB = await registry.CreateGuildAsync(
            new GuildMember { CharacterId = 2, Name = "LeaderB", GuildRank = Guild.LeaderRank },
            "GuildB",
            signature: 2);

        var alliance = await alliances.CreateAllianceAsync("United", leaderCharacterId: 1, createdA.Guild!.Id, createdB.Guild!.Id);

        return (service, alliances, createdA.Guild!, createdB.Guild!, alliance.Alliance!.Id);
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

    private sealed class FakeAllianceRepository : IAllianceRepository
    {
        private readonly Dictionary<int, Alliance> _alliances = new();

        public Task<Alliance?> FindByIdAsync(int allianceId, CancellationToken ct = default) =>
            Task.FromResult(_alliances.GetValueOrDefault(allianceId));

        public Task SaveAsync(Alliance alliance, CancellationToken ct = default)
        {
            _alliances[alliance.Id] = alliance;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int allianceId, CancellationToken ct = default)
        {
            _alliances.Remove(allianceId);
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

        public Task<bool> DeleteAsync(int characterId, CancellationToken ct = default) => Task.FromResult(false);
    }
}
