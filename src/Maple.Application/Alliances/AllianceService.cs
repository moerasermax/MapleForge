using Maple.Application.Guilds;
using Maple.Core.Alliances;

namespace Maple.Application.Alliances;

public enum AllianceCommandStatus
{
    Success,
    AllianceNotFound,
    AlreadyInAlliance,
    AllianceFull,
    InvalidName,
    NameTaken,
    InvalidGuild,
    InvalidInvite,
    InvalidRank,
    InvalidOperation,
    NoticeTooLong,
}

public enum AllianceUpdateKind
{
    Created,
    InviteCreated,
    InviteDenied,
    GuildAdded,
    GuildRemoved,
    Disbanded,
    LeaderChanged,
    RanksChanged,
    NoticeChanged,
    RankChanged,
}

public sealed record AllianceCommandResult(
    AllianceCommandStatus Status,
    AllianceState? Alliance = null,
    AllianceUpdateKind? UpdateKind = null,
    int? GuildId = null,
    int? CharacterId = null,
    byte? AllianceRank = null,
    int? PreviousLeaderId = null,
    int? InviterCharacterId = null,
    IReadOnlyList<int>? AffectedGuildIds = null)
{
    public bool Succeeded => Status == AllianceCommandStatus.Success;

    public IReadOnlyList<int> AffectedGuilds => AffectedGuildIds ?? Array.Empty<int>();
}

public sealed class AllianceService
{
    public const int MaximumNameLength = 12;
    public const int MaximumNoticeLength = 100;

    private readonly IAllianceRepository _repository;
    private readonly IGuildRegistry _guilds;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, Alliance> _alliances = new();
    private readonly Dictionary<int, int> _allianceByGuild = new();
    private readonly Dictionary<int, AllianceInvitation> _invitesByGuild = new();
    private int _nextAllianceId;

    public AllianceService(IAllianceRepository repository, IGuildRegistry guilds, int firstAllianceId = 1)
    {
        if (firstAllianceId <= 0) throw new ArgumentOutOfRangeException(nameof(firstAllianceId));

        _repository = repository;
        _guilds = guilds;
        _nextAllianceId = firstAllianceId;
    }

    /// <summary>
    /// 對照本身的 <c>_allianceByGuild</c> 權威狀態，把公會的同盟歸屬同步寫回
    /// <see cref="IGuildRegistry"/>（見任務歷程 2026-09-06_10/_11：<c>GuildState.AllianceId</c>
    /// 過去從未真正持久化，只在少數封包建構情境被臨時投影）。
    /// </summary>
    private Task SyncGuildAllianceIdAsync(int guildId, int allianceId, CancellationToken ct) =>
        _guilds.SetAllianceIdAsync(guildId, allianceId, ct);

    public async Task<AllianceState?> GetAllianceInfoAsync(int allianceId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var alliance = await GetAllianceLockedAsync(allianceId, ct).ConfigureAwait(false);
            return alliance?.Snapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> GetAllianceIdForGuildAsync(int guildId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _allianceByGuild.GetValueOrDefault(guildId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> CreateAllianceAsync(
        string name,
        int leaderCharacterId,
        int leaderGuildId,
        int partnerGuildId,
        int capacity = Alliance.InitialCapacity,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsAllianceNameAcceptable(name))
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidName);
            }

            if (leaderCharacterId <= 0 || leaderGuildId <= 0 || partnerGuildId <= 0 || leaderGuildId == partnerGuildId)
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidGuild);
            }

            if (capacity < Alliance.InitialCapacity || capacity > Alliance.MaximumGuilds)
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidOperation);
            }

            if (_allianceByGuild.ContainsKey(leaderGuildId) || _allianceByGuild.ContainsKey(partnerGuildId))
            {
                return new AllianceCommandResult(AllianceCommandStatus.AlreadyInAlliance);
            }

            if (_alliances.Values.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return new AllianceCommandResult(AllianceCommandStatus.NameTaken);
            }

            var alliance = new Alliance
            {
                Id = await AllocateAllianceIdLockedAsync(ct).ConfigureAwait(false),
                Name = name,
                LeaderId = leaderCharacterId,
                Capacity = capacity,
                GuildIds = [leaderGuildId, partnerGuildId],
            };

            await _repository.SaveAsync(alliance, ct).ConfigureAwait(false);
            TrackAllianceLocked(alliance);
            await SyncGuildAllianceIdAsync(leaderGuildId, alliance.Id, ct).ConfigureAwait(false);
            await SyncGuildAllianceIdAsync(partnerGuildId, alliance.Id, ct).ConfigureAwait(false);
            var state = alliance.Snapshot();
            return new AllianceCommandResult(
                AllianceCommandStatus.Success,
                state,
                AllianceUpdateKind.Created,
                GuildId: leaderGuildId,
                CharacterId: leaderCharacterId,
                AffectedGuildIds: state.GuildIds);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> InviteGuildAsync(
        int allianceId,
        int targetGuildId,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var alliance = await GetAllianceLockedAsync(allianceId, ct).ConfigureAwait(false);
            if (alliance is null)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceNotFound);
            }

            if (targetGuildId <= 0)
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidGuild, alliance.Snapshot());
            }

            if (_allianceByGuild.ContainsKey(targetGuildId))
            {
                return new AllianceCommandResult(AllianceCommandStatus.AlreadyInAlliance, alliance.Snapshot(), GuildId: targetGuildId);
            }

            if (!alliance.CanInvite)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceFull, alliance.Snapshot(), GuildId: targetGuildId);
            }

            _invitesByGuild[targetGuildId] = new AllianceInvitation(alliance.Id, alliance.LeaderId);
            return new AllianceCommandResult(
                AllianceCommandStatus.Success,
                alliance.Snapshot(),
                AllianceUpdateKind.InviteCreated,
                GuildId: targetGuildId,
                InviterCharacterId: alliance.LeaderId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> AcceptInviteAsync(int guildId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_invitesByGuild.Remove(guildId, out var invite))
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidInvite, GuildId: guildId);
            }

            var alliance = await GetAllianceLockedAsync(invite.AllianceId, ct).ConfigureAwait(false);
            if (alliance is null)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceNotFound, GuildId: guildId);
            }

            return await AddGuildLockedAsync(alliance, guildId, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> DenyInviteAsync(int guildId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_invitesByGuild.Remove(guildId, out var invite))
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidInvite, GuildId: guildId);
            }

            var alliance = await GetAllianceLockedAsync(invite.AllianceId, ct).ConfigureAwait(false);
            return new AllianceCommandResult(
                AllianceCommandStatus.Success,
                alliance?.Snapshot(),
                AllianceUpdateKind.InviteDenied,
                GuildId: guildId,
                InviterCharacterId: invite.LeaderCharacterId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> AddGuildAsync(int allianceId, int guildId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var alliance = await GetAllianceLockedAsync(allianceId, ct).ConfigureAwait(false);
            if (alliance is null)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceNotFound, GuildId: guildId);
            }

            return await AddGuildLockedAsync(alliance, guildId, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> RemoveGuildAsync(
        int allianceId,
        int guildId,
        bool expelled,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var alliance = await GetAllianceLockedAsync(allianceId, ct).ConfigureAwait(false);
            if (alliance is null)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceNotFound, GuildId: guildId);
            }

            var affectedGuilds = alliance.Snapshot().GuildIds;
            if (!alliance.TryRemoveGuild(guildId, out var removedLeaderGuild))
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidGuild, alliance.Snapshot(), GuildId: guildId);
            }

            if (removedLeaderGuild)
            {
                await _repository.DeleteAsync(alliance.Id, ct).ConfigureAwait(false);
                UntrackAllianceLocked(alliance.Id);
                foreach (var affectedGuildId in affectedGuilds)
                {
                    await SyncGuildAllianceIdAsync(affectedGuildId, allianceId: 0, ct).ConfigureAwait(false);
                }

                return new AllianceCommandResult(
                    AllianceCommandStatus.Success,
                    null,
                    AllianceUpdateKind.Disbanded,
                    GuildId: guildId,
                    AffectedGuildIds: affectedGuilds);
            }

            await _repository.SaveAsync(alliance, ct).ConfigureAwait(false);
            TrackAllianceLocked(alliance);
            await SyncGuildAllianceIdAsync(guildId, allianceId: 0, ct).ConfigureAwait(false);
            return new AllianceCommandResult(
                AllianceCommandStatus.Success,
                alliance.Snapshot(),
                AllianceUpdateKind.GuildRemoved,
                GuildId: guildId,
                AffectedGuildIds: affectedGuilds);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> ChangeLeaderAsync(
        int allianceId,
        int newLeaderCharacterId,
        int? newLeaderGuildId = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var alliance = await GetAllianceLockedAsync(allianceId, ct).ConfigureAwait(false);
            if (alliance is null)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceNotFound, CharacterId: newLeaderCharacterId);
            }

            var oldLeader = alliance.LeaderId;
            if (!alliance.TryChangeLeader(newLeaderCharacterId, newLeaderGuildId))
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidOperation, alliance.Snapshot(), CharacterId: newLeaderCharacterId);
            }

            await _repository.SaveAsync(alliance, ct).ConfigureAwait(false);
            TrackAllianceLocked(alliance);
            return new AllianceCommandResult(
                AllianceCommandStatus.Success,
                alliance.Snapshot(),
                AllianceUpdateKind.LeaderChanged,
                CharacterId: newLeaderCharacterId,
                PreviousLeaderId: oldLeader,
                AffectedGuildIds: alliance.Snapshot().GuildIds);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> UpdateRanksAsync(
        int allianceId,
        IReadOnlyList<string> ranks,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var alliance = await GetAllianceLockedAsync(allianceId, ct).ConfigureAwait(false);
            if (alliance is null)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceNotFound);
            }

            if (!alliance.TryUpdateRanks(ranks))
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidOperation, alliance.Snapshot());
            }

            await _repository.SaveAsync(alliance, ct).ConfigureAwait(false);
            TrackAllianceLocked(alliance);
            return new AllianceCommandResult(
                AllianceCommandStatus.Success,
                alliance.Snapshot(),
                AllianceUpdateKind.RanksChanged,
                AffectedGuildIds: alliance.Snapshot().GuildIds);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> UpdateNoticeAsync(
        int allianceId,
        string notice,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var alliance = await GetAllianceLockedAsync(allianceId, ct).ConfigureAwait(false);
            if (alliance is null)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceNotFound);
            }

            if (notice.Length > MaximumNoticeLength)
            {
                return new AllianceCommandResult(AllianceCommandStatus.NoticeTooLong, alliance.Snapshot());
            }

            alliance.SetNotice(notice);
            await _repository.SaveAsync(alliance, ct).ConfigureAwait(false);
            TrackAllianceLocked(alliance);
            return new AllianceCommandResult(
                AllianceCommandStatus.Success,
                alliance.Snapshot(),
                AllianceUpdateKind.NoticeChanged,
                AffectedGuildIds: alliance.Snapshot().GuildIds);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AllianceCommandResult> ChangeAllianceRankAsync(
        int allianceId,
        int characterId,
        byte currentRank,
        byte change,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var alliance = await GetAllianceLockedAsync(allianceId, ct).ConfigureAwait(false);
            if (alliance is null)
            {
                return new AllianceCommandResult(AllianceCommandStatus.AllianceNotFound, CharacterId: characterId);
            }

            if (characterId == alliance.LeaderId || currentRank <= Alliance.SubLeaderRank || change > 1)
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidRank, alliance.Snapshot(), CharacterId: characterId);
            }

            if ((change == 0 && currentRank >= Alliance.LowestRank) ||
                (change == 1 && currentRank <= Alliance.SubLeaderRank + 1))
            {
                return new AllianceCommandResult(AllianceCommandStatus.InvalidRank, alliance.Snapshot(), CharacterId: characterId);
            }

            var nextRank = (byte)(currentRank + (change == 0 ? 1 : -1));
            return new AllianceCommandResult(
                AllianceCommandStatus.Success,
                alliance.Snapshot(),
                AllianceUpdateKind.RankChanged,
                CharacterId: characterId,
                AllianceRank: nextRank,
                AffectedGuildIds: alliance.Snapshot().GuildIds);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AllianceCommandResult> AddGuildLockedAsync(
        Alliance alliance,
        int guildId,
        CancellationToken ct)
    {
        if (guildId <= 0)
        {
            return new AllianceCommandResult(AllianceCommandStatus.InvalidGuild, alliance.Snapshot(), GuildId: guildId);
        }

        if (_allianceByGuild.ContainsKey(guildId))
        {
            return new AllianceCommandResult(AllianceCommandStatus.AlreadyInAlliance, alliance.Snapshot(), GuildId: guildId);
        }

        if (!alliance.TryAddGuild(guildId))
        {
            return new AllianceCommandResult(AllianceCommandStatus.AllianceFull, alliance.Snapshot(), GuildId: guildId);
        }

        await _repository.SaveAsync(alliance, ct).ConfigureAwait(false);
        TrackAllianceLocked(alliance);
        await SyncGuildAllianceIdAsync(guildId, alliance.Id, ct).ConfigureAwait(false);
        return new AllianceCommandResult(
            AllianceCommandStatus.Success,
            alliance.Snapshot(),
            AllianceUpdateKind.GuildAdded,
            GuildId: guildId,
            AffectedGuildIds: alliance.Snapshot().GuildIds);
    }

    private async Task<Alliance?> GetAllianceLockedAsync(int allianceId, CancellationToken ct)
    {
        if (allianceId <= 0)
        {
            return null;
        }

        if (_alliances.TryGetValue(allianceId, out var cached))
        {
            return cached;
        }

        var loaded = await _repository.FindByIdAsync(allianceId, ct).ConfigureAwait(false);
        if (loaded is not null)
        {
            TrackAllianceLocked(loaded);
        }

        return loaded;
    }

    private async Task<int> AllocateAllianceIdLockedAsync(CancellationToken ct)
    {
        while (_alliances.ContainsKey(_nextAllianceId) ||
               await _repository.FindByIdAsync(_nextAllianceId, ct).ConfigureAwait(false) is not null)
        {
            _nextAllianceId++;
        }

        return _nextAllianceId++;
    }

    private void TrackAllianceLocked(Alliance alliance)
    {
        UntrackAllianceLocked(alliance.Id);
        _alliances[alliance.Id] = alliance;
        foreach (var guildId in alliance.Snapshot().GuildIds)
        {
            _allianceByGuild[guildId] = alliance.Id;
        }

        if (alliance.Id >= _nextAllianceId)
        {
            _nextAllianceId = alliance.Id + 1;
        }
    }

    private void UntrackAllianceLocked(int allianceId)
    {
        _alliances.Remove(allianceId);
        foreach (var guildId in _allianceByGuild.Where(pair => pair.Value == allianceId).Select(static pair => pair.Key).ToArray())
        {
            _allianceByGuild.Remove(guildId);
        }
    }

    private static bool IsAllianceNameAcceptable(string name) =>
        name.Length > 0 && name.Length <= MaximumNameLength;

    private readonly record struct AllianceInvitation(int AllianceId, int LeaderCharacterId);
}
