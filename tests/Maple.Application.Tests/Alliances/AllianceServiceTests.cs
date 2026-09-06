using Maple.Application.Alliances;
using Maple.Application.Guilds;
using Maple.Core.Alliances;
using Maple.Core.Guilds;

namespace Maple.Application.Tests.Alliances;

/// <summary>
/// P040：比照 P039 對 GuildService 的修法，驗證 AllianceService 的 Alliance.GuildIds 物件欄位
/// 異動在持久層失敗時會回滾，讓呼叫端可以安全重試（而非被 TryAddGuild/TryRemoveGuild 的
/// 「已存在/不存在」判斷卡死）。
/// </summary>
public sealed class AllianceServiceTests
{
    [Fact]
    public async Task AddGuildAsync_RepositorySaveFails_GuildIdsRollback_CanRetry()
    {
        var repository = new FakeAllianceRepository();
        var service = new AllianceService(repository, new NoOpGuildRegistry(), firstAllianceId: 1);
        var created = await service.CreateAllianceAsync("Coalition", leaderCharacterId: 1, leaderGuildId: 100, partnerGuildId: 101, capacity: 3);
        Assert.True(created.Succeeded);
        repository.ThrowOnNextSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddGuildAsync(created.Alliance!.Id, guildId: 102));

        var retry = await service.AddGuildAsync(created.Alliance!.Id, guildId: 102);
        Assert.True(retry.Succeeded);
        Assert.Contains(102, retry.Alliance!.GuildIds);
    }

    [Fact]
    public async Task RemoveGuildAsync_NonLeaderGuild_RepositorySaveFails_GuildIdsRollback_CanRetry()
    {
        var repository = new FakeAllianceRepository();
        var service = new AllianceService(repository, new NoOpGuildRegistry(), firstAllianceId: 1);
        var created = await service.CreateAllianceAsync("Coalition", leaderCharacterId: 1, leaderGuildId: 100, partnerGuildId: 101);
        repository.ThrowOnNextSave = true;

        // partnerGuildId(101) 是 index 1，不是 leader（index 0），走 SaveAsync 分支而非 DeleteAsync。
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RemoveGuildAsync(created.Alliance!.Id, guildId: 101, expelled: false));

        var retry = await service.RemoveGuildAsync(created.Alliance!.Id, guildId: 101, expelled: false);
        Assert.True(retry.Succeeded);
    }

    [Fact]
    public async Task RemoveGuildAsync_LeaderGuild_RepositoryDeleteFails_GuildIdsRollback_CanRetry()
    {
        var repository = new FakeAllianceRepository();
        var service = new AllianceService(repository, new NoOpGuildRegistry(), firstAllianceId: 1);
        var created = await service.CreateAllianceAsync("Coalition", leaderCharacterId: 1, leaderGuildId: 100, partnerGuildId: 101);
        repository.ThrowOnNextDelete = true;

        // leaderGuildId(100) 是 index 0，移除會觸發整個同盟解散（DeleteAsync 分支）。
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RemoveGuildAsync(created.Alliance!.Id, guildId: 100, expelled: false));

        var retry = await service.RemoveGuildAsync(created.Alliance!.Id, guildId: 100, expelled: false);
        Assert.True(retry.Succeeded);
        Assert.Equal(AllianceUpdateKind.Disbanded, retry.UpdateKind);
    }

    private sealed class NoOpGuildRegistry : IGuildRegistry
    {
        public Task<bool> SetAllianceIdAsync(int guildId, int allianceId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<GuildState?> GetGuildAsync(int guildId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildState?> GetGuildForCharacterAsync(int characterId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> CreateGuildAsync(GuildMember leader, string name, int signature, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> AddMemberAsync(int guildId, GuildMember member, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> LeaveGuildAsync(int characterId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> ExpelMemberAsync(int initiatorId, int targetId, string targetName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> ChangeRankAsync(int initiatorId, int targetId, byte newRank, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> ChangeRankTitlesAsync(int initiatorId, IReadOnlyList<string> titles, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> IncreaseCapacityAsync(int guildId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> DisbandGuildAsync(int guildId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> ChangeEmblemAsync(int initiatorId, GuildEmblem emblem, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> ChangeNoticeAsync(int initiatorId, string notice, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildCommandResult> SetMemberOnlineAsync(GuildMember member, bool online, int channel, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GuildInviteResult> InviteMemberAsync(int inviterId, GuildMember invitee, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPendingInviteAsync(int guildId, string characterName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ConsumeInviteAsync(int guildId, string characterName, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAllianceRepository : IAllianceRepository
    {
        private readonly Dictionary<int, Alliance> _alliances = new();

        public bool ThrowOnNextSave { get; set; }
        public bool ThrowOnNextDelete { get; set; }

        public Task<Alliance?> FindByIdAsync(int allianceId, CancellationToken ct = default) =>
            Task.FromResult(_alliances.GetValueOrDefault(allianceId));

        public Task SaveAsync(Alliance alliance, CancellationToken ct = default)
        {
            if (ThrowOnNextSave)
            {
                ThrowOnNextSave = false;
                throw new InvalidOperationException("模擬 DB 儲存失敗");
            }

            _alliances[alliance.Id] = alliance;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(int allianceId, CancellationToken ct = default)
        {
            if (ThrowOnNextDelete)
            {
                ThrowOnNextDelete = false;
                throw new InvalidOperationException("模擬 DB 刪除失敗");
            }

            _alliances.Remove(allianceId);
            return Task.CompletedTask;
        }
    }
}
