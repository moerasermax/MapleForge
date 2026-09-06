using Maple.Core.Characters;
using Maple.Core.Families;
using Maple.Core.World;

namespace Maple.Application.Families;

public enum FamilyCommandStatus
{
    Success,
    FamilyNotFound,
    NotInFamily,
    AlreadyInFamily,
    TargetNotFound,
    TargetAlreadyInFamily,
    NotSameMap,
    TargetHasSenior,
    InvalidLevel,
    LevelGapTooWide,
    TargetTooLowLevel,
    TooManyJuniors,
    InvalidInvite,
    NotLeader,
    InvalidBuff,
    NotEnoughRep,
    NotEnoughOnlinePedigree,
    NotSameFamily,
    InvalidOperation,
}

public enum FamilyUpdateKind
{
    Created,
    InviteCreated,
    InviteDenied,
    Joined,
    JuniorDeleted,
    SeniorDeleted,
    BuffUsed,
    PreceptChanged,
    SummonRequested,
    SummonAccepted,
    SummonDenied,
}

public sealed record FamilyOnlineStatusChange(bool Changed, bool Online, string MemberName, IReadOnlyList<int> NotifyRecipientIds)
{
    public static readonly FamilyOnlineStatusChange None = new(false, false, string.Empty, Array.Empty<int>());
}

public sealed record FamilyWarpTarget(int CharacterId, int DestinationMapId, Position DestinationPosition);

public sealed record FamilyBuffUsage(int BuffType, int TimesUsed);

public sealed record FamilyInfoData(
    int CharacterId,
    int CurrentRep,
    int TotalRep,
    int JuniorCount,
    int LeaderId,
    string LeaderName,
    string Notice,
    IReadOnlyList<FamilyBuffUsage> UsedBuffs);

public sealed record FamilyPedigreeMemberData(
    int CharacterId,
    int SeniorId,
    int Job,
    int Level,
    bool IsOnline,
    int CurrentRep,
    int TotalRep,
    int Channel,
    string Name);

public sealed record FamilyDescendantData(int CharacterId, int DescendantCount);

public sealed record FamilyPedigreeData(
    int CharacterId,
    IReadOnlyList<FamilyPedigreeMemberData> Members,
    long DescendantSlots,
    int Generations,
    int FamilyMemberCount,
    IReadOnlyList<FamilyDescendantData> DescendantCounts,
    IReadOnlyList<FamilyBuffUsage> UsedBuffs);

public sealed record FamilyCommandResult(
    FamilyCommandStatus Status,
    FamilyState? Family = null,
    FamilyMemberState? Member = null,
    FamilyBuffEntry? Buff = null,
    FamilyUpdateKind? UpdateKind = null,
    FamilyWarpTarget? Warp = null,
    IReadOnlyList<int>? AffectedCharacterIds = null)
{
    public bool Succeeded => Status == FamilyCommandStatus.Success;

    public IReadOnlyList<int> AffectedCharacters => AffectedCharacterIds ?? Array.Empty<int>();
}

public sealed class FamilyService : IFamilyRegistry
{
    public const int MinimumJuniorLevel = 10;
    public const int MaximumJuniorLevelGap = 20;
    public const int MaximumJuniors = 2;
    public const int OnlinePedigreeBuffRequiredCount = 7;

    private readonly IFamilyRepository _repository;
    private readonly object _sync = new();
    private readonly Dictionary<int, Family> _families = new();
    private readonly Dictionary<int, int> _familyByCharacter = new();
    private readonly Dictionary<int, OnlineFamilyPlayer> _onlinePlayers = new();
    private readonly Dictionary<int, PendingFamilyInvite> _invitesByTarget = new();
    private readonly Dictionary<int, string> _pendingSummonsByTarget = new();
    private int _nextFamilyId;

    public FamilyService(IFamilyRepository repository, int firstFamilyId = 1)
    {
        if (firstFamilyId <= 0) throw new ArgumentOutOfRangeException(nameof(firstFamilyId));

        _repository = repository;
        _nextFamilyId = firstFamilyId;
    }

    public Family CreateFamily(int leaderId)
    {
        lock (_sync)
        {
            var family = new Family
            {
                Id = AllocateFamilyIdLocked(),
                LeaderId = leaderId,
            };
            TrackFamilyLocked(family);
            return family;
        }
    }

    public async Task<Family> CreateFamilyAsync(int leaderId, CancellationToken ct = default)
    {
        var family = CreateFamily(leaderId);
        await _repository.SaveAsync(family, ct).ConfigureAwait(false);
        return family;
    }

    public FamilyCommandResult InviteToFamily(Player inviterPlayer, Player targetPlayer)
    {
        ArgumentNullException.ThrowIfNull(inviterPlayer);
        ArgumentNullException.ThrowIfNull(targetPlayer);

        lock (_sync)
        {
            var validation = ValidateInviteLocked(inviterPlayer, targetPlayer);
            if (validation != FamilyCommandStatus.Success)
            {
                return new FamilyCommandResult(validation, GetFamilyForCharacterLocked(inviterPlayer.Character.Id)?.Snapshot(OnlineChannelsLocked()));
            }

            _invitesByTarget[targetPlayer.Character.Id] = new PendingFamilyInvite(
                inviterPlayer.Character.Id,
                inviterPlayer.Character.Name,
                DateTimeOffset.UtcNow.AddMinutes(5));

            return new FamilyCommandResult(
                FamilyCommandStatus.Success,
                GetFamilyForCharacterLocked(inviterPlayer.Character.Id)?.Snapshot(OnlineChannelsLocked()),
                ToState(MemberFromCharacter(targetPlayer.Character), false, -1),
                UpdateKind: FamilyUpdateKind.InviteCreated,
                AffectedCharacterIds: [targetPlayer.Character.Id]);
        }
    }

    public async Task<FamilyCommandResult> AcceptInviteAsync(int inviterCharId, Player targetPlayer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targetPlayer);

        Family? familyToSave;
        Family? familyToDelete;
        FamilyCommandResult result;

        lock (_sync)
        {
            if (!_invitesByTarget.Remove(targetPlayer.Character.Id, out var invite) || invite.InviterId != inviterCharId || invite.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return new FamilyCommandResult(FamilyCommandStatus.InvalidInvite);
            }

            if (!_onlinePlayers.TryGetValue(inviterCharId, out var inviterOnline))
            {
                return new FamilyCommandResult(FamilyCommandStatus.TargetNotFound);
            }

            var inviterPlayer = inviterOnline.Player;
            var validation = ValidateInviteLocked(inviterPlayer, targetPlayer);
            if (validation != FamilyCommandStatus.Success)
            {
                return new FamilyCommandResult(validation);
            }

            var inviterFamily = GetFamilyForCharacterLocked(inviterCharId);
            var oldTargetFamily = GetFamilyForCharacterLocked(targetPlayer.Character.Id);
            if (inviterFamily is null)
            {
                inviterFamily = new Family
                {
                    Id = AllocateFamilyIdLocked(),
                    LeaderId = inviterCharId,
                };
                inviterFamily.TryAddMember(MemberFromCharacter(inviterPlayer.Character));
                TrackFamilyLocked(inviterFamily);
            }

            var inviterMember = inviterFamily.GetMember(inviterCharId);
            if (inviterMember is null)
            {
                inviterMember = MemberFromCharacter(inviterPlayer.Character);
                inviterFamily.TryAddMember(inviterMember);
            }

            if (!inviterMember.TryAddJunior(targetPlayer.Character.Id))
            {
                return new FamilyCommandResult(FamilyCommandStatus.TooManyJuniors, inviterFamily.Snapshot(OnlineChannelsLocked()));
            }

            var targetMember = MemberFromCharacter(targetPlayer.Character);
            targetMember.SetSenior(inviterCharId);

            if (oldTargetFamily is not null && oldTargetFamily.Id != inviterFamily.Id)
            {
                foreach (var member in oldTargetFamily.Members.Values.ToArray())
                {
                    if (member.CharacterId == targetMember.CharacterId)
                    {
                        member.SetSenior(inviterCharId);
                    }

                    inviterFamily.Members[member.CharacterId] = member;
                    _familyByCharacter[member.CharacterId] = inviterFamily.Id;
                    ApplyFamilyToOnlineCharacterLocked(member, inviterFamily.Id);
                }

                UntrackFamilyLocked(oldTargetFamily.Id);
                familyToDelete = oldTargetFamily;
            }
            else
            {
                inviterFamily.Members[targetMember.CharacterId] = targetMember;
                _familyByCharacter[targetMember.CharacterId] = inviterFamily.Id;
                familyToDelete = null;
            }

            ApplyFamilyToCharacter(inviterPlayer.Character, inviterFamily.Id, inviterMember);
            ApplyFamilyToCharacter(targetPlayer.Character, inviterFamily.Id, targetMember);
            TrackFamilyLocked(inviterFamily);

            familyToSave = inviterFamily;
            result = new FamilyCommandResult(
                FamilyCommandStatus.Success,
                inviterFamily.Snapshot(OnlineChannelsLocked()),
                ToState(targetMember, true, _onlinePlayers.GetValueOrDefault(targetMember.CharacterId).Channel),
                UpdateKind: oldTargetFamily is null && inviterFamily.Members.Count == 2 ? FamilyUpdateKind.Created : FamilyUpdateKind.Joined,
                AffectedCharacterIds: inviterFamily.Members.Keys.ToArray());
        }

        if (familyToDelete is not null)
        {
            await _repository.DeleteAsync(familyToDelete.Id, ct).ConfigureAwait(false);
        }

        await _repository.SaveAsync(familyToSave, ct).ConfigureAwait(false);
        return result;
    }

    public FamilyCommandResult DenyInvite(int inviterCharId, Player targetPlayer)
    {
        ArgumentNullException.ThrowIfNull(targetPlayer);

        lock (_sync)
        {
            if (_invitesByTarget.TryGetValue(targetPlayer.Character.Id, out var invite) && invite.InviterId == inviterCharId)
            {
                _invitesByTarget.Remove(targetPlayer.Character.Id);
                return new FamilyCommandResult(FamilyCommandStatus.Success, UpdateKind: FamilyUpdateKind.InviteDenied, AffectedCharacterIds: [inviterCharId]);
            }

            return new FamilyCommandResult(FamilyCommandStatus.InvalidInvite);
        }
    }

    public async Task<FamilyCommandResult> DeleteJuniorAsync(Player player, int juniorId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        Family? saveFamily;
        Family? deleteFamily;
        FamilyCommandResult result;

        lock (_sync)
        {
            var family = GetFamilyForCharacterLocked(player.Character.Id);
            var member = family?.GetMember(player.Character.Id);
            var junior = family?.GetMember(juniorId);
            if (family is null || member is null || junior is null || !member.HasJunior(juniorId))
            {
                return new FamilyCommandResult(FamilyCommandStatus.InvalidOperation, family?.Snapshot(OnlineChannelsLocked()));
            }

            member.RemoveJunior(juniorId);
            junior.SetSenior(0);
            ApplyFamilyToCharacter(player.Character, family.Id, member);
            ApplyFamilyToOnlineCharacterLocked(junior, family.Id);
            var split = SplitFamilyLocked(family, juniorId);
            saveFamily = split.SaveFamily;
            deleteFamily = split.DeleteFamily;
            result = new FamilyCommandResult(
                FamilyCommandStatus.Success,
                saveFamily?.Snapshot(OnlineChannelsLocked()),
                ToState(junior, _onlinePlayers.ContainsKey(junior.CharacterId), _onlinePlayers.GetValueOrDefault(junior.CharacterId).Channel),
                UpdateKind: FamilyUpdateKind.JuniorDeleted,
                AffectedCharacterIds: [player.Character.Id, juniorId]);
        }

        await SaveSplitAsync(saveFamily, deleteFamily, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<FamilyCommandResult> DeleteSeniorAsync(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        Family? saveFamily;
        Family? deleteFamily;
        FamilyCommandResult result;

        lock (_sync)
        {
            var family = GetFamilyForCharacterLocked(player.Character.Id);
            var member = family?.GetMember(player.Character.Id);
            var senior = member is null ? null : family?.GetMember(member.SeniorId);
            if (family is null || member is null || senior is null || member.SeniorId <= 0)
            {
                return new FamilyCommandResult(FamilyCommandStatus.NotInFamily, family?.Snapshot(OnlineChannelsLocked()));
            }

            senior.RemoveJunior(member.CharacterId);
            member.SetSenior(0);
            ApplyFamilyToOnlineCharacterLocked(senior, family.Id);
            ApplyFamilyToCharacter(player.Character, family.Id, member);
            var split = SplitFamilyLocked(family, member.CharacterId);
            saveFamily = split.SaveFamily;
            deleteFamily = split.DeleteFamily;
            result = new FamilyCommandResult(
                FamilyCommandStatus.Success,
                saveFamily?.Snapshot(OnlineChannelsLocked()),
                ToState(member, true, _onlinePlayers.GetValueOrDefault(member.CharacterId).Channel),
                UpdateKind: FamilyUpdateKind.SeniorDeleted,
                AffectedCharacterIds: [player.Character.Id, senior.CharacterId]);
        }

        await SaveSplitAsync(saveFamily, deleteFamily, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<FamilyCommandResult> UseFamilyBuffAsync(Player player, int buffType, Player? targetPlayer = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        Family? familyToSave = null;
        FamilyCommandResult result;

        lock (_sync)
        {
            var entry = FamilyBuff.Find(buffType);
            if (entry is null)
            {
                return new FamilyCommandResult(FamilyCommandStatus.InvalidBuff);
            }

            var family = GetFamilyForCharacterLocked(player.Character.Id);
            var member = family?.GetMember(player.Character.Id);
            if (family is null || member is null)
            {
                return new FamilyCommandResult(FamilyCommandStatus.NotInFamily, Buff: entry);
            }

            if (buffType is 0 or 1)
            {
                result = UseFamilyTeleportOrSummonLocked(player, targetPlayer, family, member, entry);
                familyToSave = result.UpdateKind == FamilyUpdateKind.BuffUsed ? family : null;
            }
            else
            {
                result = UseFamilyTimedBuffLocked(player, family, member, entry);
                familyToSave = result.Succeeded ? family : null;
            }
        }

        if (familyToSave is not null)
        {
            await _repository.SaveAsync(familyToSave, ct).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<FamilyCommandResult> SetFamilyPreceptAsync(Player player, string notice, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        Family? familyToSave;
        FamilyCommandResult result;

        lock (_sync)
        {
            var family = GetFamilyForCharacterLocked(player.Character.Id);
            if (family is null)
            {
                return new FamilyCommandResult(FamilyCommandStatus.NotInFamily);
            }

            if (family.LeaderId != player.Character.Id)
            {
                return new FamilyCommandResult(FamilyCommandStatus.NotLeader, family.Snapshot(OnlineChannelsLocked()));
            }

            family.SetNotice(notice);
            familyToSave = family;
            result = new FamilyCommandResult(
                FamilyCommandStatus.Success,
                family.Snapshot(OnlineChannelsLocked()),
                UpdateKind: FamilyUpdateKind.PreceptChanged,
                AffectedCharacterIds: family.Members.Keys.ToArray());
        }

        await _repository.SaveAsync(familyToSave, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<FamilyCommandResult> HandleFamilySummonAsync(Player player, bool accepted, string summonerName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        Family? familyToSave = null;
        FamilyCommandResult result;

        lock (_sync)
        {
            var entry = FamilyBuff.Find(1)!;
            if (!_pendingSummonsByTarget.TryGetValue(player.Character.Id, out var pendingSummoner) ||
                !string.Equals(pendingSummoner, summonerName, StringComparison.OrdinalIgnoreCase))
            {
                return new FamilyCommandResult(FamilyCommandStatus.InvalidInvite, Buff: entry);
            }

            _pendingSummonsByTarget.Remove(player.Character.Id);

            if (!accepted)
            {
                return new FamilyCommandResult(FamilyCommandStatus.Success, Buff: entry, UpdateKind: FamilyUpdateKind.SummonDenied);
            }

            var summoner = _onlinePlayers.Values.FirstOrDefault(p => string.Equals(p.Player.Character.Name, summonerName, StringComparison.OrdinalIgnoreCase));
            if (summoner.Player is null)
            {
                return new FamilyCommandResult(FamilyCommandStatus.TargetNotFound, Buff: entry);
            }

            var family = GetFamilyForCharacterLocked(player.Character.Id);
            var summonerMember = family?.GetMember(summoner.Player.Character.Id);
            if (family is null || summonerMember is null || summoner.Player.Character.FamilyId != player.Character.FamilyId)
            {
                return new FamilyCommandResult(FamilyCommandStatus.NotSameFamily, Buff: entry);
            }

            if (summonerMember.CurrentRep <= entry.RepCost || !summonerMember.TrySpendRep(entry.RepCost))
            {
                return new FamilyCommandResult(FamilyCommandStatus.NotEnoughRep, family.Snapshot(OnlineChannelsLocked()), Buff: entry);
            }

            ApplyFamilyToCharacter(summoner.Player.Character, family.Id, summonerMember);
            familyToSave = family;
            result = new FamilyCommandResult(
                FamilyCommandStatus.Success,
                family.Snapshot(OnlineChannelsLocked()),
                ToState(summonerMember, true, summoner.Channel),
                entry,
                FamilyUpdateKind.SummonAccepted,
                new FamilyWarpTarget(player.Character.Id, summoner.Player.Character.MapId, summoner.Player.Position),
                [player.Character.Id, summoner.Player.Character.Id]);
        }

        if (familyToSave is not null)
        {
            await _repository.SaveAsync(familyToSave, ct).ConfigureAwait(false);
        }

        return result;
    }

    public FamilyInfoData GetFamilyInfo(int characterId)
    {
        lock (_sync)
        {
            var family = GetFamilyForCharacterLocked(characterId);
            var member = family?.GetMember(characterId);
            if (family is null || member is null)
            {
                var player = _onlinePlayers.GetValueOrDefault(characterId).Player;
                return new FamilyInfoData(
                    characterId,
                    player?.Character.CurrentRep ?? 0,
                    player?.Character.TotalRep ?? 0,
                    0,
                    0,
                    string.Empty,
                    string.Empty,
                    Array.Empty<FamilyBuffUsage>());
            }

            var leader = family.GetLeader();
            return new FamilyInfoData(
                characterId,
                member.CurrentRep,
                member.TotalRep,
                member.JuniorCount,
                family.LeaderId,
                leader?.Name ?? string.Empty,
                family.Notice,
                Array.Empty<FamilyBuffUsage>());
        }
    }

    public FamilyPedigreeData GetFamilyPedigree(int characterId)
    {
        lock (_sync)
        {
            var family = GetFamilyForCharacterLocked(characterId);
            if (family is null)
            {
                var player = _onlinePlayers.GetValueOrDefault(characterId).Player;
                var self = player is null ? new FamilyMember { CharacterId = characterId } : MemberFromCharacter(player.Character);
                return new FamilyPedigreeData(
                    characterId,
                    [ToPedigreeData(self, _onlinePlayers.GetValueOrDefault(characterId).Channel, player is not null)],
                    2,
                    0,
                    0,
                    Array.Empty<FamilyDescendantData>(),
                    Array.Empty<FamilyBuffUsage>());
            }

            var members = family.GetPedigreeMembers(characterId)
                .Select(member =>
                {
                    var online = _onlinePlayers.TryGetValue(member.CharacterId, out var onlinePlayer);
                    return ToPedigreeData(member, online ? onlinePlayer.Channel : -1, online);
                })
                .ToArray();

            return new FamilyPedigreeData(
                characterId,
                members,
                CountDescendantSlots(family, characterId),
                family.GetGenerations(),
                family.Members.Count,
                GetDescendantCounts(family, characterId),
                Array.Empty<FamilyBuffUsage>());
        }
    }

    public async Task<FamilyCommandResult> SplitFamilyAsync(int characterId, CancellationToken ct = default)
    {
        Family? saveFamily;
        Family? deleteFamily;
        FamilyCommandResult result;

        lock (_sync)
        {
            var family = GetFamilyForCharacterLocked(characterId);
            if (family is null)
            {
                return new FamilyCommandResult(FamilyCommandStatus.NotInFamily);
            }

            var split = SplitFamilyLocked(family, characterId);
            saveFamily = split.SaveFamily;
            deleteFamily = split.DeleteFamily;
            result = new FamilyCommandResult(
                FamilyCommandStatus.Success,
                saveFamily?.Snapshot(OnlineChannelsLocked()),
                UpdateKind: FamilyUpdateKind.SeniorDeleted);
        }

        await SaveSplitAsync(saveFamily, deleteFamily, ct).ConfigureAwait(false);
        return result;
    }

    public FamilyState? GetFamilyForCharacter(int characterId)
    {
        lock (_sync)
        {
            return GetFamilyForCharacterLocked(characterId)?.Snapshot(OnlineChannelsLocked());
        }
    }

    public FamilyState? GetFamily(int familyId)
    {
        lock (_sync)
        {
            return _families.TryGetValue(familyId, out var family) ? family.Snapshot(OnlineChannelsLocked()) : null;
        }
    }

    public void Register(Family family)
    {
        ArgumentNullException.ThrowIfNull(family);

        lock (_sync)
        {
            TrackFamilyLocked(family);
        }
    }

    public void Register(Player player, int channel = 1)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_sync)
        {
            _onlinePlayers[player.Character.Id] = new OnlineFamilyPlayer(player, channel);
            var family = GetFamilyForCharacterLocked(player.Character.Id);
            var member = family?.GetMember(player.Character.Id);
            if (family is not null && member is not null)
            {
                member.Name = player.Character.Name;
                member.Level = player.Character.Level;
                member.Job = player.Character.Job;
                member.CurrentRep = player.Character.CurrentRep;
                member.TotalRep = player.Character.TotalRep;
                member.SeniorId = player.Character.SeniorId;
                member.Junior1 = player.Character.Junior1;
                member.Junior2 = player.Character.Junior2;
            }
        }
    }

    public void Unregister(int characterId)
    {
        lock (_sync)
        {
            _onlinePlayers.Remove(characterId);
        }
    }

    /// <summary>
    /// 對照 Java <c>MapleFamily.setOnline</c>：登入/登出時同步線上狀態，狀態實際翻轉（上線↔離線）
    /// 才回傳需要收到 <c>FamilyPacket.familyLoggedIn</c> 通知的對象——leader 上下線通知全家族在線
    /// 成員，其餘成員只通知自己的「族譜可視範圍」（<see cref="Family.GetPedigreeMembers"/>，對照 Java
    /// <c>mgc.getPedigree()</c>），皆排除自己且只送給目前在線的人。
    /// </summary>
    public FamilyOnlineStatusChange SetOnline(Player player, bool online, int channel)
    {
        ArgumentNullException.ThrowIfNull(player);

        lock (_sync)
        {
            var wasOnline = _onlinePlayers.ContainsKey(player.Character.Id);
            if (online)
            {
                Register(player, channel);
            }
            else
            {
                Unregister(player.Character.Id);
            }

            var family = GetFamilyForCharacterLocked(player.Character.Id);
            var member = family?.GetMember(player.Character.Id);
            if (family is null || member is null || wasOnline == online)
            {
                return FamilyOnlineStatusChange.None;
            }

            var recipients = member.CharacterId == family.LeaderId
                ? family.Members.Keys
                    .Where(id => id != member.CharacterId && _onlinePlayers.ContainsKey(id))
                    .ToArray()
                : family.GetPedigreeMembers(member.CharacterId)
                    .Where(m => m.CharacterId != member.CharacterId && _onlinePlayers.ContainsKey(m.CharacterId))
                    .Select(static m => m.CharacterId)
                    .ToArray();

            return new FamilyOnlineStatusChange(true, online, player.Character.Name, recipients);
        }
    }

    private FamilyCommandStatus ValidateInviteLocked(Player inviterPlayer, Player targetPlayer)
    {
        if (inviterPlayer.Character.Id == targetPlayer.Character.Id)
        {
            return FamilyCommandStatus.InvalidOperation;
        }

        if (targetPlayer.Character.FamilyId > 0 && targetPlayer.Character.FamilyId == inviterPlayer.Character.FamilyId)
        {
            return FamilyCommandStatus.AlreadyInFamily;
        }

        if (inviterPlayer.Character.MapId != targetPlayer.Character.MapId)
        {
            return FamilyCommandStatus.NotSameMap;
        }

        if (targetPlayer.Character.SeniorId != 0)
        {
            return FamilyCommandStatus.TargetHasSenior;
        }

        if (targetPlayer.Character.Level >= inviterPlayer.Character.Level)
        {
            return FamilyCommandStatus.InvalidLevel;
        }

        if (targetPlayer.Character.Level < inviterPlayer.Character.Level - MaximumJuniorLevelGap)
        {
            return FamilyCommandStatus.LevelGapTooWide;
        }

        if (targetPlayer.Character.Level < MinimumJuniorLevel || inviterPlayer.Character.Level < MinimumJuniorLevel)
        {
            return FamilyCommandStatus.TargetTooLowLevel;
        }

        var inviterFamily = GetFamilyForCharacterLocked(inviterPlayer.Character.Id);
        var inviterMember = inviterFamily?.GetMember(inviterPlayer.Character.Id) ?? MemberFromCharacter(inviterPlayer.Character);
        return inviterMember.JuniorCount >= MaximumJuniors ? FamilyCommandStatus.TooManyJuniors : FamilyCommandStatus.Success;
    }

    private FamilyCommandResult UseFamilyTeleportOrSummonLocked(
        Player player,
        Player? targetPlayer,
        Family family,
        FamilyMember member,
        FamilyBuffEntry entry)
    {
        if (targetPlayer is null)
        {
            return new FamilyCommandResult(FamilyCommandStatus.TargetNotFound, family.Snapshot(OnlineChannelsLocked()), ToState(member, true, _onlinePlayers.GetValueOrDefault(member.CharacterId).Channel), entry);
        }

        if (targetPlayer.Character.Id == player.Character.Id || targetPlayer.Character.FamilyId != player.Character.FamilyId)
        {
            return new FamilyCommandResult(FamilyCommandStatus.NotSameFamily, family.Snapshot(OnlineChannelsLocked()), ToState(member, true, _onlinePlayers.GetValueOrDefault(member.CharacterId).Channel), entry);
        }

        if (entry.Type == 1)
        {
            _pendingSummonsByTarget[targetPlayer.Character.Id] = player.Character.Name;
            return new FamilyCommandResult(
                FamilyCommandStatus.Success,
                family.Snapshot(OnlineChannelsLocked()),
                ToState(member, true, _onlinePlayers.GetValueOrDefault(member.CharacterId).Channel),
                entry,
                FamilyUpdateKind.SummonRequested,
                AffectedCharacterIds: [targetPlayer.Character.Id]);
        }

        return new FamilyCommandResult(
            FamilyCommandStatus.Success,
            family.Snapshot(OnlineChannelsLocked()),
            ToState(member, true, _onlinePlayers.GetValueOrDefault(member.CharacterId).Channel),
            entry,
            FamilyUpdateKind.BuffUsed,
            new FamilyWarpTarget(player.Character.Id, targetPlayer.Character.MapId, targetPlayer.Position),
            [player.Character.Id]);
    }

    private FamilyCommandResult UseFamilyTimedBuffLocked(Player player, Family family, FamilyMember member, FamilyBuffEntry entry)
    {
        if (member.CurrentRep <= entry.RepCost)
        {
            return new FamilyCommandResult(FamilyCommandStatus.NotEnoughRep, family.Snapshot(OnlineChannelsLocked()), ToState(member, true, _onlinePlayers.GetValueOrDefault(member.CharacterId).Channel), entry);
        }

        IReadOnlyList<int> affected = entry.Type switch
        {
            4 => member.GetOnlineJuniors(family, _onlinePlayers.Keys.ToHashSet()).Select(static m => m.CharacterId).ToArray(),
            _ => [player.Character.Id],
        };

        if (entry.Type == 4 && affected.Count < OnlinePedigreeBuffRequiredCount)
        {
            return new FamilyCommandResult(FamilyCommandStatus.NotEnoughOnlinePedigree, family.Snapshot(OnlineChannelsLocked()), ToState(member, true, _onlinePlayers.GetValueOrDefault(member.CharacterId).Channel), entry);
        }

        member.TrySpendRep(entry.RepCost);
        ApplyFamilyToCharacter(player.Character, family.Id, member);
        return new FamilyCommandResult(
            FamilyCommandStatus.Success,
            family.Snapshot(OnlineChannelsLocked()),
            ToState(member, true, _onlinePlayers.GetValueOrDefault(member.CharacterId).Channel),
            entry,
            FamilyUpdateKind.BuffUsed,
            AffectedCharacterIds: affected);
    }

    private FamilySplitResult SplitFamilyLocked(Family family, int characterId)
    {
        var member = family.GetMember(characterId);
        if (member is null)
        {
            return new FamilySplitResult(family, null);
        }

        var subtree = member.GetAllJuniors(family);
        if (subtree.Count <= 1)
        {
            family.RemoveMember(characterId);
            _familyByCharacter.Remove(characterId);
            ClearOnlineFamilyStatusLocked(characterId);
            return FinalizeOldFamilyAfterSplitLocked(family);
        }

        var newFamily = new Family
        {
            Id = AllocateFamilyIdLocked(),
            LeaderId = characterId,
            Notice = family.Notice,
        };

        foreach (var moving in subtree)
        {
            family.RemoveMember(moving.CharacterId);
            newFamily.Members[moving.CharacterId] = moving;
            _familyByCharacter[moving.CharacterId] = newFamily.Id;
            ApplyFamilyToOnlineCharacterLocked(moving, newFamily.Id);
        }

        TrackFamilyLocked(newFamily);
        var oldFinalize = FinalizeOldFamilyAfterSplitLocked(family);
        return new FamilySplitResult(newFamily, oldFinalize.DeleteFamily);
    }

    private FamilySplitResult FinalizeOldFamilyAfterSplitLocked(Family family)
    {
        if (family.Members.Count <= 1)
        {
            foreach (var member in family.Members.Values.ToArray())
            {
                _familyByCharacter.Remove(member.CharacterId);
                ClearOnlineFamilyStatusLocked(member.CharacterId);
            }

            UntrackFamilyLocked(family.Id);
            return new FamilySplitResult(null, family);
        }

        TrackFamilyLocked(family);
        return new FamilySplitResult(family, null);
    }

    private async Task SaveSplitAsync(Family? saveFamily, Family? deleteFamily, CancellationToken ct)
    {
        if (deleteFamily is not null)
        {
            await _repository.DeleteAsync(deleteFamily.Id, ct).ConfigureAwait(false);
        }

        if (saveFamily is not null)
        {
            await _repository.SaveAsync(saveFamily, ct).ConfigureAwait(false);
        }
    }

    private Family? GetFamilyForCharacterLocked(int characterId) =>
        _familyByCharacter.TryGetValue(characterId, out var familyId) && _families.TryGetValue(familyId, out var family)
            ? family
            : null;

    private void TrackFamilyLocked(Family family)
    {
        UntrackFamilyLocked(family.Id);
        _families[family.Id] = family;
        foreach (var member in family.Members.Values)
        {
            _familyByCharacter[member.CharacterId] = family.Id;
        }

        if (family.Id >= _nextFamilyId)
        {
            _nextFamilyId = family.Id + 1;
        }
    }

    private void UntrackFamilyLocked(int familyId)
    {
        _families.Remove(familyId);
        foreach (var characterId in _familyByCharacter.Where(pair => pair.Value == familyId).Select(static pair => pair.Key).ToArray())
        {
            _familyByCharacter.Remove(characterId);
        }
    }

    private int AllocateFamilyIdLocked()
    {
        while (_families.ContainsKey(_nextFamilyId))
        {
            _nextFamilyId++;
        }

        return _nextFamilyId++;
    }

    private IReadOnlyDictionary<int, int> OnlineChannelsLocked() =>
        _onlinePlayers.ToDictionary(static pair => pair.Key, static pair => pair.Value.Channel);

    private static FamilyMember MemberFromCharacter(Character character) => new()
    {
        CharacterId = character.Id,
        Name = character.Name,
        SeniorId = character.SeniorId,
        Junior1 = character.Junior1,
        Junior2 = character.Junior2,
        CurrentRep = character.CurrentRep,
        TotalRep = character.TotalRep,
        Level = character.Level,
        Job = character.Job,
    };

    private static FamilyMemberState ToState(FamilyMember member, bool online, int channel) => new(
        member.CharacterId,
        member.Name,
        member.SeniorId,
        member.Junior1,
        member.Junior2,
        member.CurrentRep,
        member.TotalRep,
        member.Level,
        member.Job,
        online,
        online ? channel : -1);

    private static FamilyPedigreeMemberData ToPedigreeData(FamilyMember member, int channel, bool online) => new(
        member.CharacterId,
        member.SeniorId,
        member.Job,
        member.Level,
        online,
        member.CurrentRep,
        member.TotalRep,
        online ? channel : -1,
        member.Name);

    private static void ApplyFamilyToCharacter(Character character, int familyId, FamilyMember member)
    {
        character.FamilyId = familyId;
        character.SeniorId = member.SeniorId;
        character.Junior1 = member.Junior1;
        character.Junior2 = member.Junior2;
        character.CurrentRep = member.CurrentRep;
        character.TotalRep = member.TotalRep;
    }

    private void ApplyFamilyToOnlineCharacterLocked(FamilyMember member, int familyId)
    {
        if (_onlinePlayers.TryGetValue(member.CharacterId, out var online))
        {
            ApplyFamilyToCharacter(online.Player.Character, familyId, member);
        }
    }

    private void ClearOnlineFamilyStatusLocked(int characterId)
    {
        if (_onlinePlayers.TryGetValue(characterId, out var online))
        {
            online.Player.Character.FamilyId = 0;
            online.Player.Character.SeniorId = 0;
            online.Player.Character.Junior1 = 0;
            online.Player.Character.Junior2 = 0;
        }
    }

    private long CountDescendantSlots(Family family, int characterId)
    {
        var member = family.GetMember(characterId);
        if (member is null)
        {
            return 2;
        }

        var count = 2;
        foreach (var juniorId in new[] { member.Junior1, member.Junior2 })
        {
            var junior = family.GetMember(juniorId);
            if (junior?.Junior1 > 0)
            {
                count++;
            }

            if (junior?.Junior2 > 0)
            {
                count++;
            }
        }

        return count;
    }

    private IReadOnlyList<FamilyDescendantData> GetDescendantCounts(Family family, int characterId)
    {
        var member = family.GetMember(characterId);
        if (member is null)
        {
            return Array.Empty<FamilyDescendantData>();
        }

        var result = new List<FamilyDescendantData>();
        foreach (var juniorId in new[] { member.Junior1, member.Junior2 })
        {
            var junior = family.GetMember(juniorId);
            if (junior?.Junior1 > 0)
            {
                result.Add(new FamilyDescendantData(junior.Junior1, family.GetDescendantCount(junior.Junior1)));
            }

            if (junior?.Junior2 > 0)
            {
                result.Add(new FamilyDescendantData(junior.Junior2, family.GetDescendantCount(junior.Junior2)));
            }
        }

        return result;
    }

    private readonly record struct OnlineFamilyPlayer(Player Player, int Channel);

    private readonly record struct PendingFamilyInvite(int InviterId, string InviterName, DateTimeOffset ExpiresAt);

    private readonly record struct FamilySplitResult(Family? SaveFamily, Family? DeleteFamily);
}
