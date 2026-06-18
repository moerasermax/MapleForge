using Maple.Adapters.V113.Channel;
using Maple.Application.Alliances;
using Maple.Core.Alliances;
using Maple.Core.Characters;
using Maple.Core.Guilds;
using Maple.Core.IO;
using Maple.Core.World;

namespace Maple.Adapters.V113.Tests;

public sealed class AllianceHandlerTests
{
    [Fact]
    public async Task CreateAlliance_CreatesInitialAllianceAndWritesCreatePacket()
    {
        var service = NewService();

        var result = await service.CreateAllianceAsync("Union", leaderCharacterId: 100, leaderGuildId: 10, partnerGuildId: 20);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Alliance);
        Assert.Equal("Union", result.Alliance.Name);
        Assert.Equal(100, result.Alliance.LeaderId);
        Assert.Equal(new[] { 10, 20 }, result.Alliance.GuildIds);
        Assert.Equal(Alliance.InitialCapacity, result.Alliance.Capacity);

        var packet = V113AlliancePackets.CreateGuildAlliance(
            result.Alliance,
            [GuildStateOf(10, "Alpha", 100, result.Alliance.Id), GuildStateOf(20, "Beta", 200, result.Alliance.Id)]);
        var reader = new PacketReader(packet);

        Assert.Equal(V113AlliancePackets.SendAllianceOperationOpcode, reader.ReadShort());
        Assert.Equal(V113AlliancePackets.CreateGuildAllianceCode, reader.ReadByte());
        Assert.Equal(result.Alliance.Id, reader.ReadInt());
        Assert.Equal("Union", reader.ReadMapleString());
        for (var i = 0; i < Alliance.RankCount; i++)
        {
            reader.ReadMapleString();
        }

        Assert.Equal(2, reader.ReadByte());
        Assert.Equal(10, reader.ReadInt());
        Assert.Equal(20, reader.ReadInt());
        Assert.Equal(Alliance.InitialCapacity, reader.ReadInt());
        Assert.Equal(string.Empty, reader.ReadMapleString());
        Assert.Equal(10, reader.ReadInt());
    }

    [Fact]
    public async Task HandleAccept_AddsInvitedGuildAndReturnsAlliancePackets()
    {
        var service = NewService();
        var created = await service.CreateAllianceAsync("Union", 100, 10, 20, capacity: Alliance.MaximumGuilds);
        await service.InviteGuildAsync(created.Alliance!.Id, 30);
        var hook = new FakeAllianceSessionHook();
        hook.PutGuild(GuildStateOf(10, "Alpha", 100, created.Alliance.Id));
        hook.PutGuild(GuildStateOf(20, "Beta", 200, created.Alliance.Id));
        hook.PutGuild(GuildStateOf(30, "Guest", 300));
        var handler = new V113AllianceHandler(service, hook);
        var player = Player(300, "GuestLeader", guildId: 30, guildRank: Guild.LeaderRank);
        var request = new PacketWriter()
            .WriteByte((byte)V113AllianceClientOperation.Accept)
            .ToArray();

        var result = await handler.HandleAllianceOperationAsync(new PacketReader(request), player, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.SelfPackets.Count);
        Assert.Equal(4, result.GuildPackets.Count);
        Assert.Contains(result.GuildPackets, p => p.GuildId == 10 && PacketCode(p.Packet) == V113AlliancePackets.AddGuildToAllianceCode);
        Assert.Contains(result.GuildPackets, p => p.GuildId == 20 && PacketCode(p.Packet) == V113AlliancePackets.ChangeGuildInAllianceCode);

        var alliance = await service.GetAllianceInfoAsync(created.Alliance.Id);
        Assert.NotNull(alliance);
        Assert.Equal(new[] { 10, 20, 30 }, alliance.GuildIds);

        var selfReader = new PacketReader(result.SelfPackets[0]);
        Assert.Equal(V113AlliancePackets.SendAllianceOperationOpcode, selfReader.ReadShort());
        Assert.Equal(V113AlliancePackets.AllianceInfoCode, selfReader.ReadByte());
    }

    [Fact]
    public async Task HandleInvite_SendsAllianceInviteToTargetGuildLeader()
    {
        var service = NewService();
        var created = await service.CreateAllianceAsync("Union", 100, 10, 20, capacity: Alliance.MaximumGuilds);
        var hook = new FakeAllianceSessionHook();
        hook.PutGuild(GuildStateOf(10, "Alpha", 100, created.Alliance!.Id));
        hook.PutGuild(GuildStateOf(20, "Beta", 200, created.Alliance.Id));
        hook.InviteTargets["Guest"] = new V113AllianceInviteTarget(30, 300, "GuestLeader");
        var handler = new V113AllianceHandler(service, hook);
        var player = Player(100, "AlphaLeader", guildId: 10, guildRank: Guild.LeaderRank, allianceRank: Alliance.LeaderRank);
        var request = new PacketWriter()
            .WriteByte((byte)V113AllianceClientOperation.Invite)
            .WriteMapleString("Guest")
            .ToArray();

        var result = await handler.HandleAllianceOperationAsync(new PacketReader(request), player, CancellationToken.None);

        Assert.True(result.Succeeded);
        var delivery = Assert.Single(result.CharacterPackets);
        Assert.Equal(300, delivery.CharacterId);
        var packet = new PacketReader(delivery.Packet);
        Assert.Equal(V113AlliancePackets.SendAllianceOperationOpcode, packet.ReadShort());
        Assert.Equal(V113AlliancePackets.AllianceInviteCode, packet.ReadByte());
        Assert.Equal(10, packet.ReadInt());
        Assert.Equal("AlphaLeader", packet.ReadMapleString());
        Assert.Equal("Union", packet.ReadMapleString());
    }

    [Fact]
    public async Task HandleDeny_ConsumesInviteAndReturnsLeaderNotice()
    {
        var service = NewService();
        var created = await service.CreateAllianceAsync("Union", 100, 10, 20, capacity: Alliance.MaximumGuilds);
        await service.InviteGuildAsync(created.Alliance!.Id, 30);
        var hook = new FakeAllianceSessionHook();
        hook.PutGuild(GuildStateOf(30, "Guest", 300));
        var handler = new V113AllianceHandler(service, hook);
        var player = Player(300, "GuestLeader", guildId: 30, guildRank: Guild.LeaderRank);

        var result = await handler.HandleDenyAllianceRequestAsync(new PacketReader([]), player, CancellationToken.None);

        Assert.True(result.Succeeded);
        var notice = Assert.Single(result.CharacterNotices);
        Assert.Equal(100, notice.CharacterId);
        Assert.Contains("Guest Guild has rejected", notice.Message);

        var acceptAfterDeny = await service.AcceptInviteAsync(30);
        Assert.Equal(AllianceCommandStatus.InvalidInvite, acceptAfterDeny.Status);
    }

    private static AllianceService NewService() => new(new FakeAllianceRepository());

    private static byte PacketCode(byte[] packet)
    {
        var reader = new PacketReader(packet);
        reader.ReadShort();
        return reader.ReadByte();
    }

    private static GuildState GuildStateOf(int id, string name, int leaderId, int allianceId = 0)
    {
        var leader = new GuildMember
        {
            CharacterId = leaderId,
            Name = $"{name}Leader",
            Level = 30,
            JobId = 100,
            Channel = 1,
            GuildId = id,
            GuildRank = Guild.LeaderRank,
            AllianceRank = allianceId > 0 && leaderId == 100 ? Alliance.LeaderRank : Guild.DefaultAllianceRank,
            IsOnline = true,
        };

        return new GuildState(
            id,
            name,
            leaderId,
            500,
            new GuildEmblem(),
            10,
            string.Empty,
            1,
            allianceId,
            ["Master", "Jr", "Rank3", "Rank4", "Rank5"],
            [leader]);
    }

    private static Player Player(
        int id,
        string name,
        int guildId,
        byte guildRank,
        byte allianceRank = Guild.DefaultAllianceRank) =>
        new(
            new Character
            {
                Id = id,
                Name = name,
                Level = 30,
                Job = 100,
                GuildId = guildId,
                GuildRank = guildRank,
                AllianceRank = allianceRank,
            },
            new Position(0, 0, 0, 0));

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

    private sealed class FakeAllianceSessionHook : IV113AllianceSessionHook
    {
        private readonly Dictionary<int, GuildState> _guilds = new();
        private readonly Dictionary<int, V113AllianceMember> _members = new();

        public Dictionary<string, V113AllianceInviteTarget> InviteTargets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void PutGuild(GuildState guild)
        {
            _guilds[guild.Id] = guild;
            foreach (var member in guild.Members)
            {
                _members[member.CharacterId] = new V113AllianceMember(member.CharacterId, guild.Id, member.AllianceRank);
            }
        }

        public Task<GuildState?> GetGuildAsync(int guildId, CancellationToken ct) =>
            Task.FromResult(_guilds.GetValueOrDefault(guildId));

        public ValueTask<V113AllianceInviteTarget?> FindGuildLeaderByGuildNameAsync(string guildName, CancellationToken ct) =>
            ValueTask.FromResult(InviteTargets.GetValueOrDefault(guildName));

        public ValueTask<V113AllianceMember?> FindAllianceMemberAsync(int characterId, CancellationToken ct) =>
            ValueTask.FromResult(_members.GetValueOrDefault(characterId));
    }
}
