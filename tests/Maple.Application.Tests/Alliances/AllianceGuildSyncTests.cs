using Maple.Application.Alliances;
using Maple.Application.Guilds;
using Maple.Core.Alliances;
using Maple.Core.Guilds;

namespace Maple.Application.Tests.Alliances;

/// <summary>
/// 對照任務歷程 2026-09-06_10/_11：GuildState.AllianceId 過去從未真正寫回 IGuildRegistry，
/// 只在少數封包建構情境被臨時投影。這裡驗證 AllianceService 的四個成員異動時機點
/// （建立/加入/一般移除/解散）都會把結果同步寫回。
/// </summary>
public sealed class AllianceGuildSyncTests
{
    [Fact]
    public async Task CreateAllianceAsync_WritesAllianceIdToBothFoundingGuilds()
    {
        var (alliances, guilds) = CreateHarness();
        var guildA = await CreateGuildAsync(guilds, "GuildA", leaderId: 1);
        var guildB = await CreateGuildAsync(guilds, "GuildB", leaderId: 2);

        var result = await alliances.CreateAllianceAsync("United", leaderCharacterId: 1, guildA.Id, guildB.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(result.Alliance!.Id, (await guilds.GetGuildAsync(guildA.Id))!.AllianceId);
        Assert.Equal(result.Alliance.Id, (await guilds.GetGuildAsync(guildB.Id))!.AllianceId);
    }

    [Fact]
    public async Task AcceptInviteAsync_WritesAllianceIdToJoiningGuild()
    {
        var (alliances, guilds) = CreateHarness();
        var guildA = await CreateGuildAsync(guilds, "GuildA", leaderId: 1);
        var guildB = await CreateGuildAsync(guilds, "GuildB", leaderId: 2);
        var guildC = await CreateGuildAsync(guilds, "GuildC", leaderId: 3);
        var created = await alliances.CreateAllianceAsync("United", leaderCharacterId: 1, guildA.Id, guildB.Id, capacity: 3);

        var invited = await alliances.InviteGuildAsync(created.Alliance!.Id, guildC.Id);
        Assert.True(invited.Succeeded);
        var accepted = await alliances.AcceptInviteAsync(guildC.Id);

        Assert.True(accepted.Succeeded);
        Assert.Equal(created.Alliance.Id, (await guilds.GetGuildAsync(guildC.Id))!.AllianceId);
    }

    [Fact]
    public async Task RemoveGuildAsync_NonFoundingGuild_ClearsOnlyThatGuildsAllianceId()
    {
        var (alliances, guilds) = CreateHarness();
        var guildA = await CreateGuildAsync(guilds, "GuildA", leaderId: 1);
        var guildB = await CreateGuildAsync(guilds, "GuildB", leaderId: 2);
        var guildC = await CreateGuildAsync(guilds, "GuildC", leaderId: 3);
        var created = await alliances.CreateAllianceAsync("United", leaderCharacterId: 1, guildA.Id, guildB.Id, capacity: 3);
        await alliances.InviteGuildAsync(created.Alliance!.Id, guildC.Id);
        await alliances.AcceptInviteAsync(guildC.Id);

        var removed = await alliances.RemoveGuildAsync(created.Alliance.Id, guildC.Id, expelled: false);

        Assert.True(removed.Succeeded);
        Assert.Equal(0, (await guilds.GetGuildAsync(guildC.Id))!.AllianceId);
        Assert.Equal(created.Alliance.Id, (await guilds.GetGuildAsync(guildA.Id))!.AllianceId);
        Assert.Equal(created.Alliance.Id, (await guilds.GetGuildAsync(guildB.Id))!.AllianceId);
    }

    [Fact]
    public async Task RemoveGuildAsync_FoundingGuild_DisbandsAndClearsAllGuildsAllianceId()
    {
        var (alliances, guilds) = CreateHarness();
        var guildA = await CreateGuildAsync(guilds, "GuildA", leaderId: 1);
        var guildB = await CreateGuildAsync(guilds, "GuildB", leaderId: 2);
        var created = await alliances.CreateAllianceAsync("United", leaderCharacterId: 1, guildA.Id, guildB.Id);

        // guildA is index 0 (the founding/leader guild) — removing it disbands the whole alliance.
        var removed = await alliances.RemoveGuildAsync(created.Alliance!.Id, guildA.Id, expelled: false);

        Assert.True(removed.Succeeded);
        Assert.Equal(AllianceUpdateKind.Disbanded, removed.UpdateKind);
        Assert.Equal(0, (await guilds.GetGuildAsync(guildA.Id))!.AllianceId);
        Assert.Equal(0, (await guilds.GetGuildAsync(guildB.Id))!.AllianceId);
    }

    private static (AllianceService Alliances, IGuildRegistry Guilds) CreateHarness()
    {
        var guilds = new InMemoryGuildRegistry(new FakeGuildRepository());
        var alliances = new AllianceService(new FakeAllianceRepository(), guilds);
        return (alliances, guilds);
    }

    private static async Task<GuildState> CreateGuildAsync(IGuildRegistry guilds, string name, int leaderId)
    {
        var leader = new GuildMember { CharacterId = leaderId, Name = $"Leader{leaderId}", GuildRank = Guild.LeaderRank };
        var result = await guilds.CreateGuildAsync(leader, name, signature: leaderId);
        return result.Guild!;
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
