using Maple.Core.Characters;
using Maple.Core.Guilds;
using Maple.Core.World;

namespace Maple.Application.Guilds;

public enum GuildUpdateKind
{
    Created,
    Joined,
    Left,
    Expelled,
    RankTitlesChanged,
    RankChanged,
    EmblemChanged,
    NoticeChanged,
    MemberOnline,
    MemberOffline,
    CapacityChanged,
    Disbanded,
}

public enum GuildCommandStatus
{
    Success,
    AlreadyInGuild,
    NotInGuild,
    GuildNotFound,
    GuildFull,
    NotLeader,
    NotAuthorized,
    InvalidName,
    NameTaken,
    NotEnoughMeso,
    InvalidMap,
    InvalidInvite,
    InvalidRank,
    TargetNotFound,
    TargetAlreadyInGuild,
    InvalidOperation,
}

public sealed record GuildCommandResult(
    GuildCommandStatus Status,
    GuildState? Guild = null,
    GuildMember? Target = null,
    GuildUpdateKind? UpdateKind = null,
    IReadOnlyList<int>? RecipientCharacterIds = null,
    bool OnlineStatusChanged = false)
{
    public bool Succeeded => Status == GuildCommandStatus.Success;

    public IReadOnlyList<int> Recipients => RecipientCharacterIds ?? Array.Empty<int>();
}

public sealed record GuildInviteResult(
    GuildCommandStatus Status,
    GuildState? Guild = null,
    GuildMember? Invitee = null)
{
    public bool Succeeded => Status == GuildCommandStatus.Success;
}

public interface IGuildRegistry
{
    Task<GuildState?> GetGuildAsync(int guildId, CancellationToken ct = default);

    Task<GuildState?> GetGuildForCharacterAsync(int characterId, CancellationToken ct = default);

    Task<GuildCommandResult> CreateGuildAsync(GuildMember leader, string name, int signature, CancellationToken ct = default);

    Task<GuildCommandResult> AddMemberAsync(int guildId, GuildMember member, CancellationToken ct = default);

    Task<GuildCommandResult> LeaveGuildAsync(int characterId, CancellationToken ct = default);

    Task<GuildCommandResult> ExpelMemberAsync(int initiatorId, int targetId, string targetName, CancellationToken ct = default);

    Task<GuildCommandResult> ChangeRankAsync(int initiatorId, int targetId, byte newRank, CancellationToken ct = default);

    Task<GuildCommandResult> ChangeRankTitlesAsync(int initiatorId, IReadOnlyList<string> titles, CancellationToken ct = default);

    /// <summary>擴充公會人數上限（cm.increaseGuildCapacity 用）。對照 Java <c>MapleGuild.increaseCapacity</c>：
    /// +5、上限 100，找不到公會或已達上限回非 Success（不拋例外）。</summary>
    Task<GuildCommandResult> IncreaseCapacityAsync(int guildId, CancellationToken ct = default);

    /// <summary>解散公會（cm.disbandGuild 用）。對照 Java <c>MapleGuild.disbandGuild</c>：從登記表移除、
    /// 刪除持久化紀錄；回傳的 <see cref="GuildCommandResult.Guild"/> 是刪除前的快照（含完整成員名單，
    /// 供呼叫端做成員狀態重置/同盟移除/廣播），<see cref="GuildCommandResult.RecipientCharacterIds"/>
    /// 是刪除前的在線成員清單。找不到公會回 <see cref="GuildCommandStatus.GuildNotFound"/>。</summary>
    Task<GuildCommandResult> DisbandGuildAsync(int guildId, CancellationToken ct = default);

    Task<GuildCommandResult> ChangeEmblemAsync(int initiatorId, GuildEmblem emblem, CancellationToken ct = default);

    Task<GuildCommandResult> ChangeNoticeAsync(int initiatorId, string notice, CancellationToken ct = default);

    Task<GuildCommandResult> SetMemberOnlineAsync(GuildMember member, bool online, int channel, CancellationToken ct = default);

    Task<GuildInviteResult> InviteMemberAsync(int inviterId, GuildMember invitee, CancellationToken ct = default);

    Task<bool> ConsumeInviteAsync(int guildId, string characterName, CancellationToken ct = default);

    /// <summary>
    /// 內部資料一致性維護用：把公會的同盟歸屬寫回（<c>Maple.Application.Alliances.AllianceService</c>
    /// 是唯一呼叫端）。找不到公會回 false；這不是玩家可見指令，不走 <see cref="GuildCommandResult"/>/
    /// recipients 廣播。
    /// </summary>
    Task<bool> SetAllianceIdAsync(int guildId, int allianceId, CancellationToken ct = default);
}

public sealed class InMemoryGuildRegistry : IGuildRegistry
{
    private readonly IGuildRepository _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, Guild> _guilds = new();
    private readonly Dictionary<int, int> _guildByCharacter = new();
    private readonly Dictionary<GuildInviteKey, DateTimeOffset> _invites = new();
    private bool _loaded;
    private int _nextGuildId;

    public InMemoryGuildRegistry(IGuildRepository repository, int firstGuildId = 1)
    {
        if (firstGuildId <= 0) throw new ArgumentOutOfRangeException(nameof(firstGuildId));

        _repository = repository;
        _nextGuildId = firstGuildId;
    }

    public async Task<GuildState?> GetGuildAsync(int guildId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _guilds.TryGetValue(guildId, out var guild) ? guild.Snapshot() : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildState?> GetGuildForCharacterAsync(int characterId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return GetGuildForCharacterLocked(characterId)?.Snapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> CreateGuildAsync(GuildMember leader, string name, int signature, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(leader);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_guildByCharacter.ContainsKey(leader.CharacterId))
            {
                return new GuildCommandResult(GuildCommandStatus.AlreadyInGuild, GetGuildForCharacterLocked(leader.CharacterId)?.Snapshot());
            }

            if (_guilds.Values.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return new GuildCommandResult(GuildCommandStatus.NameTaken);
            }

            var guild = new Guild
            {
                Id = _nextGuildId++,
                Name = name,
                LeaderId = leader.CharacterId,
                Signature = signature,
                GuildPoints = GuildService.InitialGuildPoints,
            };

            leader.GuildId = guild.Id;
            leader.GuildRank = Guild.LeaderRank;
            leader.AllianceRank = Guild.DefaultAllianceRank;
            guild.TryAddMember(leader);

            _guilds.Add(guild.Id, guild);
            _guildByCharacter.Add(leader.CharacterId, guild.Id);
            await _repository.AddAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                leader.Clone(),
                GuildUpdateKind.Created,
                OnlineRecipientIds(guild));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> AddMemberAsync(int guildId, GuildMember member, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(member);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_guildByCharacter.ContainsKey(member.CharacterId))
            {
                return new GuildCommandResult(GuildCommandStatus.AlreadyInGuild, GetGuildForCharacterLocked(member.CharacterId)?.Snapshot());
            }

            if (!_guilds.TryGetValue(guildId, out var guild))
            {
                return new GuildCommandResult(GuildCommandStatus.GuildNotFound);
            }

            if (guild.IsFull)
            {
                return new GuildCommandResult(GuildCommandStatus.GuildFull, guild.Snapshot());
            }

            member.GuildId = guild.Id;
            member.GuildRank = Guild.DefaultMemberRank;
            member.AllianceRank = Guild.DefaultAllianceRank;
            if (!guild.TryAddMember(member))
            {
                return new GuildCommandResult(GuildCommandStatus.InvalidOperation, guild.Snapshot(), member.Clone());
            }

            _guildByCharacter.Add(member.CharacterId, guild.Id);
            guild.GainGuildPoints(50);
            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                member.Clone(),
                GuildUpdateKind.Joined,
                OnlineRecipientIds(guild));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> LeaveGuildAsync(int characterId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var guild = GetGuildForCharacterLocked(characterId);
            if (guild is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild);
            }

            var target = guild.GetMember(characterId)?.Clone();
            if (target is null || !guild.TryRemoveMember(characterId, out _))
            {
                return new GuildCommandResult(GuildCommandStatus.TargetNotFound, guild.Snapshot());
            }

            var recipients = OnlineRecipientIds(guild, target);
            _guildByCharacter.Remove(characterId);
            guild.GainGuildPoints(-50);
            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                target,
                GuildUpdateKind.Left,
                recipients);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> ExpelMemberAsync(int initiatorId, int targetId, string targetName, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var guild = GetGuildForCharacterLocked(initiatorId);
            if (guild is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild);
            }

            var initiator = guild.GetMember(initiatorId);
            if (initiator is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild, guild.Snapshot());
            }

            if (initiator.GuildRank > Guild.JuniorMasterRank)
            {
                return new GuildCommandResult(GuildCommandStatus.NotAuthorized, guild.Snapshot());
            }

            var target = guild.GetMember(targetId);
            if (target is null || !string.Equals(target.Name, targetName, StringComparison.OrdinalIgnoreCase))
            {
                return new GuildCommandResult(GuildCommandStatus.TargetNotFound, guild.Snapshot());
            }

            if (initiator.GuildRank >= target.GuildRank)
            {
                return new GuildCommandResult(GuildCommandStatus.NotAuthorized, guild.Snapshot(), target.Clone());
            }

            var removed = target.Clone();
            var recipients = OnlineRecipientIds(guild);
            guild.TryRemoveMember(targetId, out _);
            _guildByCharacter.Remove(targetId);
            guild.GainGuildPoints(-50);
            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                removed,
                GuildUpdateKind.Expelled,
                recipients);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> ChangeRankAsync(int initiatorId, int targetId, byte newRank, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var guild = GetGuildForCharacterLocked(initiatorId);
            if (guild is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild);
            }

            var initiator = guild.GetMember(initiatorId);
            if (initiator is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild, guild.Snapshot());
            }

            if (!CanChangeRank(initiator, newRank))
            {
                return new GuildCommandResult(GuildCommandStatus.InvalidRank, guild.Snapshot());
            }

            if (!guild.TryChangeRank(targetId, newRank, out var changed) || changed is null)
            {
                return new GuildCommandResult(GuildCommandStatus.TargetNotFound, guild.Snapshot());
            }

            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                changed,
                GuildUpdateKind.RankChanged,
                OnlineRecipientIds(guild));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> ChangeRankTitlesAsync(int initiatorId, IReadOnlyList<string> titles, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var guild = GetGuildForCharacterLocked(initiatorId);
            if (guild is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild);
            }

            var initiator = guild.GetMember(initiatorId);
            if (initiator?.GuildRank != Guild.LeaderRank)
            {
                return new GuildCommandResult(GuildCommandStatus.NotLeader, guild.Snapshot());
            }

            if (!guild.TryChangeRankTitles(titles))
            {
                return new GuildCommandResult(GuildCommandStatus.InvalidOperation, guild.Snapshot());
            }

            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                initiator.Clone(),
                GuildUpdateKind.RankTitlesChanged,
                OnlineRecipientIds(guild));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> IncreaseCapacityAsync(int guildId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_guilds.TryGetValue(guildId, out var guild))
            {
                return new GuildCommandResult(GuildCommandStatus.GuildNotFound);
            }

            if (!guild.TryIncreaseCapacity())
            {
                return new GuildCommandResult(GuildCommandStatus.InvalidOperation, guild.Snapshot());
            }

            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                UpdateKind: GuildUpdateKind.CapacityChanged,
                RecipientCharacterIds: OnlineRecipientIds(guild));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> DisbandGuildAsync(int guildId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_guilds.TryGetValue(guildId, out var guild))
            {
                return new GuildCommandResult(GuildCommandStatus.GuildNotFound);
            }

            var snapshot = guild.Snapshot();
            var recipients = OnlineRecipientIds(guild);

            foreach (var member in guild.Members)
            {
                _guildByCharacter.Remove(member.CharacterId);
            }

            _guilds.Remove(guildId);
            await _repository.DeleteAsync(guildId, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                snapshot,
                UpdateKind: GuildUpdateKind.Disbanded,
                RecipientCharacterIds: recipients);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> ChangeEmblemAsync(int initiatorId, GuildEmblem emblem, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var guild = GetGuildForCharacterLocked(initiatorId);
            if (guild is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild);
            }

            var initiator = guild.GetMember(initiatorId);
            if (initiator?.GuildRank != Guild.LeaderRank)
            {
                return new GuildCommandResult(GuildCommandStatus.NotLeader, guild.Snapshot());
            }

            guild.SetEmblem(emblem);
            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                initiator.Clone(),
                GuildUpdateKind.EmblemChanged,
                OnlineRecipientIds(guild));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> ChangeNoticeAsync(int initiatorId, string notice, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var guild = GetGuildForCharacterLocked(initiatorId);
            if (guild is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild);
            }

            var initiator = guild.GetMember(initiatorId);
            if (initiator is null || initiator.GuildRank > Guild.JuniorMasterRank)
            {
                return new GuildCommandResult(GuildCommandStatus.NotAuthorized, guild.Snapshot());
            }

            guild.SetNotice(notice);
            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                initiator.Clone(),
                GuildUpdateKind.NoticeChanged,
                OnlineRecipientIds(guild));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> SetAllianceIdAsync(int guildId, int allianceId, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_guilds.TryGetValue(guildId, out var guild))
            {
                return false;
            }

            guild.AllianceId = allianceId;
            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildCommandResult> SetMemberOnlineAsync(GuildMember member, bool online, int channel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(member);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_guilds.TryGetValue(member.GuildId, out var guild))
            {
                return new GuildCommandResult(GuildCommandStatus.GuildNotFound);
            }

            var existing = guild.GetMember(member.CharacterId);
            if (existing is null)
            {
                return new GuildCommandResult(GuildCommandStatus.NotInGuild, guild.Snapshot());
            }

            var changed = existing.IsOnline != online;
            existing.Level = member.Level;
            existing.JobId = member.JobId;
            existing.Name = member.Name;
            existing.GuildRank = member.GuildRank;
            existing.AllianceRank = member.AllianceRank;
            existing.Channel = online && channel > 0 ? (byte)Math.Min(channel, byte.MaxValue - 1) : byte.MaxValue;
            existing.IsOnline = online;

            await _repository.UpdateAsync(guild, ct).ConfigureAwait(false);

            return new GuildCommandResult(
                GuildCommandStatus.Success,
                guild.Snapshot(),
                existing.Clone(),
                online ? GuildUpdateKind.MemberOnline : GuildUpdateKind.MemberOffline,
                changed ? OnlineRecipientIds(guild, excludeCharacterId: member.CharacterId) : Array.Empty<int>(),
                OnlineStatusChanged: changed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GuildInviteResult> InviteMemberAsync(int inviterId, GuildMember invitee, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invitee);

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var guild = GetGuildForCharacterLocked(inviterId);
            if (guild is null)
            {
                return new GuildInviteResult(GuildCommandStatus.NotInGuild);
            }

            var inviter = guild.GetMember(inviterId);
            if (inviter is null || inviter.GuildRank > Guild.JuniorMasterRank)
            {
                return new GuildInviteResult(GuildCommandStatus.NotAuthorized, guild.Snapshot(), invitee.Clone());
            }

            if (invitee.GuildId > 0 || _guildByCharacter.ContainsKey(invitee.CharacterId))
            {
                return new GuildInviteResult(GuildCommandStatus.TargetAlreadyInGuild, guild.Snapshot(), invitee.Clone());
            }

            if (guild.IsFull)
            {
                return new GuildInviteResult(GuildCommandStatus.GuildFull, guild.Snapshot(), invitee.Clone());
            }

            PruneInvitesLocked(DateTimeOffset.UtcNow);
            _invites[new GuildInviteKey(guild.Id, invitee.Name)] = DateTimeOffset.UtcNow.AddHours(1);
            return new GuildInviteResult(GuildCommandStatus.Success, guild.Snapshot(), invitee.Clone());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ConsumeInviteAsync(int guildId, string characterName, CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            PruneInvitesLocked(now);
            var key = new GuildInviteKey(guildId, characterName);
            if (!_invites.TryGetValue(key, out var expiresAt) || expiresAt <= now)
            {
                _invites.Remove(key);
                return false;
            }

            _invites.Remove(key);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            var guilds = await _repository.GetAllAsync(ct).ConfigureAwait(false);
            foreach (var guild in guilds.Where(static g => g.Id > 0))
            {
                _guilds[guild.Id] = guild;
                foreach (var member in guild.Members)
                {
                    member.GuildId = guild.Id;
                    _guildByCharacter[member.CharacterId] = guild.Id;
                }
            }

            if (_guilds.Count > 0)
            {
                _nextGuildId = Math.Max(_nextGuildId, _guilds.Keys.Max() + 1);
            }

            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Guild? GetGuildForCharacterLocked(int characterId)
    {
        return _guildByCharacter.TryGetValue(characterId, out var guildId) && _guilds.TryGetValue(guildId, out var guild)
            ? guild
            : null;
    }

    private static bool CanChangeRank(GuildMember initiator, byte newRank)
    {
        if (newRank <= Guild.LeaderRank || newRank > Guild.DefaultMemberRank || initiator.GuildRank > Guild.JuniorMasterRank)
        {
            return false;
        }

        return newRank > Guild.JuniorMasterRank || initiator.GuildRank == Guild.LeaderRank;
    }

    private static IReadOnlyList<int> OnlineRecipientIds(Guild guild, GuildMember? extra = null, int? excludeCharacterId = null)
    {
        var ids = guild.Members
            .Where(m => m.IsOnline && m.CharacterId != excludeCharacterId)
            .Select(static m => m.CharacterId)
            .ToList();

        if (extra is { IsOnline: true } && extra.CharacterId != excludeCharacterId && !ids.Contains(extra.CharacterId))
        {
            ids.Add(extra.CharacterId);
        }

        return ids;
    }

    private void PruneInvitesLocked(DateTimeOffset now)
    {
        var expired = _invites
            .Where(i => i.Value <= now)
            .Select(static i => i.Key)
            .ToArray();

        foreach (var key in expired)
        {
            _invites.Remove(key);
        }
    }

    private readonly record struct GuildInviteKey
    {
        public int GuildId { get; }

        public string CharacterName { get; }

        public GuildInviteKey(int guildId, string characterName)
        {
            GuildId = guildId;
            CharacterName = characterName.ToLowerInvariant();
        }
    }
}

public sealed class GuildService
{
    public const int CreationMapId = 200000301;
    public const int CreationCost = 1_500_000;
    public const int EmblemCost = 1_000_000;
    public const int InitialGuildPoints = 500;
    public const int EffectiveMaximumNameLength = 12;
    public const int HandlerMaximumNameLength = 15;
    public const int MinimumNameLength = 3;

    private readonly IGuildRegistry _registry;
    private readonly ICharacterRepository _characters;

    public GuildService(IGuildRegistry registry, ICharacterRepository characters)
    {
        _registry = registry;
        _characters = characters;
    }

    public Task<GuildState?> GetGuildAsync(int guildId, CancellationToken ct = default) =>
        _registry.GetGuildAsync(guildId, ct);

    public Task<GuildState?> GetGuildForCharacterAsync(int characterId, CancellationToken ct = default) =>
        _registry.GetGuildForCharacterAsync(characterId, ct);

    public async Task<GuildCommandResult> CreateGuildAsync(Player player, string name, int channel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Character.GuildId > 0)
        {
            return new GuildCommandResult(GuildCommandStatus.AlreadyInGuild);
        }

        if (player.Character.MapId != CreationMapId)
        {
            return new GuildCommandResult(GuildCommandStatus.InvalidMap);
        }

        if (player.Character.Meso < CreationCost)
        {
            return new GuildCommandResult(GuildCommandStatus.NotEnoughMeso);
        }

        if (!IsGuildNameAcceptable(name))
        {
            return new GuildCommandResult(GuildCommandStatus.InvalidName);
        }

        var member = GuildMember.FromCharacter(player.Character, channel, rank: Guild.LeaderRank);
        var signature = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = await _registry.CreateGuildAsync(member, name, signature, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.Guild is null)
        {
            return result;
        }

        player.GainMeso(-CreationCost);
        player.JoinGuild(result.Guild.Id, Guild.LeaderRank);
        await _characters.UpdateAsync(player.Character, ct).ConfigureAwait(false);
        return result;
    }

    public Task<GuildInviteResult> InviteMemberAsync(int inviterId, GuildMember invitee, CancellationToken ct = default) =>
        _registry.InviteMemberAsync(inviterId, invitee, ct);

    public async Task<GuildCommandResult> AcceptInviteAsync(Player player, int guildId, int channel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Character.GuildId > 0)
        {
            return new GuildCommandResult(GuildCommandStatus.AlreadyInGuild);
        }

        if (!await _registry.ConsumeInviteAsync(guildId, player.Character.Name, ct).ConfigureAwait(false))
        {
            return new GuildCommandResult(GuildCommandStatus.InvalidInvite);
        }

        var member = GuildMember.FromCharacter(
            player.Character,
            channel,
            rank: Guild.DefaultMemberRank,
            guildId: guildId);
        var result = await _registry.AddMemberAsync(guildId, member, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.Guild is null)
        {
            return result;
        }

        player.JoinGuild(result.Guild.Id, Guild.DefaultMemberRank);
        await _characters.UpdateAsync(player.Character, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<GuildCommandResult> LeaveGuildAsync(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var result = await _registry.LeaveGuildAsync(player.Character.Id, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return result;
        }

        player.LeaveGuild();
        await _characters.UpdateAsync(player.Character, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<GuildCommandResult> ExpelMemberAsync(Player initiator, int targetId, string targetName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initiator);

        var result = await _registry.ExpelMemberAsync(initiator.Character.Id, targetId, targetName, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.Target is null)
        {
            return result;
        }

        await ClearCharacterGuildStatusAsync(result.Target.CharacterId, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<GuildCommandResult> ChangeRankAsync(Player initiator, int targetId, byte newRank, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initiator);

        var result = await _registry.ChangeRankAsync(initiator.Character.Id, targetId, newRank, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.Target is null)
        {
            return result;
        }

        await UpdateCharacterGuildRankAsync(result.Target.CharacterId, newRank, ct).ConfigureAwait(false);
        return result;
    }

    public Task<GuildCommandResult> ChangeRankTitlesAsync(Player initiator, IReadOnlyList<string> titles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initiator);
        return _registry.ChangeRankTitlesAsync(initiator.Character.Id, titles, ct);
    }

    public const int IncreaseCapacityCost = 250_000;

    /// <summary>
    /// 對照 Java <c>NPCConversationManager.increaseGuildCapacity</c>：呼叫端（<c>NpcContext</c>）已先
    /// 檢查 meso 足夠且 gid&gt;0 才會走到這裡。楓幣**無條件扣**（Java 對 <c>World.Guild.increaseGuildCapacity</c>
    /// 的回傳值不做檢查，即使公會已滿 100 人上限、容量沒真的增加，錢一樣扣）——刻意保留這個 Java 行為，
    /// 不「修得比原版更合理」。
    /// </summary>
    public async Task<GuildCommandResult> IncreaseGuildCapacityAsync(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Character.GuildId <= 0)
        {
            return new GuildCommandResult(GuildCommandStatus.NotInGuild);
        }

        var result = await _registry.IncreaseCapacityAsync(player.Character.GuildId, ct).ConfigureAwait(false);

        player.GainMeso(-IncreaseCapacityCost);
        await _characters.UpdateAsync(player.Character, ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// 對照 Java <c>NPCConversationManager.disbandGuild</c>：非會長或無公會靜默返回
    /// （<see cref="GuildCommandStatus.NotLeader"/>，呼叫端不應對此送任何封包）。成功後對照
    /// <c>MapleGuild.disbandGuild</c>/<c>writeToDB(true)</c> 重置**所有**成員（不分上下線）的持久化
    /// 公會欄位（guildId=0/guildRank=<see cref="Guild.DefaultMemberRank"/>/allianceRank=
    /// <see cref="Guild.DefaultAllianceRank"/>）。BBS 清理、同盟移除、在線玩家記憶體同步與廣播
    /// 屬於跨服務協調（BBS repository + AllianceService + IOnlinePlayerRegistry），留給呼叫端
    /// （Adapter 層，已同時持有這幾個依賴）用回傳的 <see cref="GuildCommandResult.Guild"/> 快照完成。
    /// </summary>
    public async Task<GuildCommandResult> DisbandGuildAsync(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Character.GuildId <= 0 || player.Character.GuildRank != Guild.LeaderRank)
        {
            return new GuildCommandResult(GuildCommandStatus.NotLeader);
        }

        var result = await _registry.DisbandGuildAsync(player.Character.GuildId, ct).ConfigureAwait(false);
        if (!result.Succeeded || result.Guild is null)
        {
            return result;
        }

        foreach (var member in result.Guild.Members)
        {
            await ClearCharacterGuildStatusAsync(member.CharacterId, ct).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<GuildCommandResult> ChangeEmblemAsync(Player initiator, GuildEmblem emblem, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initiator);

        if (initiator.Character.MapId != CreationMapId)
        {
            return new GuildCommandResult(GuildCommandStatus.InvalidMap);
        }

        if (initiator.Character.Meso < EmblemCost)
        {
            return new GuildCommandResult(GuildCommandStatus.NotEnoughMeso);
        }

        var result = await _registry.ChangeEmblemAsync(initiator.Character.Id, emblem, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return result;
        }

        initiator.GainMeso(-EmblemCost);
        await _characters.UpdateAsync(initiator.Character, ct).ConfigureAwait(false);
        return result;
    }

    public Task<GuildCommandResult> ChangeNoticeAsync(Player initiator, string notice, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initiator);

        return notice.Length > 100
            ? Task.FromResult(new GuildCommandResult(GuildCommandStatus.InvalidOperation))
            : _registry.ChangeNoticeAsync(initiator.Character.Id, notice, ct);
    }

    public Task<GuildCommandResult> SetMemberOnlineAsync(Player player, bool online, int channel, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Character.GuildId <= 0)
        {
            return Task.FromResult(new GuildCommandResult(GuildCommandStatus.NotInGuild));
        }

        var member = GuildMember.FromCharacter(player.Character, channel, online, guildId: player.Character.GuildId);
        return _registry.SetMemberOnlineAsync(member, online, channel, ct);
    }

    public async Task ClearCharacterGuildStatusAsync(int characterId, CancellationToken ct = default)
    {
        var character = await _characters.FindByIdAsync(characterId, ct).ConfigureAwait(false);
        if (character is null)
        {
            return;
        }

        character.GuildId = 0;
        character.GuildRank = Guild.DefaultMemberRank;
        character.AllianceRank = Guild.DefaultAllianceRank;
        await _characters.UpdateAsync(character, ct).ConfigureAwait(false);
    }

    private async Task UpdateCharacterGuildRankAsync(int characterId, byte newRank, CancellationToken ct)
    {
        var character = await _characters.FindByIdAsync(characterId, ct).ConfigureAwait(false);
        if (character is null)
        {
            return;
        }

        character.GuildRank = newRank;
        await _characters.UpdateAsync(character, ct).ConfigureAwait(false);
    }

    private static bool IsGuildNameAcceptable(string name) =>
        name.Length >= MinimumNameLength && name.Length <= EffectiveMaximumNameLength;
}
